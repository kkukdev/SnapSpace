import argparse
import open3d as o3d
import numpy as np
import copy
import time

# ----------------------------------------
# 메쉬 단순화(Decimation) 함수
# ----------------------------------------
def simplify_mesh(mesh, target_ratio=0.5):
    """
    메쉬의 삼각형 개수를 줄이는 함수.
    - target_ratio: 남길 삼각형 비율 (0~1)
    """
    # 현재 삼각형 수 대비 목표 삼각형 수 계산
    target_triangles = int(len(mesh.triangles) * target_ratio)
    print(f"🔹 폴리곤 수 {len(mesh.triangles)} → {target_triangles} (목표 비율: {target_ratio})")

    # Open3D의 Quadric Decimation으로 삼각형 수 줄이기
    simplified = mesh.simplify_quadric_decimation(target_triangles)

    # 줄인 메쉬의 정점 노멀 재계산 (광원과 시각화 품질 향상)
    simplified.compute_vertex_normals()
    return simplified



# -----------------------------------------------------------
# ✅ 작은 노이즈 조각(삼각형 개수가 너무 적은 부분)을 제거
# -----------------------------------------------------------
def remove_loose_parts(mesh, min_triangles=100):
    labels = np.array(mesh.cluster_connected_triangles()[0])
    cluster_tri_counts = np.bincount(labels)
    large_clusters = np.where(cluster_tri_counts > min_triangles)[0]
    mask = np.isin(labels, large_clusters)
    mesh.remove_triangles_by_mask(~mask)
    mesh.remove_unreferenced_vertices()
    return mesh


# -----------------------------------------------------------
# ✅ 시각화: 좌우 비교 or 겹침 비교
# -----------------------------------------------------------
def visualize_meshes(original_mesh, optimized_mesh, mode="full"):
    vis = o3d.visualization.VisualizerWithKeyCallback()

    title = "Mesh Optimizer Viewer"
    if mode == "overlay":
        title += " (겹침 비교)"
    else:
        title += " (좌: 원본, 우: 최적화본)"

    vis.create_window(window_name=title, width=1600, height=900)

    # 🟡 원본 / 🔵 최적화본 복제 및 색상 지정
    original = copy.deepcopy(original_mesh)
    optimized = copy.deepcopy(optimized_mesh)
    original.paint_uniform_color([1.0, 0.706, 0.0])    # 노란색
    optimized.paint_uniform_color([0.0, 0.651, 0.929]) # 파란색

    if mode == "full":
        # 좌우로 배치
        bbox = original.get_axis_aligned_bounding_box()
        offset = bbox.get_extent()[0] * 0.5
        original.translate((-offset, 0, 0))
        optimized.translate((offset, 0, 0))
    elif mode == "overlay":
        # 겹치기 (원본 반투명)
        opt = vis.get_render_option()
        opt.mesh_show_back_face = True
        opt.background_color = np.array([0.0, 0.0, 0.0])
        opt.line_width = 1.0
        opt.point_size = 2.0
        opt.mesh_show_wireframe = False
        opt.show_coordinate_frame = True

        # 원본을 투명하게 만들기 위해 점 크기 조정 (시각적 투명효과)
        original.paint_uniform_color([1.0, 0.8, 0.2])
        optimized.paint_uniform_color([0.0, 0.651, 0.929])

    vis.add_geometry(original)
    vis.add_geometry(optimized)

    # 🔹 상태 변수
    state = {
        "wireframe": False,
        "auto_rotate": False,
        "show_fps": True,
        "last_time": time.time(),
        "frames": 0,
    }

    # 🔹 단축키 안내
    print("\n🎮 조작 방법")
    print("──────────────────────────────")
    print("[W] : 와이어프레임 토글")
    print("[R] : 자동 회전 토글")
    print("[F] : FPS 표시 토글")
    print("[H] : 도움말 표시")
    print("[ESC] : 종료\n")

    # 🔸 와이어프레임 모드 전환
    def toggle_wireframe(vis):
        state["wireframe"] = not state["wireframe"]
        render = vis.get_render_option()
        render.mesh_show_wireframe = state["wireframe"]
        print(f"🔹 와이어프레임: {'ON' if state['wireframe'] else 'OFF'}")
        return False

    # 🔸 자동 회전 토글
    def toggle_auto_rotate(vis):
        state["auto_rotate"] = not state["auto_rotate"]
        print(f"🔹 자동 회전: {'ON' if state['auto_rotate'] else 'OFF'}")
        return False

    # 🔸 FPS 표시 토글
    def toggle_fps(vis):
        state["show_fps"] = not state["show_fps"]
        print(f"🔹 FPS 표시: {'ON' if state['show_fps'] else 'OFF'}")
        return False

    # 🔸 도움말 출력
    def show_help(vis):
        print("\n🎮 조작 방법")
        print("──────────────────────────────")
        print("[W] : 와이어프레임 토글")
        print("[R] : 자동 회전 토글")
        print("[F] : FPS 표시 토글")
        print("[H] : 도움말 표시")
        print("[ESC] : 종료\n")
        return False

    # 🔸 ESC로 종료
    def close_viewer(vis):
        vis.destroy_window()
        return False

    # 🔸 자동 회전 및 FPS 업데이트
    def update(vis):
        if state["auto_rotate"]:
            ctr = vis.get_view_control()
            ctr.rotate(1.0, 0.0)

        if state["show_fps"]:
            state["frames"] += 1
            now = time.time()
            if now - state["last_time"] >= 1.0:
                fps = state["frames"] / (now - state["last_time"])
                print(f"🕹️ FPS: {fps:.1f}")
                state["frames"] = 0
                state["last_time"] = now
        return False

    # 🔸 단축키 등록
    vis.register_key_callback(ord("W"), toggle_wireframe)
    vis.register_key_callback(ord("R"), toggle_auto_rotate)
    vis.register_key_callback(ord("F"), toggle_fps)
    vis.register_key_callback(ord("H"), show_help)
    vis.register_key_callback(256, close_viewer)  # ESC
    vis.register_animation_callback(update)

    vis.run()
    vis.destroy_window()


# -----------------------------------------------------------
# ✅ 메쉬 최적화 전체 파이프라인
# -----------------------------------------------------------
def optimize_mesh(input_path, target_ratio=0.5, min_triangles=100, visualize=True, mode="full"):
    print(f"✅ '{input_path}' 로드 중...")
    mesh = o3d.io.read_triangle_mesh(input_path)
    if mesh.is_empty():
        raise ValueError("❌ 메쉬가 비어 있습니다.")

    mesh.compute_vertex_normals()
    print(f"   - 원본 삼각형 수: {len(mesh.triangles)}")

    # 🔹 폴리곤 감소
    target_triangles = int(len(mesh.triangles) * target_ratio)
    print(f"🔹 폴리곤 {len(mesh.triangles)} → {target_triangles} (비율 {target_ratio})")
    simplified = mesh.simplify_quadric_decimation(target_triangles)
    simplified.remove_unreferenced_vertices()

    # 🔹 노이즈 제거
    print(f"🔹 작은 조각 제거 중... (삼각형 {min_triangles}개 미만)")
    cleaned = remove_loose_parts(simplified, min_triangles)
    print(f"   → 제거 후 삼각형 수: {len(cleaned.triangles)}")

    # 🔹 결과 저장
    output_path = input_path.replace(".obj", "_optimized.obj")
    o3d.io.write_triangle_mesh(output_path, cleaned)
    print(f"💾 최적화 완료 → {output_path}")

    # 🔹 시각화 실행
    if visualize:
        visualize_meshes(mesh, cleaned, mode=mode)


# -----------------------------------------------------------
# ✅ 명령행 실행부
# -----------------------------------------------------------
if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="3D Mesh Optimizer (좌우 비교 + 겹침 비교 지원)")
    parser.add_argument("--input", type=str, required=True, help=".obj 파일 경로")
    parser.add_argument("--target_ratio", type=float, default=0.5, help="감소시킬 비율 (0~1)")
    parser.add_argument("--min_triangles", type=int, default=100, help="노이즈 최소 삼각형 수")
    parser.add_argument("--visualize", action="store_true", help="시각화 창 띄우기")
    parser.add_argument("--mode", choices=["full", "overlay"], default="full", help="시각화 모드 선택")
    args = parser.parse_args()

    optimize_mesh(args.input, args.target_ratio, args.min_triangles, args.visualize, mode=args.mode)


'''
python mesh_optimizer.py --input Scaniverse_2025_10_15_165232.obj --target_ratio 0.4 --visualize --mode full

마우스 조작 지원: 회전, 이동, 줌

키보드 단축키:
    Q / Esc: 창 종료
    R: 카메라 리셋
    W: 와이어프레임 토글 (코드에서 이미 켬)

    

1️⃣ simplify_mesh(mesh, target_ratio=0.5)

역할: 메쉬 삼각형 수를 줄이는 Decimation 기능.

인자
   - mesh: open3d.geometry.TriangleMesh 객체
   - target_ratio: 0~1 사이의 목표 삼각형 비율 (예: 0.5 → 50%로 감소)

동작
    1. 현재 삼각형 수 확인
    2. 목표 삼각형 수 계산
    3. mesh.simplify_quadric_decimation(target_triangles)로 삼각형 감소
    4. 정점 노멀(vertex normals) 재계산

출력: 간소화된 TriangleMesh 객체


2️⃣ remove_loose_parts(mesh, min_triangles=100)

역할: 메쉬 내 작은 조각(노이즈) 제거

인자
    - mesh: TriangleMesh 객체
    - min_triangles: 제거 기준 삼각형 수 (이거보다 작으면 삭제)

동작
    1. mesh.cluster_connected_triangles()로 연결된 삼각형 그룹 식별
    2. 각 그룹의 삼각형 개수 계산
    3. min_triangles보다 작은 그룹 제거
    4. 사용되지 않는 정점 제거 및 노멀 재계산

출력: 노이즈 제거된 TriangleMesh 객체


3️⃣ visualize_meshes(mesh_list, ...)

역할: Open3D를 이용해 메쉬를 시각화

주요 기능
    - 좌우 나란히 배치 (side_by_side=True & mesh_list가 2개)
    - 메쉬 색상 지정 (colors=[[r,g,b], ...])
    - FPS 표시 (Open3D 기본 뷰어 기능)
    - 와이어프레임 보기 (opt.mesh_show_wireframe=True)
    - 자동 회전 (auto_rotate=True)
        - ctr.rotate(1.0, 0.0) 반복으로 카메라 회전
    - 좌표축 표시 (opt.show_coordinate_frame=True)
    - 마우스로 확대/축소/회전 가능

인자
    - mesh_list: 시각화할 메쉬 리스트
    - colors: 각 메쉬 색상
    - window_name: 창 이름
    - auto_rotate: True이면 회전
    - side_by_side: True이면 2개의 메쉬를 좌우 배치

출력: 창 띄워서 실시간 시각화


4️⃣ optimize_mesh(input_path, target_ratio=0.5, min_triangles=100, visualize=False, mode="full")

역할: 메쉬 로드 → 단순화 → 노이즈 제거 → 저장 → 시각화

인자
    - input_path: .obj 파일 경로
    - target_ratio: 삼각형 감소 비율
    - min_triangles: 노이즈 제거 기준 삼각형 수
    - visualize: True면 뷰어 실행
    - mode: 시각화 모드
        - "original": 원본만
        - "optimized": 최적화본만
        - "full": 원본 + 최적화본 좌우 비교

동작
    1. .obj 파일 로드
    2. 원본 삼각형 수 출력
    3. simplify_mesh로 삼각형 수 감소
    4. remove_loose_parts로 작은 조각 제거
    5. _optimized.obj로 저장
    6. visualize_meshes 호출 (선택)

출력: 최적화된 파일 경로


5️⃣ __main__ (커맨드라인 인터페이스)

사용 예시
    # 원본과 최적화본 좌우 비교
    python mesh_optimizer.py --input dummy.obj --visualize --mode full
    # 원본만 보기
    python mesh_optimizer.py --input dummy.obj --visualize --mode original
    # 최적화본만 보기
    python mesh_optimizer.py --input dummy.obj --visualize --mode optimized
    # 삼각형 40%로 감소
    python mesh_optimizer.py --input dummy.obj --target_ratio 0.4 --visualize --mode full

인자
    --input: 필수, .obj 파일 경로
    --target_ratio: w
    --min_triangles: 노이즈 제거 기준 (기본 100)
    --visualize: 시각화 켜기
    --mode: "original", "optimized", "full" 선택
'''