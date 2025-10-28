"""
============================================================
📦 Mesh Optimizer & Cleaner (Safe Mode)
------------------------------------------------------------
3D 스캔으로 생성된 OBJ 파일을 자동으로 정리(clean-up)하는 도구입니다.
Open3D + PyMeshLab을 결합하여 다음 작업을 수행합니다.

🧩 주요 기능:
1️⃣ 손상된 OBJ 파일 자동 복구 (잘린 줄 제거)
2️⃣ Open3D 기반 사전 정리 (중복 정점, 비정상 엣지 제거)
3️⃣ PyMeshLab 기반 Poisson 표면 복원 + 평탄화(Smoothing)
4️⃣ 홀 메움 + 노멀 재계산
5️⃣ 결과 시각화 (Open3D Viewer)

------------------------------------------------------------
💻 실행 예시:
python mesh_optimizer.py --input ../../datasets/obj/mainhall.obj --visualize

⚙️ 옵션 설명:
--input       : 입력 OBJ 파일 경로 (필수)
--visualize   : 결과를 Open3D 뷰어로 시각화 (선택)

------------------------------------------------------------
✅ 출력 예시:
🧹 손상된 라인 제거 완료 → mainhall_safe.obj
🧭 [Open3D] 비정상 요소 제거 중...
✅ Open3D 사전 정리 완료 → mainhall_precleaned.obj
🔹 Poisson Surface Reconstruction 실행 중...
   → 새 mesh로 전환 완료 (Face: 240,532)
🔹 평탄화 (Smoothing)
   → Taubin smoothing 완료
🔹 홀 메우기 + 노멀 재계산
✅ 클린업 완료 → mainhall_precleaned_cleaned.obj
============================================================
"""

import open3d as o3d
import pymeshlab as ml
import argparse
import os
import time
import numpy as np


# ===========================================================
# 1️⃣ 손상된 OBJ 자동 복구 (잘린 줄 제거)
# ===========================================================
def remove_invalid_lines(input_path):
    """
    OBJ 파일의 끝부분에 존재할 수 있는
    '숫자만 있는 잘린 줄'을 자동으로 제거하여 안전하게 복원합니다.

    🧠 배경:
    PyMeshLab은 OBJ 파일 끝에 쓰다만 숫자 같은 라인이 있으면
    파싱 중 세그멘테이션 오류(Segmentation Fault)가 발생합니다.
    """
    with open(input_path, "r", encoding="utf-8", errors="ignore") as f:
        lines = f.readlines()

    valid = []
    for line in lines:
        s = line.strip()
        if not s:
            continue  # 빈 줄 제거
        if s.startswith(("v ", "vt ", "vn ", "f ", "usemtl", "o ", "s ")):
            valid.append(line)  # 정상 OBJ 데이터
        elif not any(c.isalpha() for c in s):
            continue  # 숫자만 있는 잘린 줄 제거
        else:
            valid.append(line)

    tmp = input_path.replace(".obj", "_safe.obj")
    with open(tmp, "w", encoding="utf-8") as f:
        f.writelines(valid)

    print(f"🧹 손상된 라인 제거 완료 → {tmp}")
    return tmp


# ===========================================================
# 2️⃣ Open3D 기반 사전 클린 (비정상 vertex/face 제거)
# ===========================================================
def preclean_with_open3d(input_path):
    """
    Open3D를 사용하여 메쉬 구조를 안전하게 정리합니다.
    - 중복 정점 / 면 제거
    - degenerate(면적 0) 삼각형 제거
    - 비정상(non-manifold) 엣지 제거
    """
    mesh = o3d.io.read_triangle_mesh(input_path)
    if mesh.is_empty():
        raise ValueError("❌ 메쉬가 비어 있습니다.")

    print("🧭 [Open3D] 비정상 요소 제거 중...")

    mesh.remove_unreferenced_vertices()
    mesh.remove_degenerate_triangles()
    mesh.remove_duplicated_vertices()
    mesh.remove_duplicated_triangles()
    mesh.remove_non_manifold_edges()
    mesh.compute_vertex_normals()

    temp_path = input_path.replace(".obj", "_precleaned.obj")
    o3d.io.write_triangle_mesh(temp_path, mesh, write_triangle_uvs=False)
    print(f"✅ Open3D 사전 정리 완료 → {temp_path}")
    return temp_path


# ===========================================================
# 3️⃣ PyMeshLab 기반 클린업 (Poisson + 평탄화)
# ===========================================================
def clean_and_reconstruct(input_path, min_faces=20, poisson_depth=10, smooth_iter=15):
    """
    전체 클린업 파이프라인의 핵심 함수입니다.

    처리 순서:
    1. 손상된 줄 제거 (remove_invalid_lines)
    2. Open3D 기반 사전 정리 (preclean_with_open3d)
    3. PyMeshLab으로 Poisson 표면 복원
    4. 평탄화(Smoothing) 및 홀 메움
    5. 노멀 재계산 및 최종 저장
    """
    print("\n🧹 [Clean Mode] 스캔 메쉬 클린업 시작")

    # Step 1️⃣ 손상된 라인 정리 + Open3D 사전 정리
    input_path = remove_invalid_lines(input_path)
    input_path = preclean_with_open3d(input_path)

    ms = ml.MeshSet()
    ms.load_new_mesh(input_path)

    # Step 2️⃣ 작은 노이즈 컴포넌트 제거
    ms.apply_filter("meshing_remove_connected_component_by_face_number", mincomponentsize=min_faces)
    ms.apply_filter("meshing_remove_unreferenced_vertices")
    ms.apply_filter("meshing_remove_duplicate_faces")
    ms.apply_filter("meshing_remove_null_faces")

    # Step 3️⃣ Poisson Surface Reconstruction
    print("🔹 Poisson Surface Reconstruction 실행 중...")
    try:
        ms.apply_filter("generate_surface_reconstruction_screened_poisson", depth=poisson_depth)
        if ms.mesh_number() > 1:
            ms.set_current_mesh(ms.mesh_number() - 1)
            cur = ms.current_mesh()
            if cur.face_number() == 0:
                print("⚠️ Poisson 결과 mesh가 비어 있음 → 원본 유지")
                ms.set_current_mesh(0)
            else:
                print(f"   → 새 mesh로 전환 완료 (Face: {cur.face_number()})")
    except Exception as e:
        print(f"⚠️ Poisson 실패: {e}")

    # Step 4️⃣ 평탄화 (Smoothing)
    print("🔹 평탄화 (Smoothing)")
    try:
        ms.apply_filter("apply_coord_taubin_smoothing")
        print("   → Taubin smoothing 완료")
    except Exception as e1:
        print(f"⚠️ Taubin 실패 ({e1}), Laplacian으로 대체")
        try:
            ms.apply_filter("apply_coord_laplacian_smoothing")
            print("   → Laplacian smoothing 완료")
        except Exception as e2:
            print(f"⚠️ Laplacian smoothing 실패 ({e2})")

    # Step 5️⃣ 홀 메움 + 노멀 재계산
    print("🔹 홀 메우기 + 노멀 재계산")
    try:
        ms.apply_filter("meshing_close_holes", maxholesize=1000)
        ms.apply_filter("compute_normal_for_point_clouds", k=10)
    except Exception as e:
        print(f"⚠️ 후처리 실패: {e}")

    # Step 6️⃣ 결과 저장
    base, ext = os.path.splitext(input_path)
    output_path = f"{base}_cleaned.obj"
    ms.save_current_mesh(output_path)
    print(f"✅ 클린업 완료 → {output_path}")
    return output_path


# ===========================================================
# 4️⃣ 시각화 (Open3D)
# ===========================================================
def visualize_mesh(mesh_path):
    """
    Open3D로 결과 메쉬를 시각화합니다.
    """
    mesh = o3d.io.read_triangle_mesh(mesh_path)
    if mesh.is_empty():
        raise ValueError("❌ 시각화 실패: 메쉬가 비어 있습니다.")
    mesh.compute_vertex_normals()
    o3d.visualization.draw_geometries([mesh], window_name="Cleaned Mesh Viewer")


# ===========================================================
# 5️⃣ 실행부 (CLI 인터페이스)
# ===========================================================
if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="3D Mesh Cleaner (Safe Mode)")
    parser.add_argument("--input", type=str, required=True, help="입력 .obj 파일 경로")
    parser.add_argument("--visualize", action="store_true", help="Open3D 뷰어로 결과 시각화")
    args = parser.parse_args()

    start = time.time()
    output = clean_and_reconstruct(args.input)
    print(f"\n✅ 전체 파이프라인 완료 (총 {time.time() - start:.2f}초)")

    if args.visualize:
        visualize_mesh(output)
