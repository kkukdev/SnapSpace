import open3d as o3d
import pymeshlab as ml
import argparse
import os
import time
import numpy as np

# -----------------------------------------------------------
# 1️⃣ 손상된 OBJ 자동 복구 (잘린 줄 제거)
# -----------------------------------------------------------
def remove_invalid_lines(input_path):
    with open(input_path, "r", encoding="utf-8", errors="ignore") as f:
        lines = f.readlines()
    valid = []
    for line in lines:
        s = line.strip()
        if not s:
            continue
        if s.startswith(("v ", "vt ", "vn ", "f ", "usemtl", "o ", "s ")):
            valid.append(line)
        elif not any(c.isalpha() for c in s):
            continue  # 숫자만 있는 잘린 줄 제거
        else:
            valid.append(line)
    tmp = input_path.replace(".obj", "_safe.obj")
    with open(tmp, "w", encoding="utf-8") as f:
        f.writelines(valid)
    print(f"🧹 손상된 라인 제거 완료 → {tmp}")
    return tmp


# -----------------------------------------------------------
# 2️⃣ Open3D 기반 사전 클린 (비정상 vertex/face 제거)
# -----------------------------------------------------------
def preclean_with_open3d(input_path):
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


# -----------------------------------------------------------
# 3️⃣ PyMeshLab 기반 클린업 (Poisson + 평탄화)
# -----------------------------------------------------------
def clean_and_reconstruct(input_path, min_faces=20, poisson_depth=10, smooth_iter=15):
    print("\n🧹 [Clean Mode] 스캔 메쉬 클린업 시작")

    # Step 1: 손상된 줄 정리 + Open3D 사전정리
    input_path = remove_invalid_lines(input_path)
    input_path = preclean_with_open3d(input_path)

    ms = ml.MeshSet()
    ms.load_new_mesh(input_path)

    ms.apply_filter("meshing_remove_connected_component_by_face_number", mincomponentsize=min_faces)
    ms.apply_filter("meshing_remove_unreferenced_vertices")
    ms.apply_filter("meshing_remove_duplicate_faces")
    ms.apply_filter("meshing_remove_null_faces")

    # Step 2: Poisson 재구성
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

    # Step 3: 평탄화 (Taubin → Laplacian fallback)
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

    # Step 4: 홀 메우기 + 노멀 재계산
    print("🔹 홀 메우기 + 노멀 재계산")
    try:
        ms.apply_filter("meshing_close_holes", maxholesize=1000)
        ms.apply_filter("compute_normal_for_point_clouds", k=10)
    except Exception as e:
        print(f"⚠️ 후처리 실패: {e}")

    # Step 5: 저장
    base, ext = os.path.splitext(input_path)
    output_path = f"{base}_cleaned.obj"
    ms.save_current_mesh(output_path)
    print(f"✅ 클린업 완료 → {output_path}")
    return output_path


# -----------------------------------------------------------
# 4️⃣ 시각화
# -----------------------------------------------------------
def visualize_mesh(mesh_path):
    mesh = o3d.io.read_triangle_mesh(mesh_path)
    if mesh.is_empty():
        raise ValueError("❌ 시각화 실패: 메쉬가 비어 있습니다.")
    mesh.compute_vertex_normals()
    o3d.visualization.draw_geometries([mesh], window_name="Cleaned Mesh Viewer")


# -----------------------------------------------------------
# 5️⃣ 실행부
# -----------------------------------------------------------
if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Mesh Cleaner (Safe Mode)")
    parser.add_argument("--input", type=str, required=True)
    parser.add_argument("--visualize", action="store_true")
    args = parser.parse_args()

    start = time.time()
    output = clean_and_reconstruct(args.input)
    print(f"\n✅ 전체 파이프라인 완료 (총 {time.time() - start:.2f}초)")
    if args.visualize:
        visualize_mesh(output)





# python mesh_optimizer.py   --input ../../datasets/obj/mainhall.obj   --visualize
