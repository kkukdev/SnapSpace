"""
python mesh_denoiser.py -i ../../datasets/mainhall_safe_precleaned_cleaned.obj   --mode auto_flat --proj-dist 0.008   --floor-ratio 0.5 --wall-ratio 2.0   --smooth-floor 6 --smooth-wall 24   --wall-ortho-dot 0.15   --max-walls 6 --ransac-iters 4000   --preclean --visualize
"""


import argparse
import os
import sys
import time
import subprocess
import shutil
import glob
import numpy as np
from typing import Optional, List
import open3d as o3d

# ----------------------------------------
# 기본 메쉬 로드 함수
# ----------------------------------------
def _load_mesh(path: str) -> o3d.geometry.TriangleMesh:
    """OBJ 파일을 읽어서 Open3D의 TriangleMesh 객체로 반환"""
    mesh = o3d.io.read_triangle_mesh(path)
    if mesh.is_empty():
        raise ValueError("Empty mesh: " + path)
    return mesh


# ----------------------------------------
# 메쉬 전처리 (불량 요소 제거)
# ----------------------------------------
def _preclean_mesh(mesh: o3d.geometry.TriangleMesh) -> o3d.geometry.TriangleMesh:
    """
    불필요하거나 비정상적인 요소 제거:
    - 참조되지 않는 정점
    - 중복된 정점 및 삼각형
    - 퇴화 삼각형(면적=0)
    - 비매니폴드 엣지 (엣지 연결이 비정상인 경우)
    """
    mesh.remove_unreferenced_vertices()
    mesh.remove_degenerate_triangles()
    mesh.remove_duplicated_vertices()
    mesh.remove_duplicated_triangles()
    mesh.remove_non_manifold_edges()
    mesh.compute_vertex_normals()  # 정점 노멀 재계산
    return mesh


# ----------------------------------------
# 메쉬 평탄화 (스무딩) 알고리즘 선택
# ----------------------------------------
def _smooth_mesh(mesh: o3d.geometry.TriangleMesh, algo: str, iterations: int) -> o3d.geometry.TriangleMesh:
    """
    지정한 알고리즘으로 반복적 스무딩 수행.
    - taubin : 형태 유지하면서 부드럽게
    - laplacian : 단순 인접 평균화
    - simple : 기본 스무딩
    """
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


# ----------------------------------------
# 여러 메쉬 비교 시각화
# ----------------------------------------
def _visualize_compare(paths: List[str], overlay: bool = False, gap_scale: float = 1.2) -> None:
    """
    여러 OBJ를 한 화면에서 비교 시각화
    overlay=False → 옆으로 나란히 배치
    overlay=True  → 동일 좌표계에 겹쳐 표시(색상으로 구분)
    """
    if not paths:
        raise ValueError("No paths provided for comparison")

    colors = [
        (1.0, 0.2, 0.2),  # 빨강
        (0.2, 0.6, 1.0),  # 파랑
        (0.2, 0.8, 0.3),  # 초록
        (1.0, 0.6, 0.0),  # 주황
        (0.8, 0.2, 0.8),  # 보라
        (0.9, 0.9, 0.1),  # 노랑
    ]

    geoms = []
    cur_offset = 0.0
    for idx, p in enumerate(paths):
        if not os.path.isfile(p):
            raise FileNotFoundError(p)
        m = _load_mesh(p)
        m.compute_vertex_normals()
        m.paint_uniform_color(colors[idx % len(colors)])

        # overlay=False → 좌우로 배치
        if not overlay:
            bbox = m.get_axis_aligned_bounding_box()
            extent = bbox.get_extent()
            width = float(extent[0]) if extent[0] > 0 else 1.0
            center = bbox.get_center()
            m.translate(-center)
            m.translate((cur_offset + width * 0.5, 0.0, 0.0))
            cur_offset += width * gap_scale
        geoms.append(m)

    win_name = f"Compare {len(paths)} Meshes ({'overlay' if overlay else 'side-by-side'})"
    o3d.visualization.draw_geometries(geoms, window_name=win_name)


# ----------------------------------------
# DeepMeshPrior의 최신 결과 파일 탐색
# ----------------------------------------
def _pick_latest_dmp_output(mesh_basename: str, created_after: float, root: str) -> Optional[str]:
    """
    DeepMeshPrior 실행 결과(root/... ) 중 가장 최근의 *_output.obj 파일을 탐색
    """
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

    # 파일명 앞부분(epoch 값)을 기준으로 최신 결과 선택
    def _epoch_key(p: str) -> int:
        base = os.path.basename(p)
        try:
            return int(base.split("_")[0])
        except Exception:
            return -1

    return max(objs, key=_epoch_key)


# ----------------------------------------
# AI 기반 노이즈 제거 (DeepMeshPrior)
# ----------------------------------------
def _ai_denoise_with_deepmeshprior(
    input_path: str,
    iters: int,
    lr: float,
    lap: float,
    dmp_script: str,
    dmp_output_root: str
) -> Optional[str]:
    """
    DeepMeshPrior(unsupervised mesh denoising 모델)을 subprocess로 실행하여
    AI 기반 노이즈 제거 수행.
    """
    script = dmp_script
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

    # 결과 파일 탐색
    latest_obj = _pick_latest_dmp_output(mesh_basename, created_after=t0, root=dmp_output_root)
    if not latest_obj or not os.path.isfile(latest_obj):
        print("[warn] AI output not found.")
        return None

    # 결과 복사 및 이름 변경
    out_path = os.path.join(os.path.dirname(input_path), f"{mesh_basename}_denoised_ai.obj")
    try:
        shutil.copyfile(latest_obj, out_path)
    except Exception as e:
        print("[warn] Failed to copy AI result:", e)
        return None
    return out_path


# ----------------------------------------
# 메인 함수 (명령행 인터페이스)
# ----------------------------------------
def main():
    parser = argparse.ArgumentParser(description="Standalone mesh denoiser (algorithmic or AI)")
    parser.add_argument("--input", "-i", help="Input OBJ file path")
    parser.add_argument("--mode", choices=["algo", "ai", "auto_flat"], default="algo", help="Denoise mode")
    parser.add_argument("--algo", choices=["taubin", "laplacian", "simple"], default="taubin")
    parser.add_argument("--iter", type=int, default=15)
    parser.add_argument("--ai-iters", type=int, default=400)
    parser.add_argument("--ai-lr", type=float, default=0.01)
    parser.add_argument("--ai-lap", type=float, default=1.2)
    parser.add_argument("--visualize", action="store_true")
    parser.add_argument("--preclean", action="store_true")

    # auto_flat 모드(바닥/벽 자동 평탄화) 관련 파라미터
    parser.add_argument("--max-walls", type=int, default=4)
    parser.add_argument("--proj-dist", type=float, default=0.02)
    parser.add_argument("--floor-ratio", type=float, default=0.6)
    parser.add_argument("--wall-ratio", type=float, default=1.6)
    parser.add_argument("--smooth-floor", type=int, default=8)
    parser.add_argument("--smooth-wall", type=int, default=22)
    parser.add_argument("--wall-ortho-dot", type=float, default=0.2)
    parser.add_argument("--ransac-iters", type=int, default=3000)
    parser.add_argument("--sample-n", type=int, default=250000)
    parser.add_argument("--smooth-iters", type=int, default=12)
    parser.add_argument("--compare", nargs="+")
    parser.add_argument("--overlay", action="store_true")
    parser.add_argument("--gap", type=float, default=1.2)

    # 하드코딩된 경로 옵션화
    parser.add_argument(
        "--dmp-script",
        default=os.environ.get("DMP_SCRIPT", os.path.join("ai_pipeline", "DeepMeshPrior", "denoise.py")),
        help="DeepMeshPrior denoise.py 경로"
    )
    parser.add_argument(
        "--dmp-output-root",
        default=os.environ.get("DMP_OUTPUT_ROOT", os.path.join(".", "datasets", "d_output")),
        help="DeepMeshPrior 결과 디렉터리 루트"
    )

    args = parser.parse_args()

    # 여러 파일 비교 모드
    if args.compare:
        _visualize_compare(args.compare, overlay=args.overlay, gap_scale=max(1.0, float(args.gap)))
        return

    in_path = args.input
    if not in_path or not os.path.isfile(in_path):
        raise FileNotFoundError(str(in_path))

    # AI 모드 실행
    if args.mode == "ai":
        out = _ai_denoise_with_deepmeshprior(
            in_path,
            args.ai_iters,
            args.ai_lr,
            args.ai_lap,
            dmp_script=args.dmp_script,
            dmp_output_root=args.dmp_output_root
        )
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

    # 자동 평면화 모드
    if args.mode == "auto_flat":
        out = _auto_flatten_floor_walls(
            in_path,
            max_walls=args.max_walls,
            proj_dist_ratio=args.proj_dist,
            ransac_iters=args.ransac_iters,
            sample_n=args.sample_n,
            smooth_iters=args.smooth_iters,
            floor_ratio=args.floor_ratio,       # ← 옵션 전달
            wall_ratio=args.wall_ratio,         # ← 옵션 전달
            smooth_floor=args.smooth_floor,     # ← 옵션 전달
            smooth_wall=args.smooth_wall,       # ← 옵션 전달
            wall_ortho_dot=args.wall_ortho_dot, # ← 옵션 전달
            do_preclean=args.preclean,
        )
        if args.visualize:
            m = _load_mesh(out)
            m.compute_vertex_normals()
            o3d.visualization.draw_geometries([m], window_name="Auto Floor/Walls")
        print("[ok] Saved:", out)
        return

    # 기본 알고리즘 스무딩
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


# ----------------------------------------
# 자동 바닥/벽 평탄화 알고리즘
# ----------------------------------------
def _auto_flatten_floor_walls(
    input_path: str,
    max_walls: int = 4,
    proj_dist_ratio: float = 0.02,
    ransac_iters: int = 3000,
    sample_n: int = 250000,
    smooth_iters: int = 12,
    floor_ratio: float = 0.6,
    wall_ratio: float = 1.6,
    smooth_floor: int = 8,
    smooth_wall: int = 22,
    wall_ortho_dot: float = 0.2,
    do_preclean: bool = True
) -> str:
    """
    실내 메쉬에서 바닥 평면과 벽 평면을 자동으로 찾아
    각 영역을 평탄화 + 부드럽게 스무딩 처리
    """
    mesh = _load_mesh(input_path)
    if do_preclean:
        mesh = _preclean_mesh(mesh)
    mesh.compute_vertex_normals()
    V = np.asarray(mesh.vertices)

    # 장면 크기에 따라 투영 허용 거리 계산
    aabb = mesh.get_axis_aligned_bounding_box()
    diag = np.linalg.norm(aabb.get_max_bound() - aabb.get_min_bound())
    diag = float(diag) if diag > 0 else 1.0
    base_dist = max(1e-4, float(proj_dist_ratio) * diag)
    floor_dist = base_dist * float(floor_ratio)
    wall_dist  = base_dist * float(wall_ratio)

    # 균등 샘플링 포인트로 RANSAC 평면 탐색
    pcd = mesh.sample_points_uniformly(
        number_of_points=min(int(sample_n), max(50000, len(V)))
    )
    pts = np.asarray(pcd.points)

    # RANSAC 평면 추출 함수
    def seg_plane(pcd_in, dist, iters):
        plane, inl = pcd_in.segment_plane(distance_threshold=float(dist),
                                          ransac_n=3,
                                          num_iterations=int(iters))
        n = np.array(plane[:3], dtype=float)
        n /= (np.linalg.norm(n) + 1e-12)
        d = float(plane[3])
        return n, d, np.asarray(inl, dtype=int)

    # 1) 바닥 평면 검출
    n_floor, d_floor, inl_floor = seg_plane(pcd, floor_dist, ransac_iters)

    # 2) 벽 평면 검출 (바닥과 직교 방향만 유지)
    wall_list = []
    mask = np.ones(len(pts), dtype=bool)
    mask[inl_floor] = False
    pcd_left = pcd.select_by_index(np.where(mask)[0])
    for _ in range(int(max_walls)):
        if len(pcd_left.points) < 500:
            break
        try:
            n, d, inl = seg_plane(pcd_left, wall_dist, max(500, ransac_iters // 2))
        except RuntimeError:
            break
        # 바닥과 평행한 평면 제외, 직교인 경우만 채택
        if abs(float(np.dot(n, n_floor))) < float(wall_ortho_dot):
            wall_list.append((n, d))
        # 다음 평면 탐색을 위한 점 제거
        all_idx = np.arange(len(pcd_left.points))
        keep = np.setdiff1d(all_idx, inl, assume_unique=False)
        if len(keep) < 500:
            break
        pcd_left = pcd_left.select_by_index(keep.tolist())

    # 3) 선택 영역을 평면으로 투영
    sel = np.zeros(len(V), dtype=bool)
    def project(n, d, thr):
        dist = V @ n + d
        idx = np.where(np.abs(dist) < thr)[0]
        sel[idx] = True
        V[idx] = V[idx] - dist[idx, None] * n
    # 바닥
    project(n_floor, d_floor, floor_dist)
    # 벽
    sel_wall = np.zeros(len(V), dtype=bool)
    for n, d in wall_list:
        dist = V @ n + d
        idx = np.where(np.abs(dist) < wall_dist)[0]
        sel[idx] = True
        sel_wall[idx] = True
        V[idx] = V[idx] - dist[idx, None] * n

    # 4) 영역별 스무딩 적용 후 병합
    mesh.vertices = o3d.utility.Vector3dVector(V)
    mesh.compute_vertex_normals()
    m_floor = mesh.filter_smooth_taubin(number_of_iterations=int(smooth_floor), lambda_filter=0.53, mu=-0.55)
    Vf = np.asarray(m_floor.vertices)
    m_wall  = mesh.filter_smooth_taubin(number_of_iterations=int(smooth_wall),  lambda_filter=0.53, mu=-0.55)
    Vw = np.asarray(m_wall.vertices)

    # 바닥 / 벽 / 나머지 구역 병합
    V_out = V.copy()
    sel_floor_only = sel & (~sel_wall)
    V_out[sel_floor_only] = Vf[sel_floor_only]
    V_out[sel_wall]       = Vw[sel_wall]
    mesh.vertices = o3d.utility.Vector3dVector(V_out)
    mesh.compute_vertex_normals()

    out = os.path.splitext(input_path)[0] + "_auto_flat.obj"
    o3d.io.write_triangle_mesh(out, mesh, write_triangle_uvs=False)
    return out


# ----------------------------------------
# 엔트리포인트
# ----------------------------------------
if __name__ == "__main__":
    main()
