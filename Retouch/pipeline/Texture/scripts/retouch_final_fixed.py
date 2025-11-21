import sys

# -----------------------------------------------------------
# 🧩 Blender 내부 인자 정리 (반드시 argparse보다 위에 위치해야 함)
# -----------------------------------------------------------
def sanitize_argv():
    """
    Blender는 내부적으로 sys.argv에 Blender 실행 인자까지 포함시킵니다.
    예: ['blender', '-b', '--python', 'retouch_final_fixed.py', '--', '--hi', ...]
    아래 코드는 '--' 이후 인자만 남겨 argparse가 정확히 읽도록 합니다.
    """
    if "--" in sys.argv:
        sys.argv = ["blender"] + sys.argv[sys.argv.index("--") + 1:]
    else:
        sys.argv = ["blender"]

sanitize_argv()

import bpy
import argparse
import os
from array import array
import sys
from mathutils import Vector

if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="ignore")
    except Exception:
        pass


def ensure_dir(path: str):
    if path:
        os.makedirs(path, exist_ok=True)

# -----------------------------------------------------------
# Mesh Import (GLB/OBJ 지원)
# -----------------------------------------------------------
def import_mesh(path, clear_scene=False, existing_mesh_names=None):
    """
    GLB 또는 OBJ 파일 임포트 (자동 감지)
    
    Args:
        path: GLB 또는 OBJ 파일 경로
        clear_scene: 씬을 정리할지 여부 (기본값: False)
        existing_mesh_names: 기존 메쉬 이름 집합 (두 번째 임포트 시 제외)
    
    Returns:
        임포트된 메쉬 오브젝트
    """
    file_ext = os.path.splitext(path)[1].lower()
    
    # ✅ 씬 정리 (첫 번째 임포트 시에만)
    if clear_scene:
        bpy.ops.object.select_all(action='SELECT')
        bpy.ops.object.delete(use_global=False)
        print("🧹 씬 정리 완료")
    
    # ✅ 기존 메쉬 이름 저장 (임포트 전)
    if existing_mesh_names is None:
        existing_mesh_names = set()
    before_names = {obj.name for obj in bpy.context.scene.objects if obj.type == "MESH"}
    
    # ✅ 파일 형식에 따라 임포트
    if file_ext == '.glb' or file_ext == '.gltf':
        print(f"📂 Importing GLB/GLTF: {path}")
        bpy.ops.import_scene.gltf(filepath=path)
    elif file_ext == '.obj':
        print(f"📂 Importing OBJ: {path}")
        # Blender 4.5+ 사용 시 새로운 OBJ import 사용
        try:
            bpy.ops.wm.obj_import(filepath=path)
        except AttributeError:
            # 이전 버전 호환성
            bpy.ops.import_scene.obj(filepath=path)
    else:
        raise ValueError(f"지원하지 않는 파일 형식: {file_ext} (지원: .glb, .gltf, .obj)")

    # ✅ 임포트 후 모든 메쉬 가져오기
    all_mesh_objs = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    
    # ✅ 새로 임포트된 메쉬만 필터링 (기존 메쉬 제외)
    new_mesh_objs = [obj for obj in all_mesh_objs if obj.name not in before_names and obj.name not in existing_mesh_names]

    if not new_mesh_objs:
        print("⚠️ 새로 임포트된 메쉬가 없습니다. 모든 메쉬 중에서 선택합니다.")
        new_mesh_objs = all_mesh_objs

    print(f"🧩 가져온 메쉬 개수: {len(all_mesh_objs)} (새로 임포트: {len(new_mesh_objs)})")
    
    # ✅ 새로 임포트된 메쉬 중 가장 큰 것을 선택
    largest = max(new_mesh_objs, key=lambda o: len(o.data.vertices) if o.data else 0)
    print(f"✅ 사용 대상 오브젝트: {largest.name} (vertices: {len(largest.data.vertices) if largest.data else 0})")
    
    return largest


# -----------------------------------------------------------
# UV 생성
# -----------------------------------------------------------
def ensure_uv(obj):
    if not obj or not obj.data:
        print("⚠️ UV 생성 스킵 (유효하지 않은 오브젝트)")
        return
    if not hasattr(obj.data, "uv_layers"):
        print(f"⚠️ {obj.name} 은 메쉬 타입이 아님 → 스킵")
        return
    if not obj.data.uv_layers:
        print(f"🌀 {obj.name}: UV 자동 생성 중...")
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.uv.smart_project()
        bpy.ops.object.mode_set(mode="OBJECT")


def apply_object_transform(obj):
    if not obj:
        return
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    try:
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    except Exception as exc:
        print(f"⚠️ Transform apply failed for {obj.name}: {exc}")
    obj.select_set(False)

NEUTRAL_FILL_COLORS = {
    "albedo": (156 / 255.0, 147 / 255.0, 145 / 255.0, 1.0),
    "diffuse": (156 / 255.0, 147 / 255.0, 145 / 255.0, 1.0),
    "basecolor": (156 / 255.0, 147 / 255.0, 145 / 255.0, 1.0),
    "base_color": (156 / 255.0, 147 / 255.0, 145 / 255.0, 1.0),
    "color": (156 / 255.0, 147 / 255.0, 145 / 255.0, 1.0),
}


def prefill_generated_image(image, color, width, height):
    """
    채워지지 않은 텍셀을 검정보다 중간 회색으로 유지하기 위해
    새로 생성된 이미지를 지정 색상으로 프리필한다.
    """
    if not image or not color:
        return
    try:
        total_pixels = max(1, int(width) * int(height))
        fill_data = array("f", color)
        fill_data *= total_pixels
        image.pixels.foreach_set(fill_data)
        image.update()
        print(f"🩹 {image.name}: 초기 컬러 {color} 프리필 완료")
    except Exception as exc:
        print(f"⚠️ {image.name} 프리필 실패: {exc}")


def save_bake_image(img, path):
    ensure_dir(os.path.dirname(path))
    img.filepath_raw = path
    img.filepath = path
    try:
        img.save_render(path)
        return
    except Exception:
        pass
    try:
        img.save()
    except Exception as exc:
        print(f"⚠️ Failed to save bake image {path}: {exc}")


def convert_materials_to_projection(obj):
    """
    Force HI mesh materials to emit base color only so that EMIT bake
    acts as true projection from texture space onto LO mesh.
    """
    if not obj:
        return
    for slot in obj.material_slots:
        mat = slot.material
        if not mat or not mat.use_nodes:
            continue
        nodes = mat.node_tree.nodes
        links = mat.node_tree.links
        output = next((n for n in nodes if n.type == "OUTPUT_MATERIAL"), None)
        if not output:
            continue
        principled = next((n for n in nodes if n.type == "BSDF_PRINCIPLED"), None)
        color_socket = None
        color_default = (1.0, 1.0, 1.0, 1.0)
        if principled:
            base_input = principled.inputs.get("Base Color")
            if base_input:
                if base_input.is_linked:
                    color_socket = base_input.links[0].from_socket
                else:
                    color_default = tuple(base_input.default_value)
        emission = next((n for n in nodes if n.type == "EMISSION"), None)
        if not emission:
            emission = nodes.new(type="ShaderNodeEmission")
            emission.location = (output.location[0] - 200, output.location[1])
        if color_socket:
            links.new(color_socket, emission.inputs["Color"])
            print(f"🟢 {obj.name} material {mat.name}: linked {color_socket.name} -> emission")
        else:
            emission.inputs["Color"].default_value = color_default
            print(f"🟡 {obj.name} material {mat.name}: using default color {color_default}")
        for link in list(links):
            if link.to_node == output and link.to_socket == output.inputs["Surface"]:
                links.remove(link)
        links.new(emission.outputs["Emission"], output.inputs["Surface"])


def compute_world_diagonal(obj):
    """
    Estimate world-space diagonal length of an object's axis-aligned bounding box.
    """
    if not obj or not obj.bound_box:
        return 0.0
    world_corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    xs = [c.x for c in world_corners]
    ys = [c.y for c in world_corners]
    zs = [c.z for c in world_corners]
    min_corner = Vector((min(xs), min(ys), min(zs)))
    max_corner = Vector((max(xs), max(ys), max(zs)))
    return (max_corner - min_corner).length

# -----------------------------------------------------------
# 텍스처 Bake
# -----------------------------------------------------------
def bake_maps(hi, lo, maps, res, ray, outdir):
    os.makedirs(outdir, exist_ok=True)

    # Selected-to-active 베이크 기본 설정
    bpy.context.scene.render.bake.use_selected_to_active = True
    bpy.context.scene.render.bake.use_cage = True
    bpy.context.scene.render.bake.cage_extrusion = ray
    bpy.context.scene.render.bake.max_ray_distance = ray

    cage = lo.copy()
    cage.data = lo.data.copy()
    cage.name = f"{lo.name}_CAGE"
    bpy.context.collection.objects.link(cage)
    bpy.context.view_layer.objects.active = cage
    cage.select_set(True)
    try:
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.transform.shrink_fatten(value=ray)
        bpy.ops.object.mode_set(mode="OBJECT")
    except Exception as exc:
        print(f"⚠️ Cage inflate failed: {exc}")
        bpy.ops.object.mode_set(mode="OBJECT")
    cage.select_set(False)
    cage.hide_viewport = True
    cage.hide_render = True
    bpy.context.scene.render.bake.cage_object = cage

    # ✅ 렌더 엔진을 Cycles로 설정 (Bake 지원 필수)
    bpy.context.scene.render.engine = 'CYCLES'
    bpy.context.scene.cycles.device = 'CPU'  # 기본값

    # ✅ GPU 강제 설정 (가능하다면)
    bpy.context.scene.cycles.device = 'CPU'
    print("⚠️ Baking forced to CPU for consistent selected-to-active results")

    # ✅ ViewLayer Pass 설정 (진짜 잘 먹히는 세팅!)
    try:
        view_layer = bpy.context.scene.view_layers["ViewLayer"]
        view_layer.use_pass_diffuse_color = True   # Diffuse 색상 패스
        view_layer.use_pass_diffuse_direct = True  # 직접 조명 패스
        view_layer.use_pass_diffuse_indirect = True  # 간접 조명 패스 (주변 반사)
        print("✅ ViewLayer Pass 설정 완료")
    except KeyError:
        # ViewLayer가 없는 경우 (드물지만 안전을 위해)
        print("⚠️ ViewLayer를 찾을 수 없습니다. 기본 설정 사용")
        view_layer = bpy.context.view_layer
    
    # ✅ Bake 설정 개선 (원본 색상 최대한 보존 및 매칭 강화)
    bpy.context.scene.render.bake.use_pass_direct = True  # 직접 조명 포함
    bpy.context.scene.render.bake.use_pass_indirect = True  # 간접 조명 포함 (주변 반사)
    bpy.context.scene.render.bake.use_pass_color = True  # 색상 정보 사용
    bpy.context.scene.render.bake.cage_extrusion = ray  # ray distance로 주변 색 샘플링 범위 확장
    bpy.context.scene.render.bake.margin_type = 'ADJACENT_FACES'  # 인접 면 기반 마진 (인근 색 참고)
    
    # ✅ 원본 색상 보존을 위한 추가 설정
    bpy.context.scene.render.bake.margin = 4  # 마진 크기 (인근 색 참고 범위)

    # ✅ 모든 선택 해제
    bpy.ops.object.select_all(action='DESELECT')

    # ✅ 대상 오브젝트 강제 활성화
    if hi.type != "MESH" or lo.type != "MESH":
        print(f"❌ Bake 대상이 메쉬가 아닙니다. (hi={hi.type}, lo={lo.type})")
        return

    # ✅ HI mesh의 원본 머티리얼 보존 확인
    if not hi.data.materials or len(hi.data.materials) == 0:
        print("⚠️ HI mesh에 머티리얼이 없습니다. 원본 텍스처를 복원할 수 없습니다.")
    else:
        print(f"✅ HI mesh 원본 머티리얼 확인: {len(hi.data.materials)}개")

    for map_name in maps.split(","):
        map_name = map_name.strip()
        if not map_name:
            continue
        print(f"[Bake] Baking {map_name}...")

        bpy.ops.object.select_all(action='DESELECT')

        bpy.ops.object.select_all(action='DESELECT')

        lo.select_set(True)
        hi.select_set(True)
        bpy.context.view_layer.objects.active = lo

        img = bpy.data.images.new(map_name, width=res, height=res)
        img.filepath_raw = os.path.join(outdir, f"{map_name}.png")
        img.file_format = "PNG"
        map_key = map_name.lower()
        neutral_color = NEUTRAL_FILL_COLORS.get(map_key)
        if neutral_color:
            prefill_generated_image(img, neutral_color, res, res)
        try:
            if map_name in {"normal", "ao"}:
                img.colorspace_settings.name = "Non-Color"
            else:
                img.colorspace_settings.name = "sRGB"
        except Exception:
            pass

        mat = bpy.data.materials.new(name=f"{map_name}_mat")
        lo.data.materials.clear()
        lo.data.materials.append(mat)
        for poly in lo.data.polygons:
            poly.material_index = 0
        mat.use_nodes = True

        nodes = mat.node_tree.nodes
        links = mat.node_tree.links
        nodes.clear()

        tex_node = nodes.new(type="ShaderNodeTexImage")
        tex_node.image = img
        output = nodes.new(type="ShaderNodeOutputMaterial")
        links.new(tex_node.outputs["Color"], output.inputs["Surface"])
        for node in nodes:
            node.select = False
        tex_node.select = True
        nodes.active = tex_node

        if map_key in {"ao", "ambientocclusion"}:
            bake_type = "AO"
        elif map_key in {"normal", "normalmap"}:
            bake_type = "NORMAL"
        else:
            bake_type = "DIFFUSE"

        bpy.context.scene.render.bake.use_pass_color = True
        if bake_type == "DIFFUSE":
            bpy.context.scene.render.bake.use_pass_direct = False
            bpy.context.scene.render.bake.use_pass_indirect = False
        elif bake_type == "AO":
            bpy.context.scene.render.bake.use_pass_direct = False
            bpy.context.scene.render.bake.use_pass_indirect = False
        else:  # NORMAL
            bpy.context.scene.render.bake.use_pass_direct = True
            bpy.context.scene.render.bake.use_pass_indirect = True

        try:
            bpy.context.scene.cycles.bake_type = bake_type
        except Exception:
            pass

        bpy.context.view_layer.objects.active = lo
        selected_names = [obj.name for obj in bpy.context.selected_objects]
        print(f"[Bake] Selected objects: {selected_names}, active={bpy.context.view_layer.objects.active.name}")

        bake_kwargs = dict(
            type=bake_type,
            margin=8,
            use_selected_to_active=True,
        )

        try:
            result = bpy.ops.object.bake(**bake_kwargs)
            print(f"[Bake] Bake result: {result}")
        except Exception as e:
            print(f"[Bake] Bake failed ({e}); retrying with relaxed settings")
            fallback_kwargs = dict(bake_kwargs)
            fallback_kwargs["margin"] = 4
            result = bpy.ops.object.bake(**fallback_kwargs)
            print(f"[Bake] Fallback bake result: {result}")

        try:
            pixels = list(img.pixels)
            if pixels:
                avg = sum(pixels) / len(pixels)
                sample = ", ".join(f"{v:.3f}" for v in pixels[:12])
                print(f"[Bake] {map_name} pixel mean (linear RGBA) -> {avg:.4f} | sample {sample}")
        except Exception as exc:
            print(f"[Bake] Failed to sample pixels for {map_name}: {exc}")

        save_bake_image(img, img.filepath_raw)
        print(f"[Bake] {map_name} saved -> {img.filepath_raw}")

    # ✅ Bake 후 선택 해제
    bpy.ops.object.select_all(action='DESELECT')

    bpy.context.scene.render.bake.cage_object = None
    bpy.context.scene.render.bake.use_cage = False
    try:
        bpy.data.objects.remove(cage, do_unlink=True)
    except Exception:
        pass


# -----------------------------------------------------------
# 텍스처 연결
# -----------------------------------------------------------
def assign_baked_textures(obj, baked_dir):
    print(f"🎨 Applying baked textures from {baked_dir}")
    if not obj or not obj.data:
        print("⚠️ 대상 오브젝트 없음")
        return

    obj.data.materials.clear()
    mat = bpy.data.materials.new(name="BakedMaterial")
    obj.data.materials.append(mat)
    mat.use_nodes = True

    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()

    principled = nodes.new(type="ShaderNodeBsdfPrincipled")
    output = nodes.new(type="ShaderNodeOutputMaterial")
    principled.location = (0, 0)
    output.location = (300, 0)
    links.new(principled.outputs["BSDF"], output.inputs["Surface"])

    for tex_name, input_name in [("albedo", "Base Color"), ("ao", "Ambient Occlusion"), ("normal", "Normal")]:
        tex_path = os.path.join(baked_dir, f"{tex_name}.png")
        if not os.path.exists(tex_path):
            print(f"⚠️ {tex_path} 없음 → {tex_name} 스킵")
            continue

        tex_node = nodes.new(type="ShaderNodeTexImage")
        tex_node.image = bpy.data.images.load(tex_path)
        tex_node.label = tex_name
        tex_node.location = (-400, 200 - 200 * list(["albedo","ao","normal"]).index(tex_name))

        if tex_name == "albedo":
            links.new(tex_node.outputs["Color"], principled.inputs["Base Color"])

        elif tex_name == "ao":
            # ✅ AO는 Base Color와 Multiply 블렌딩
            mix_node = nodes.new(type="ShaderNodeMixRGB")
            mix_node.blend_type = 'MULTIPLY'
            mix_node.inputs[0].default_value = 1.0
            mix_node.location = (-150, -100)

            # AO 출력 → Mix Color1
            links.new(tex_node.outputs["Color"], mix_node.inputs["Color1"])

            # Base Color 입력을 Principled 대신 Mix로 재연결
            base_color_input = principled.inputs["Base Color"]
            existing_link = None

            # 기존 Base Color 연결 탐색
            for link in list(links):
                if link.to_socket == base_color_input:
                    existing_link = link
                    break

            # 기존 연결 제거 + 안전하게 재연결
            if existing_link:
                from_socket = existing_link.from_socket  # ✅ 삭제 전 안전 복사
                links.remove(existing_link)
                links.new(from_socket, mix_node.inputs["Color2"])

            # Mix 출력 → Principled Base Color 연결
            links.new(mix_node.outputs["Color"], base_color_input)


        elif tex_name == "normal":
            norm_node = nodes.new(type="ShaderNodeNormalMap")
            norm_node.location = (-150, -200)
            links.new(tex_node.outputs["Color"], norm_node.inputs["Color"])
            links.new(norm_node.outputs["Normal"], principled.inputs["Normal"])

    print("✅ 텍스처 노드 연결 완료")


# -----------------------------------------------------------
# 외부 텍스처 디렉토리 재연결
# -----------------------------------------------------------
def relink_textures_from_directory(texture_dir):
    texture_dir = os.path.abspath(texture_dir)
    if not os.path.isdir(texture_dir):
        print(f"⚠️ 외부 텍스처 디렉토리를 찾을 수 없습니다: {texture_dir}")
        return

    texture_map = {}
    for entry in os.listdir(texture_dir):
        lower = entry.lower()
        if lower.endswith((".png", ".jpg", ".jpeg", ".tga", ".exr")):
            texture_map[lower] = os.path.join(texture_dir, entry)

    if not texture_map:
        print(f"⚠️ 외부 텍스처 디렉토리에 이미지가 없습니다: {texture_dir}")
        return

    relinked = 0
    for image in bpy.data.images:
        if image.packed_file:
            try:
                image.unpack(method='USE_ORIGINAL')
            except Exception:
                pass
        basename = os.path.basename(image.filepath or image.name).lower()
        if basename in texture_map:
            image.filepath = texture_map[basename]
            try:
                image.reload()
            except Exception:
                pass
            relinked += 1

    print(f"✅ 외부 텍스처 재연결 완료: {relinked}개 이미지 -> {texture_dir}")


# -----------------------------------------------------------
# Inpainting 후처리 (Blender 외부에서 실행)
# -----------------------------------------------------------
def run_inpainting_postprocess(baked_dir, maps, use_ai=False, use_controlnet=True, guidance_scale=7.5, inference_steps=20, hi_mesh_path=None, device_preference: str = "auto"):
    """
    Bake 후 Inpainting 후처리 실행
    Blender 외부 Python 스크립트를 subprocess로 호출
    
    Args:
        baked_dir: Bake된 텍스처 디렉토리
        maps: 처리할 맵 목록
        use_ai: AI 모델 사용 여부
        use_controlnet: ControlNet Tile 사용 여부 (AI 모드에서만)
        guidance_scale: Guidance scale (AI 모드)
        inference_steps: 추론 스텝 수 (AI 모드)
        hi_mesh_path: 원본 HI mesh 파일 경로 (원본 텍스처 찾기용)
    """
    import subprocess
    import sys
    import shutil
    
    script_dir = os.path.dirname(os.path.abspath(__file__))
    inpaint_script = os.path.join(script_dir, "texture_inpaint.py")
    
    if not os.path.exists(inpaint_script):
        print("⚠️ Inpainting 스크립트를 찾을 수 없습니다. 스킵합니다.")
        return
    
    # ✅ 시스템 Python 경로 찾기 (Blender 내장 Python이 아닌)
    # Blender 내부에서 실행 중이므로 sys.executable은 Blender Python을 가리킴
    # 환경 변수나 PATH에서 시스템 Python 찾기
    python_exe = None
    
    # 1. 환경 변수에서 찾기
    if 'PYTHON_EXECUTABLE' in os.environ:
        python_exe = os.environ['PYTHON_EXECUTABLE']
        if os.path.exists(python_exe):
            print(f"✅ 환경 변수에서 Python 발견: {python_exe}")
    
    # 2. PATH에서 python 찾기
    if not python_exe:
        for python_name in ['python', 'python3', 'py']:
            python_exe = shutil.which(python_name)
            if python_exe:
                print(f"✅ PATH에서 Python 발견: {python_exe}")
                break
    
    # 3. 일반적인 Python 경로 시도 (Windows)
    if not python_exe and sys.platform == 'win32':
        common_paths = [
            r"C:\Python311\python.exe",
            r"C:\Python310\python.exe",
            r"C:\Python39\python.exe",
            r"C:\Program Files\Python311\python.exe",
            r"C:\Program Files\Python310\python.exe",
        ]
        for path in common_paths:
            if os.path.exists(path):
                python_exe = path
                print(f"✅ 일반 경로에서 Python 발견: {python_exe}")
                break
    
    if not python_exe:
        print("⚠️ 시스템 Python을 찾을 수 없습니다.")
        print("💡 해결 방법: 환경 변수 PYTHON_EXECUTABLE을 설정하거나 PATH에 python을 추가하세요.")
        print("   예: set PYTHON_EXECUTABLE=C:\\Python311\\python.exe")
        return
    
    # ✅ 원본 텍스처 디렉토리 찾기 (HI mesh 파일 경로에서)
    original_texture_dir = None
    if hi_mesh_path and os.path.exists(hi_mesh_path):
        # HI mesh 파일이 있는 디렉토리에서 원본 텍스처 찾기
        hi_mesh_dir = os.path.dirname(os.path.abspath(hi_mesh_path))
        # 같은 디렉토리에서 PNG/JPG 파일 찾기
        if os.path.exists(hi_mesh_dir):
            png_files = [f for f in os.listdir(hi_mesh_dir) if f.lower().endswith(('.png', '.jpg'))]
            if png_files:
                original_texture_dir = hi_mesh_dir
                print(f"✅ 원본 텍스처 디렉토리 발견: {original_texture_dir} ({len(png_files)}개 텍스처 파일)")
    
    try:
        cmd = [
            python_exe,  # 시스템 Python 인터프리터 사용
            inpaint_script,
            "--input", os.path.abspath(baked_dir),
            "--maps", maps,
        ]
        
        # ✅ 원본 텍스처 디렉토리 전달
        if original_texture_dir and os.path.exists(original_texture_dir):
            cmd.extend(["--original-texture-dir", os.path.abspath(original_texture_dir)])
            print(f"✅ 원본 텍스처 디렉토리 전달: {original_texture_dir}")
        
        # ✅ 디바이스 전달 (cuda / cpu / auto)
        if device_preference in ("cuda", "cpu"):
            cmd.extend(["--device", device_preference])
        
        if use_ai:
            cmd.append("--use-ai")
            if use_controlnet:
                cmd.append("--use-controlnet")
            else:
                cmd.append("--no-controlnet")
            cmd.extend(["--guidance-scale", str(guidance_scale)])
            cmd.extend(["--inference-steps", str(inference_steps)])
        
        print(f"\n🎨 Inpainting 후처리 실행 중...")
        if use_ai:
            print(f"   🤖 AI 모드: Stable Diffusion + {'ControlNet Tile' if use_controlnet else '기본 Inpaint'}")
        result = subprocess.run(cmd, capture_output=True, text=True, check=True)
        print(result.stdout)
        if result.stderr:
            print(f"⚠️ Inpainting 경고: {result.stderr}")
    except subprocess.CalledProcessError as e:
        print(f"⚠️ Inpainting 오류: {e}")
        print(f"   출력: {e.stdout}")
        print(f"   오류: {e.stderr}")
    except Exception as e:
        print(f"⚠️ Inpainting 실행 중 예외 발생: {e}")


# -----------------------------------------------------------
# Main
# -----------------------------------------------------------
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--hi", required=True)
    parser.add_argument("--lo", required=True)
    parser.add_argument("--maps", default="albedo,ao,normal")
    parser.add_argument("--res", type=int, default=2048)
    parser.add_argument("--ray", type=float, default=0.05)
    parser.add_argument("--uv-policy", default="unwrap")
    parser.add_argument("--outdir", default="./outputs/baked")
    parser.add_argument("--out", default="./outputs/retouched.glb")
    parser.add_argument("--external-texture-dir", help="베이크 전에 재연결할 외부 텍스처 디렉토리")
    parser.add_argument("--inpaint", action="store_true", help="Bake 후 Inpainting 후처리 적용")
    parser.add_argument("--use-ai-inpaint", action="store_true", help="AI 기반 Inpainting 사용 (Stable Diffusion + ControlNet Tile)")
    parser.add_argument("--use-controlnet", action="store_true", default=True, help="ControlNet Tile 사용 (AI 모드에서만)")
    parser.add_argument("--no-controlnet", dest="use_controlnet", action="store_false", help="ControlNet Tile 비활성화")
    parser.add_argument("--guidance-scale", type=float, default=7.5, help="Guidance scale (AI 모드)")
    parser.add_argument("--inference-steps", type=int, default=20, help="추론 스텝 수 (AI 모드)")
    parser.add_argument("--skip-inpaint", action="store_true", help="Bake 후 Inpainting 단계를 건너뜁니다.")
    args = parser.parse_args()

    # ✅ 첫 번째 Mesh 임포트 (HI mesh) - 씬 정리 (GLB/OBJ 자동 감지)
    hi = import_mesh(os.path.abspath(args.hi), clear_scene=True)
    if not hi:
        print("❌ HI mesh Import 실패. 종료.")
        return
    
    # ✅ HI mesh 이름 변경 (구분을 위해)
    hi.name = f"{hi.name}_HI"
    print(f"💾 HI mesh 이름 설정: {hi.name}")

    if args.external_texture_dir:
        relink_textures_from_directory(args.external_texture_dir)

    # ✅ 기존 메쉬 이름 저장 (LO 임포트 시 제외하기 위해)
    existing_mesh_names = {hi.name}
    
    # ✅ 두 번째 Mesh 임포트 (LO mesh) - 씬 유지, 기존 메쉬 제외 (GLB/OBJ 자동 감지)
    lo = import_mesh(os.path.abspath(args.lo), clear_scene=False, existing_mesh_names=existing_mesh_names)
    if not lo:
        print("❌ LO mesh Import 실패. 종료.")
        return

    # ✅ LO mesh 이름 변경 (구분을 위해)
    lo.name = f"{lo.name}_LO"
    print(f"💾 LO mesh 이름 설정: {lo.name}")

    apply_object_transform(hi)
    apply_object_transform(lo)
    hi.hide_render = False
    hi.hide_set(False)
    lo.hide_render = False
    lo.hide_set(False)
    print(f"✅ HI location {tuple(round(v, 4) for v in hi.location)}, LO location {tuple(round(v, 4) for v in lo.location)}")

    # ✅ HI와 LO가 다른 오브젝트인지 확인
    if hi == lo:
        print(f"❌ HI와 LO가 같은 오브젝트입니다! (hi={hi.name}, lo={lo.name})")
        return
    
    print(f"✅ 최종 오브젝트 확인: HI={hi.name} (vertices: {len(hi.data.vertices) if hi.data else 0}), LO={lo.name} (vertices: {len(lo.data.vertices) if lo.data else 0})")

    if args.uv_policy == "unwrap":
        ensure_uv(lo)
    if getattr(lo.data, "uv_layers", None):
        lo.data.uv_layers.active_index = len(lo.data.uv_layers) - 1
        lo.data.uv_layers.active = lo.data.uv_layers[-1]

    print(f"✅ HI UV layers: {len(getattr(hi.data, 'uv_layers', []))}, LO UV layers: {len(getattr(lo.data, 'uv_layers', []))}")

    hi_diag = compute_world_diagonal(hi)
    lo_diag = compute_world_diagonal(lo)
    auto_ray = max(args.ray, 0.02 * max(hi_diag, lo_diag, 1e-3))
    print(f"📏 Auto cage extrusion 적용: {auto_ray:.4f} (입력 {args.ray:.4f}, diag 기반 {0.02 * max(hi_diag, lo_diag, 1e-3):.4f})")

    bake_maps(
        hi,
        lo,
        args.maps,
        args.res,
        auto_ray,
        os.path.abspath(args.outdir),
    )
    
    # ✅ Inpainting 후처리 적용 (옵션)
    if args.inpaint and not args.skip_inpaint:
        run_inpainting_postprocess(
            os.path.abspath(args.outdir), 
            args.maps, 
            args.use_ai_inpaint,
            args.use_controlnet,
            args.guidance_scale,
            args.inference_steps,
            hi_mesh_path=os.path.abspath(args.hi),  # ✅ 원본 HI mesh 경로 전달
            device_preference="cuda"  # ✅ 우선 GPU 사용 시도
        )
    elif args.inpaint and args.skip_inpaint:
        print("⚠️ skip-inpaint 옵션으로 인해 후처리 Inpainting을 건너뜁니다.")
    
    assign_baked_textures(lo, os.path.abspath(args.outdir))

    out_path = os.path.abspath(args.out)
    out_ext = os.path.splitext(out_path)[1].lower()
    
    # ✅ 파일 형식에 따라 Export (GLB/OBJ 자동 감지)
    if out_ext == '.glb' or out_ext == '.gltf':
        print(f"💾 Exporting GLB/GLTF → {out_path}")
        export_format = 'GLB' if out_ext == '.glb' else 'GLTF'
        bpy.ops.export_scene.gltf(filepath=out_path, export_format=export_format, export_image_format='AUTO', export_materials='EXPORT')
    elif out_ext == '.obj':
        print(f"💾 Exporting OBJ → {out_path}")
        # OBJ export 시 선택된 오브젝트만 export
        bpy.ops.object.select_all(action='DESELECT')
        lo.select_set(True)
        bpy.context.view_layer.objects.active = lo
        # Blender 4.5+ 사용 시 새로운 OBJ export 사용
        try:
            bpy.ops.wm.obj_export(filepath=out_path, export_selected_objects=True, export_materials=True)
        except AttributeError:
            # 이전 버전 호환성
            bpy.ops.export_scene.obj(filepath=out_path, use_selection=True)
    else:
        # 기본값: GLB
        print(f"💾 Exporting GLB (기본값) → {out_path}")
        bpy.ops.export_scene.gltf(filepath=out_path, export_format='GLB', export_image_format='AUTO', export_materials='EXPORT')
    
    print(f"✅ 전체 완료 → {out_path}")

if __name__ == "__main__":
    main()
