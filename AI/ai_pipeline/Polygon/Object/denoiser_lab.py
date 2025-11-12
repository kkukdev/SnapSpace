"""
============================================================
📦 Mesh Denoiser (AI/ML Powered - Revised for Object Extraction)
------------------------------------------------------------
3D 스캔으로 생성된 OBJ 파일에서 바닥을 제거하고
중심이 되는 주요 객체(인물)만 선별하여 추출하는 도구입니다.

🧠 주요 기능:
1️⃣ 손상된 OBJ 파일 자동 복구 (잘린 줄 제거) - 안전성 강화
2️⃣ RANSAC: 3D 공간에서 가장 큰 평면(바닥)을 탐지하여 제거
3️⃣ DBSCAN: 바닥 제거 후, 남아있는 포인트 클라우드에서
            가장 밀집된 클러스터(주요 객체)만 선별
4️⃣ 메시 재구성: 선별된 포인트 클라우드로부터 새로운 TriangleMesh 생성 (Ball Pivoting 우선)
5️⃣ PyMeshLab: 아주 작은 부유물 노이즈 및 연결되지 않은 조각 추가 제거

------------------------------------------------------------
💻 실행 예시:
python mesh_denoiser.py --input ../../datasets/obj/your_scan.obj --output-dir ../../datasets/denoised --visualize

⚙️ 옵션 설명:
--input                 : 입력 OBJ 파일 경로 (필수)
--output-dir            : 결과 파일 저장 디렉토리 (기본값: 현재 디렉토리)
--visualize             : 결과를 Open3D 뷰어로 시각화 (선택)
--ransac-threshold      : RANSAC 바닥 평면 탐지 임계값 (기본 0.01)
--dbscan-eps            : DBSCAN 클러스터링 거리 임계값 (기본 0.05)
--dbscan-min-points     : DBSCAN 클러스터링 최소 포인트 수 (기본 100)
--poisson-depth         : 메시 재구성 시 Poisson 깊이 (기본 9)
--ball-pivoting-radii   : 메시 재구성 시 Ball Pivoting 반경 (쉼표로 구분, 기본 0.005,0.01,0.02,0.04)
--sampling-points       : RANSAC/DBSCAN을 위한 포인트 클라우드 샘플링 개수 (기본 200000)
------------------------------------------------------------
✅ 출력 예시:
🧹 손상된 라인 제거 완료 → your_scan_safe.obj
🧭 [Open3D] 포인트 클라우드 샘플링 중...
... (중략) ...
✅ Denoiser 완료 → your_scan_denoised.obj
============================================================
"""

import open3d as o3d
import pymeshlab as ml
import argparse
import os
import time
import numpy as np
import shutil # 파일 이동/복사를 위해 추가
import unicodedata
import hashlib


def sanitize_filename(name: str) -> str:
    normalized = unicodedata.normalize("NFKD", name or "")
    ascii_name = normalized.encode("ascii", "ignore").decode("ascii")
    safe = "".join(c if c.isalnum() or c in ("-", "_") else "_" for c in ascii_name)
    safe = safe.strip("_")
    if safe:
        return safe
    digest = hashlib.sha1((name or "").encode("utf-8")).hexdigest()[:16]
    return f"file_{digest}"


def build_temp_path(base_name: str, suffix: str, directory: str) -> str:
    safe_base = sanitize_filename(base_name)
    filename = f"{safe_base}{suffix}"
    return os.path.join(directory, filename)

# ----------------------------------------
# 헬퍼 함수: 손상된 OBJ 자동 복구
# ----------------------------------------
def _remove_invalid_lines(input_path):
    """
    OBJ 파일의 끝부분에 존재할 수 있는
    '숫자만 있는 잘린 줄'을 자동으로 제거하여 안전하게 복원합니다.
    """
    with open(input_path, "r", encoding="utf-8", errors="ignore") as f:
        lines = f.readlines()

    valid = []
    for line in lines:
        s = line.strip()
        if not s:
            continue
        # 유효한 OBJ 데이터 라인인지 확인
        if s.startswith(("v ", "vt ", "vn ", "f ", "usemtl", "o ", "s ", "#")):
            valid.append(line)
        # 숫자만 있거나 짧은 알 수 없는 라인 제거 (정확도 향상)
        elif not any(c.isalpha() for c in s) or len(s) < 3: 
            continue
        else:
            valid.append(line)

    base = os.path.splitext(os.path.basename(input_path))[0]
    out_dir = os.path.dirname(input_path) # 중간 파일은 입력 파일 경로에 저장
    os.makedirs(out_dir, exist_ok=True)
    tmp = build_temp_path(base, "_safe.obj", out_dir)
    with open(tmp, "w", encoding="utf-8") as f:
        f.writelines(valid)

    print(f"🧹 손상된 라인 제거 완료 → {tmp}")
    return tmp


# ----------------------------------------
# 핵심 함수: Denoiser 및 메인 객체 추출 (RANSAC + DBSCAN + Reconstruct)
# ----------------------------------------
def _denoise_and_extract_main_object(
    input_obj_path: str,
    output_dir: str,
    ransac_threshold: float = 0.01,
    dbscan_eps: float = 0.05,
    dbscan_min_points: int = 100,
    poisson_depth: int = 9,
    ball_pivoting_radii_str: str = "0.005,0.01,0.02,0.04",
    sampling_points: int = 200000
) -> str:
    """
    OBJ 파일에서 RANSAC으로 바닥을 제거하고, DBSCAN으로 중심 객체를 선별한 뒤,
    선별된 포인트 클라우드로부터 새로운 메시를 재구성하고 PyMeshLab으로 후처리합니다.
    """
    
    # 1. 안전한 OBJ 파일 로드
    safe_obj_path = _remove_invalid_lines(input_obj_path)
    mesh = o3d.io.read_triangle_mesh(safe_obj_path)
    if mesh.is_empty():
        raise ValueError("❌ 메쉬가 비어 있습니다.")

    print(f"🧭 [Open3D] 포인트 클라우드 샘플링 중 (Points: {sampling_points})...")
    pcd = mesh.sample_points_poisson_disk(number_of_points=sampling_points)
    
    # ==================
    # 1. RANSAC 평면 탐지 (바닥 찾기 및 제거)
    # ==================
    print(f"🧭 [Open3D] RANSAC 평면 분할로 바닥 탐색 중 (threshold={ransac_threshold})...")
    
    plane_model, inliers = pcd.segment_plane(
        distance_threshold=ransac_threshold,
        ransac_n=3,
        num_iterations=1000
    )
    
    pcd_no_ground = pcd.select_by_index(inliers, invert=True)
    print(f"   → RANSAC: 바닥(평면) 포인트 {len(inliers)}개 제거. 잔여 포인트: {len(pcd_no_ground.points)}개")

    # ==================
    # 2. DBSCAN 클러스터링 (중심 인물 선별)
    # ==================
    print(f"🧭 [Open3D] 바닥이 제거된 포인트에서 DBSCAN으로 중심 인물 선별 중 (eps={dbscan_eps}, min_points={dbscan_min_points})...")

    with o3d.utility.VerbosityContextManager(o3d.utility.VerbosityLevel.Debug) as cm:
        labels = np.array(pcd_no_ground.cluster_dbscan(
            eps=dbscan_eps, 
            min_points=dbscan_min_points, 
            print_progress=True
        ))

    unique_labels, counts = np.unique(labels[labels >= 0], return_counts=True)
    if len(counts) == 0:
        print("⚠️ DBSCAN 실패: 메인 클러스터를 찾지 못했습니다.")
        print("⚠️ 튜닝 값 (dbscan_eps, dbscan_min_points)을 조절해 보세요.")
        final_pcd = pcd_no_ground # 실패 시 바닥 제거된 모든 포인트를 사용
        print("   → DBSCAN 실패로, 바닥 제거된 모든 포인트를 사용합니다.")
    else:
        largest_cluster_label = unique_labels[np.argmax(counts)]
        print(f"   → 총 {len(unique_labels)}개 클러스터 발견. 가장 큰 클러스터(Label {largest_cluster_label}) 선택.")
        final_pcd = pcd_no_ground.select_by_index(np.where(labels == largest_cluster_label)[0])
        print(f"   → 중심 인물 포인트 클라우드 추출 완료 (Points: {len(final_pcd.points)})")

    if len(final_pcd.points) < 100:
        raise ValueError("❌ 최종 포인트 클라우드가 너무 적습니다. 객체 추출 실패. (튜닝 값 조절 필요)")

    # ==================
    # 3. 메시 재구성 (Ball Pivoting 우선, 실패 시 Poisson)
    # ==================
    print("   → 포인트 클라우드 노멀 계산 중...")
    final_pcd.estimate_normals(search_param=o3d.geometry.KDTreeSearchParamHybrid(radius=0.1, max_nn=30))
    
    print("   → 메시 재구성 중 (Ball Pivoting)...")
    radii_list = [float(r) for r in ball_pivoting_radii_str.split(',')]
    reconstructed_mesh = o3d.geometry.TriangleMesh.create_from_point_cloud_ball_pivoting(
        final_pcd, o3d.utility.DoubleVector(radii_list)
    )

    if not reconstructed_mesh.has_triangles() or len(reconstructed_mesh.triangles) == 0:
        print(f"⚠️ Ball Pivoting 실패 (Triangles: {len(reconstructed_mesh.triangles)}). Poisson으로 대체합니다 (depth={poisson_depth})...")
        reconstructed_mesh, _ = o3d.geometry.TriangleMesh.create_from_point_cloud_poisson(final_pcd, depth=poisson_depth)
    
    # 메시 초기 정리 (Open3D 내장 기능)
    reconstructed_mesh.remove_unreferenced_vertices()
    reconstructed_mesh.remove_degenerate_triangles()
    reconstructed_mesh.remove_duplicated_vertices()
    reconstructed_mesh.remove_duplicated_triangles()
    reconstructed_mesh.remove_non_manifold_edges()
    reconstructed_mesh.compute_vertex_normals()

    # ==================
    # 4. PyMeshLab으로 추가 노이즈 제거 및 정리
    # ==================
    if reconstructed_mesh.has_triangles() and len(reconstructed_mesh.triangles) > 0:
        base_name = os.path.splitext(os.path.basename(input_obj_path))[0]
        safe_base = sanitize_filename(base_name)
        # PyMeshLab 로드를 위해 Open3D 결과 임시 저장
        temp_output_path_for_ml = build_temp_path(safe_base, "_temp_reconstructed.obj", output_dir)
        os.makedirs(output_dir, exist_ok=True)
        o3d.io.write_triangle_mesh(temp_output_path_for_ml, reconstructed_mesh, write_triangle_uvs=False)

        ms = ml.MeshSet()
        ms.load_new_mesh(temp_output_path_for_ml)
        print("🔹 PyMeshLab으로 작은 부유물 노이즈 추가 제거 중...")
        # 연결된 조각 중 아주 작은 것(얼굴 100개 미만) 제거
        ms.apply_filter("meshing_remove_connected_component_by_face_number", mincomponentsize=100)
        ms.apply_filter("meshing_remove_unreferenced_vertices")
        ms.apply_filter("meshing_remove_duplicate_faces")
        ms.apply_filter("meshing_remove_null_faces")
        
        # 재구성된 메시에서 발생할 수 있는 작은 구멍 메우기
        ms.apply_filter("meshing_close_holes", maxholesize=50)
        # 노멀 재계산 (Open3D에서 했지만, PyMeshLab에서도 한 번 더 하는 것이 안전)
        ms.apply_filter("compute_normal_for_point_clouds", k=10)


        final_mesh_output_path = os.path.join(output_dir, f"{safe_base}_denoised.obj")
        ms.save_current_mesh(final_mesh_output_path)
        os.remove(temp_output_path_for_ml) # 임시 파일 삭제
        print(f"✅ Denoiser 완료 (PyMeshLab 후처리 포함) → {final_mesh_output_path}")
        return final_mesh_output_path
    else:
        raise ValueError("❌ 재구성된 메시가 유효하지 않습니다.")


# ----------------------------------------
# 시각화 (Open3D)
# ----------------------------------------
def _visualize_mesh(mesh_path):
    """
    Open3D로 결과 메쉬를 시각화합니다.
    """
    mesh = o3d.io.read_triangle_mesh(mesh_path)
    if mesh.is_empty():
        raise ValueError("❌ 시각화 실패: 메쉬가 비어 있습니다.")
    mesh.compute_vertex_normals()
    o3d.visualization.draw_geometries([mesh], window_name="Denoised Mesh Viewer")


# ----------------------------------------
# 메인 함수 (명령행 인터페이스)
# ----------------------------------------
if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="3D Mesh Denoiser (AI/ML Powered - Object Extraction)")
    parser.add_argument("--input", "-i", type=str, required=True, help="입력 .obj 파일 경로")
    parser.add_argument("--output-dir", type=str, default=".", help="결과 파일 저장 디렉토리 (기본값: 현재 디렉토리)")
    parser.add_argument("--visualize", action="store_true", help="Open3D 뷰어로 결과 시각화")
    parser.add_argument("--ransac-threshold", type=float, default=0.012, help="RANSAC 바닥 평면 탐지 임계값 (기본 0.012)")
    parser.add_argument("--dbscan-eps", type=float, default=0.04, help="DBSCAN 클러스터링 거리 임계값 (기본 0.04)")
    parser.add_argument("--dbscan-min-points", type=int, default=100, help="DBSCAN 클러스터링 최소 포인트 수 (기본 100)")
    parser.add_argument("--poisson-depth", type=int, default=9, help="메시 재구성 시 Poisson 깊이 (기본 9)")
    parser.add_argument("--ball-pivoting-radii", type=str, default="0.005,0.01,0.02,0.04", help="메시 재구성 시 Ball Pivoting 반경 (쉼표로 구분, 기본 0.005,0.01,0.02,0.04)")
    parser.add_argument("--sampling-points", type=int, default=200000, help="RANSAC/DBSCAN을 위한 포인트 클라우드 샘플링 개수 (기본 200000)")
    
    args = parser.parse_args()

    start = time.time()
    try:
        denoised_output_path = _denoise_and_extract_main_object(
            args.input, 
            args.output_dir,
            args.ransac_threshold,
            args.dbscan_eps,
            args.dbscan_min_points,
            args.poisson_depth,
            args.ball_pivoting_radii,
            args.sampling_points
        )
        print(f"\n✅ 전체 Denoiser 파이프라인 완료 (총 {time.time() - start:.2f}초)")

        if args.visualize:
            _visualize_mesh(denoised_output_path)
            
    except ValueError as e:
        print(f"❌ 오류 발생: {e}")
    except Exception as e:
        print(f"❌ 예상치 못한 오류 발생: {e}")