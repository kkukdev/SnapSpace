import argparse
import os
import sys
import time
import subprocess
import shutil
import glob
from typing import Optional, List

import open3d as o3d


def _load_mesh(path: str) -> o3d.geometry.TriangleMesh:
    mesh = o3d.io.read_triangle_mesh(path)
    if mesh.is_empty():
        raise ValueError("Empty mesh: " + path)
    return mesh


def _preclean_mesh(mesh: o3d.geometry.TriangleMesh) -> o3d.geometry.TriangleMesh:
    mesh.remove_unreferenced_vertices()
    mesh.remove_degenerate_triangles()
    mesh.remove_duplicated_vertices()
    mesh.remove_duplicated_triangles()
    mesh.remove_non_manifold_edges()
    mesh.compute_vertex_normals()
    return mesh


def _smooth_mesh(mesh: o3d.geometry.TriangleMesh, algo: str, iterations: int) -> o3d.geometry.TriangleMesh:
    algo = algo.lower()
    iterations = max(1, int(iterations))
    if algo == "taubin":
        mesh = mesh.filter_smooth_taubin(number_of_iterations=iterations)
    elif algo == "laplacian":
        mesh = mesh.filter_smooth_laplacian(number_of_iterations=iterations)
    elif algo == "simple":
        mesh = mesh.filter_smooth_simple(number_of_iterations=iterations)
    else:
        raise ValueError("Unknown algorithm: " + algo)
    mesh.compute_vertex_normals()
    return mesh


def _visualize_compare(paths: List[str], overlay: bool = False, gap_scale: float = 1.2) -> None:
    """
    여러 OBJ를 한 창에서 비교 시각화합니다.
    - overlay=False: X축으로 나란히 배치
    - overlay=True : 동일 좌표계에 겹쳐서 비교(색상으로 구분)
    """
    if not paths:
        raise ValueError("No paths provided for comparison")

    colors = [
        (1.0, 0.2, 0.2),  # red
        (0.2, 0.6, 1.0),  # blue
        (0.2, 0.8, 0.3),  # green
        (1.0, 0.6, 0.0),  # orange
        (0.8, 0.2, 0.8),  # purple
        (0.9, 0.9, 0.1),  # yellow
    ]

    geoms = []
    cur_offset = 0.0
    max_height = 0.0
    for idx, p in enumerate(paths):
        if not os.path.isfile(p):
            raise FileNotFoundError(p)
        m = _load_mesh(p)
        m.compute_vertex_normals()
        m.paint_uniform_color(colors[idx % len(colors)])

        if not overlay:
            bbox = m.get_axis_aligned_bounding_box()
            extent = bbox.get_extent()
            width = float(extent[0]) if extent[0] > 0 else 1.0
            height = float(extent[1])
            max_height = max(max_height, height)
            # 중심을 원점으로 맞춘 뒤 X축으로 옮김
            center = bbox.get_center()
            m.translate(-center)
            m.translate((cur_offset + width * 0.5, 0.0, 0.0))
            cur_offset += width * gap_scale
        geoms.append(m)

    win_name = f"Compare {len(paths)} Meshes ({'overlay' if overlay else 'side-by-side'})"
    o3d.visualization.draw_geometries(geoms, window_name=win_name)


def _pick_latest_dmp_output(mesh_basename: str, created_after: float) -> Optional[str]:
    root = os.path.join(".", "datasets", "d_output")
    if not os.path.isdir(root):
        return None
    buckets = []
    for d in os.listdir(root):
        if not d.lower().startswith(mesh_basename.lower()):
            continue
        full = os.path.join(root, d)
        try:
            mtime = os.path.getmtime(full)
        except OSError:
            continue
        if mtime >= created_after - 5:
            buckets.append((mtime, full))
    if not buckets:
        return None
    _, latest_dir = max(buckets, key=lambda x: x[0])
    objs = glob.glob(os.path.join(latest_dir, "*_output.obj"))
    if not objs:
        return None

    def _epoch_key(p: str) -> int:
        base = os.path.basename(p)
        try:
            return int(base.split("_")[0])
        except Exception:
            return -1

    return max(objs, key=_epoch_key)


def _ai_denoise_with_deepmeshprior(input_path: str, iters: int, lr: float, lap: float) -> Optional[str]:
    script = os.path.join("ai_pipeline", "DeepMeshPrior", "denoise.py")
    if not os.path.isfile(script):
        print("[warn] DeepMeshPrior script not found. Skipping AI denoise.")
        return None

    mesh_basename = os.path.splitext(os.path.basename(input_path))[0]
    t0 = time.time()
    cmd = [
        sys.executable,
        script,
        "-i", os.path.abspath(input_path),
        "--iter", str(int(iters)),
        "--lr", str(float(lr)),
        "--lap", str(float(lap)),
        "--no-log",
        "--save-every", "100",
    ]
    print("[AI] Running:", " ".join(cmd))
    try:
        subprocess.run(cmd, check=True)
    except subprocess.CalledProcessError as e:
        print("[warn] AI denoise failed:", e)
        return None

    latest_obj = _pick_latest_dmp_output(mesh_basename, created_after=t0)
    if not latest_obj or not os.path.isfile(latest_obj):
        print("[warn] AI output not found.")
        return None

    out_path = os.path.join(os.path.dirname(input_path), f"{mesh_basename}_denoised_ai.obj")
    try:
        shutil.copyfile(latest_obj, out_path)
    except Exception as e:
        print("[warn] Failed to copy AI result:", e)
        return None
    return out_path


def main():
    parser = argparse.ArgumentParser(description="Standalone mesh denoiser (algorithmic or AI)")
    parser.add_argument("--input", "-i", help="Input OBJ file path")
    parser.add_argument("--mode", choices=["algo", "ai"], default="algo", help="Denoise mode")
    parser.add_argument("--algo", choices=["taubin", "laplacian", "simple"], default="taubin", help="Algorithmic smoothing method")
    parser.add_argument("--iter", type=int, default=15, help="Smoothing iterations (algo mode)")
    parser.add_argument("--ai-iters", type=int, default=400, help="AI denoise iterations")
    parser.add_argument("--ai-lr", type=float, default=0.01, help="AI learning rate")
    parser.add_argument("--ai-lap", type=float, default=1.2, help="AI Laplacian weight")
    parser.add_argument("--visualize", action="store_true", help="Visualize result in Open3D viewer")
    parser.add_argument("--preclean", action="store_true", help="Apply basic mesh pre-clean before denoise")
    # Compare mode
    parser.add_argument("--compare", nargs="+", help="Compare multiple OBJ files in one viewer")
    parser.add_argument("--overlay", action="store_true", help="Overlay meshes instead of side-by-side")
    parser.add_argument("--gap", type=float, default=1.2, help="Side-by-side gap scale (>=1.0)")
    args = parser.parse_args()

    # Compare early-exit path
    if args.compare:
        _visualize_compare(args.compare, overlay=args.overlay, gap_scale=max(1.0, float(args.gap)))
        return

    in_path = args.input
    if not in_path or not os.path.isfile(in_path):
        raise FileNotFoundError(str(in_path))

    if args.mode == "ai":
        out = _ai_denoise_with_deepmeshprior(in_path, args.ai_iters, args.ai_lr, args.ai_lap)
        if out is None:
            print("[info] Falling back to algorithmic smoothing due to AI failure.")
            args.mode = "algo"
        else:
            if args.visualize:
                mesh = _load_mesh(out)
                mesh.compute_vertex_normals()
                o3d.visualization.draw_geometries([mesh], window_name="AI Denoised Mesh")
            print("[ok] Saved:", out)
            return

    mesh = _load_mesh(in_path)
    if args.preclean:
        mesh = _preclean_mesh(mesh)
    mesh = _smooth_mesh(mesh, args.algo, args.iter)

    base = os.path.splitext(in_path)[0]
    out_path = f"{base}_denoised_{args.algo}.obj"
    o3d.io.write_triangle_mesh(out_path, mesh, write_triangle_uvs=False)
    if args.visualize:
        o3d.visualization.draw_geometries([mesh], window_name="Algo Denoised Mesh")
    print("[ok] Saved:", out_path)


if __name__ == "__main__":
    main()
