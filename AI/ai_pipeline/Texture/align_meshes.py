import argparse
import copy
import json
import os
import sys
from typing import Tuple

import numpy as np
import open3d as o3d


def _get_windows_short_path(path: str) -> str:
    """Get Windows short path (8.3 format) to avoid encoding issues with non-ASCII characters."""
    if sys.platform != 'win32':
        return path
    
    try:
        import ctypes
        from ctypes import wintypes
        
        # GetShortPathNameW is the Unicode version
        GetShortPathNameW = ctypes.windll.kernel32.GetShortPathNameW
        GetShortPathNameW.argtypes = [wintypes.LPCWSTR, wintypes.LPWSTR, wintypes.DWORD]
        GetShortPathNameW.restype = wintypes.DWORD
        
        # Call with a buffer to get the short path
        buffer = ctypes.create_unicode_buffer(260)  # MAX_PATH
        result = GetShortPathNameW(path, buffer, 260)
        
        if result == 0:
            # If GetShortPathName fails, return original path
            return path
        return buffer.value
    except Exception:
        # If anything fails, return original path
        return path


def _load_mesh(path: str) -> o3d.geometry.TriangleMesh:
    # Handle Windows path encoding issues with non-ASCII characters
    import pathlib
    
    # Convert to pathlib.Path which handles encoding better on Windows
    path_obj = pathlib.Path(path)
    
    # Ensure the path exists
    if not path_obj.exists():
        raise FileNotFoundError(f"Mesh file not found: {path}")
    
    # On Windows, handle path encoding issues when passing to C++ libraries
    if sys.platform == 'win32':
        try:
            # Use resolve() to get absolute path, then convert to string
            # This helps normalize the path encoding
            normalized_path = str(path_obj.resolve())
        except (OSError, ValueError):
            # If resolve() fails, use the original path
            normalized_path = str(path_obj)
        
        # Try to use the path as-is first
        try:
            mesh = o3d.io.read_triangle_mesh(normalized_path)
        except (UnicodeDecodeError, UnicodeError) as e:
            # If encoding fails, try using Windows short path (8.3 format)
            # This avoids encoding issues with non-ASCII characters
            try:
                short_path = _get_windows_short_path(normalized_path)
                if short_path != normalized_path:
                    mesh = o3d.io.read_triangle_mesh(short_path)
                else:
                    # If short path is same, try with os.fsencode/fsdecode
                    path_bytes = os.fsencode(normalized_path)
                    fs_path = os.fsdecode(path_bytes)
                    mesh = o3d.io.read_triangle_mesh(fs_path)
            except Exception:
                # Last resort: try with the original path
                mesh = o3d.io.read_triangle_mesh(str(path_obj))
        except Exception as e:
            raise RuntimeError(f"Failed to load mesh from {path}: {str(e)}") from e
    else:
        # On non-Windows systems, use resolve() normally
        normalized_path = str(path_obj.resolve())
        mesh = o3d.io.read_triangle_mesh(normalized_path)
    
    if mesh.is_empty():
        raise ValueError(f"Mesh is empty or unreadable: {path}")
    mesh.compute_vertex_normals()
    return mesh


def _mesh_diagnostics(mesh: o3d.geometry.TriangleMesh) -> Tuple[np.ndarray, float]:
    bbox = mesh.get_axis_aligned_bounding_box()
    extent = bbox.get_extent()
    diag = float(np.linalg.norm(extent))
    center = bbox.get_center()
    return np.asarray(center), diag if diag > 0 else 1.0


def _sample_points(mesh: o3d.geometry.TriangleMesh, samples: int) -> o3d.geometry.PointCloud:
    count = max(5000, min(samples, len(mesh.vertices) * 50 or samples))
    try:
        pcd = mesh.sample_points_poisson_disk(count)
    except RuntimeError:
        pcd = mesh.sample_points_uniformly(count)
    pcd.estimate_normals()
    return pcd


def _downsample(pcd: o3d.geometry.PointCloud, voxel: float) -> o3d.geometry.PointCloud:
    if voxel <= 0:
        down = pcd
    else:
        down = pcd.voxel_down_sample(voxel)
        if down.is_empty():
            down = pcd
    down.estimate_normals()
    return down


def _icp_multi_scale(src_pcd, tgt_pcd, base_voxel: float, max_iter: int) -> Tuple[np.ndarray, dict]:
    estimation = o3d.pipelines.registration.TransformationEstimationPointToPlane()
    current = np.eye(4)
    stats = {}

    radii = [base_voxel * 4.0, base_voxel * 2.0, base_voxel]
    radii = [max(r, base_voxel * 0.5, 1e-3) for r in radii]
    iter_schedule = [
        max(20, max_iter // 2),
        max(15, max_iter // 1.5),
        max(10, max_iter),
    ]

    for idx, (radius, iters) in enumerate(zip(radii, iter_schedule), start=1):
        src_down = _downsample(src_pcd, radius)
        tgt_down = _downsample(tgt_pcd, radius)

        criteria = o3d.pipelines.registration.ICPConvergenceCriteria(max_iteration=int(iters))
        reg = o3d.pipelines.registration.registration_icp(
            src_down,
            tgt_down,
            max_correspondence_distance=radius * 2.5,
            init=current,
            estimation_method=estimation,
            criteria=criteria,
        )
        current = reg.transformation
        stats[f"stage_{idx}"] = {
            "radius": radius,
            "iterations": iters,
            "fitness": reg.fitness,
            "rmse": reg.inlier_rmse,
        }

    return current, stats


def align(hi_path: str, lo_path: str, out_mesh: str, report_path: str, samples: int, voxel_ratio: float, max_iter: int) -> dict:
    hi_mesh = _load_mesh(hi_path)
    lo_mesh = _load_mesh(lo_path)

    hi_center, hi_diag = _mesh_diagnostics(hi_mesh)
    lo_center, lo_diag = _mesh_diagnostics(lo_mesh)
    diag = max(hi_diag, lo_diag, 1e-3)
    base_voxel = max(voxel_ratio * diag, diag * 0.005, 5e-4)

    print(f"[Align] HI diag={hi_diag:.4f}, LO diag={lo_diag:.4f}, base voxel={base_voxel:.5f}")

    hi_pcd = _sample_points(hi_mesh, samples)
    lo_pcd = _sample_points(lo_mesh, samples)

    init = np.eye(4)
    init[:3, 3] = hi_center - lo_center
    lo_pcd.transform(init)

    transform, stats = _icp_multi_scale(lo_pcd, hi_pcd, base_voxel, max_iter)
    final_transform = transform @ init

    aligned_mesh = copy.deepcopy(lo_mesh)
    aligned_mesh.transform(final_transform)

    out_mesh = os.path.abspath(out_mesh)
    out_dir = os.path.dirname(out_mesh)
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)
    
    # Handle Windows path encoding for write operation
    write_path = out_mesh
    if sys.platform == 'win32':
        try:
            # Try writing with the path as-is first
            success = o3d.io.write_triangle_mesh(write_path, aligned_mesh, write_triangle_uvs=True)
        except (UnicodeDecodeError, UnicodeError):
            # If encoding fails, try using Windows short path
            try:
                short_path = _get_windows_short_path(write_path)
                if short_path != write_path:
                    write_path = short_path
                success = o3d.io.write_triangle_mesh(write_path, aligned_mesh, write_triangle_uvs=True)
            except Exception:
                # Last resort: try with filesystem encoding
                path_bytes = os.fsencode(write_path)
                fs_path = os.fsdecode(path_bytes)
                success = o3d.io.write_triangle_mesh(fs_path, aligned_mesh, write_triangle_uvs=True)
                write_path = fs_path
    else:
        success = o3d.io.write_triangle_mesh(write_path, aligned_mesh, write_triangle_uvs=True)
    
    if not success:
        raise RuntimeError(f"Failed to write aligned mesh: {out_mesh}")

    alignment_report = {
        "hi_mesh": os.path.abspath(hi_path),
        "lo_mesh": os.path.abspath(lo_path),
        "aligned_mesh": out_mesh,
        "voxel_ratio": voxel_ratio,
        "base_voxel": base_voxel,
        "samples": samples,
        "max_iter": max_iter,
        "transform": final_transform.tolist(),
        "stages": stats,
    }

    if report_path:
        report_path = os.path.abspath(report_path)
        report_dir = os.path.dirname(report_path)
        if report_dir:
            os.makedirs(report_dir, exist_ok=True)
        with open(report_path, "w", encoding="utf-8") as fh:
            json.dump(alignment_report, fh, indent=2)
    return alignment_report


def parse_args():
    parser = argparse.ArgumentParser(description="Rigid alignment between textured HI mesh and texture-less LO mesh using Open3D ICP.")
    parser.add_argument("--hi", required=True, help="High-poly GLB/OBJ path (texture donor).")
    parser.add_argument("--lo", required=True, help="Low-poly GLB/OBJ path (texture receiver).")
    parser.add_argument("--out-mesh", required=True, help="Path to write aligned low mesh (OBJ/PLY/GLB).")
    parser.add_argument("--report", required=True, help="JSON report with ICP stats and transformation matrix.")
    parser.add_argument("--samples", type=int, default=200000, help="Number of points to sample for ICP.")
    parser.add_argument("--voxel-ratio", type=float, default=0.02, help="Base voxel size as ratio of bbox diagonal.")
    parser.add_argument("--max-iter", type=int, default=60, help="Max iteration count for the finest ICP stage.")
    return parser.parse_args()


def main():
    args = parse_args()
    report = align(
        args.hi,
        args.lo,
        args.out_mesh,
        args.report,
        samples=args.samples,
        voxel_ratio=args.voxel_ratio,
        max_iter=args.max_iter,
    )
    print("[Align] Finished with fitness "
          f"{report['stages']['stage_3']['fitness']:.4f} / rmse {report['stages']['stage_3']['rmse']:.5f}")
    print(f"[Align] Result mesh -> {report['aligned_mesh']}")
    print(f"[Align] Report saved -> {os.path.abspath(args.report)}")


if __name__ == "__main__":
    main()
