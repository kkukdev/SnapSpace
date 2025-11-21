import bpy
import sys
import argparse
import os
import glob

if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="ignore")
    except Exception:
        pass

# -----------------------------------------------------------
# 🧩 Blender 내부 인자 정리 (argparse 인자 꼬임 방지)
# -----------------------------------------------------------
if "--" in sys.argv:
    idx = sys.argv.index("--")
    sys.argv = ["blender"] + sys.argv[idx + 1 :]
else:
    sys.argv = ["blender"]

# -----------------------------------------------------------
# 텍스처 연결 함수
# -----------------------------------------------------------
def ensure_uv(obj):
    if not obj or not obj.data:
        return
    if not hasattr(obj.data, "uv_layers"):
        return
    if obj.data.uv_layers:
        return
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.uv.smart_project(angle_limit=66, island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.select_set(False)


def import_mesh(path, existing_mesh_names=None):
    file_ext = os.path.splitext(path)[1].lower()
    if existing_mesh_names is None:
        existing_mesh_names = set()
    before_names = {obj.name for obj in bpy.context.scene.objects if obj.type == "MESH"}

    if file_ext in {".glb", ".gltf"}:
        print(f"📦 Importing GLB/GLTF: {path}")
        bpy.ops.import_scene.gltf(filepath=path)
    elif file_ext == ".obj":
        print(f"📦 Importing OBJ: {path}")
        try:
            bpy.ops.wm.obj_import(filepath=path)
        except AttributeError:
            bpy.ops.import_scene.obj(filepath=path)
    else:
        raise ValueError(f"지원하지 않는 파일 형식: {file_ext} (지원: .glb, .gltf, .obj)")

    all_mesh_objs = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    new_meshes = [
        obj
        for obj in all_mesh_objs
        if obj.name not in before_names and obj.name not in (existing_mesh_names or set())
    ]
    if not new_meshes:
        print("⚠️ 새로 임포트된 메쉬를 찾지 못해 씬의 모든 메쉬를 사용합니다.")
        new_meshes = all_mesh_objs
    return new_meshes


def assign_baked_textures(obj, baked_dir, use_hi_reference=True):
    print(f"🎨 Applying baked textures from {baked_dir}")
    if not obj or not obj.data:
        print("⚠️ 대상 오브젝트 없음")
        return

    # 기존 머티리얼 제거 후 새로 생성
    obj.data.materials.clear()
    mat = bpy.data.materials.new(name="BakedMaterial")
    obj.data.materials.append(mat)
    mat.use_nodes = True

    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()

    # 기본 노드 구성
    texcoord = nodes.new(type="ShaderNodeTexCoord")
    mapping = nodes.new(type="ShaderNodeMapping")
    principled = nodes.new(type="ShaderNodeBsdfPrincipled")
    output = nodes.new(type="ShaderNodeOutputMaterial")

    # 노드 배치 (보기 편하게)
    texcoord.location = (-800, 0)
    mapping.location = (-600, 0)
    principled.location = (0, 0)
    output.location = (300, 0)

    links.new(principled.outputs["BSDF"], output.inputs["Surface"])
    links.new(texcoord.outputs["UV"], mapping.inputs["Vector"])

    albedo_node = None

    # 텍스처 타입별 연결
    for tex_name, input_name in [("albedo", "Base Color"), ("ao", "Ambient Occlusion"), ("normal", "Normal")]:
        base_tex_path = os.path.join(baked_dir, f"{tex_name}.png")
        tex_path = base_tex_path
        ai_candidates = sorted(glob.glob(os.path.join(baked_dir, f"{tex_name}*_ai_inpainted*.png")))
        if ai_candidates:
            tex_path = ai_candidates[0]
        elif use_hi_reference:
            matched_candidates = sorted(glob.glob(os.path.join(baked_dir, f"{tex_name}_hi_preserved*.png")))
            if not matched_candidates:
                matched_candidates = sorted(glob.glob(os.path.join(baked_dir, f"{tex_name}_color_matched*.png")))
            if matched_candidates:
                tex_path = matched_candidates[0]
        if not os.path.exists(tex_path):
            print(f"⚠️ {tex_path} 없음 → {tex_name} 스킵")
            continue

        tex_node = nodes.new(type="ShaderNodeTexImage")
        tex_node.image = bpy.data.images.load(tex_path)
        tex_node.location = (-400, 200 - 200 * list(["albedo","ao","normal"]).index(tex_name))
        links.new(mapping.outputs["Vector"], tex_node.inputs["Vector"])

        if tex_name == "albedo":
            if use_hi_reference:
                ai_node = tex_node
                hi_reference_path = os.path.join(baked_dir, f"{tex_name}_hi_reference.png")
                if not os.path.exists(hi_reference_path):
                    hi_reference_path = base_tex_path
                hi_node = nodes.new(type="ShaderNodeTexImage")
                hi_node.image = bpy.data.images.load(hi_reference_path)
                hi_node.location = (tex_node.location[0], tex_node.location[1] + 220)
                links.new(mapping.outputs["Vector"], hi_node.inputs["Vector"])

                mix_node = nodes.new(type="ShaderNodeMixRGB")
                mix_node.blend_type = "MIX"
                mix_node.inputs[0].default_value = 0.7
                mix_node.location = (tex_node.location[0] + 200, tex_node.location[1])
                links.new(hi_node.outputs["Color"], mix_node.inputs[1])
                links.new(ai_node.outputs["Color"], mix_node.inputs[2])
                links.new(mix_node.outputs["Color"], principled.inputs["Base Color"])
                albedo_node = mix_node
            else:
                links.new(tex_node.outputs["Color"], principled.inputs["Base Color"])
                albedo_node = tex_node
        elif tex_name == "ao":
            mix_node = nodes.new(type="ShaderNodeMixRGB")
            mix_node.blend_type = "MULTIPLY"
            mix_node.inputs[0].default_value = 1.0
            mix_node.location = (-150, -100)
            links.new(tex_node.outputs["Color"], mix_node.inputs[2])

            if albedo_node:
                links.new(albedo_node.outputs["Color"], mix_node.inputs[1])
            else:
                mix_node.inputs[1].default_value = (1.0, 1.0, 1.0, 1.0)

            for link in list(links):
                if link.to_socket == principled.inputs["Base Color"]:
                    links.remove(link)
            links.new(mix_node.outputs["Color"], principled.inputs["Base Color"])
        elif tex_name == "normal":
            norm_node = nodes.new(type="ShaderNodeNormalMap")
            norm_node.location = (-150, -200)
            links.new(tex_node.outputs["Color"], norm_node.inputs["Color"])
            links.new(norm_node.outputs["Normal"], principled.inputs["Normal"])

    print("✅ 텍스처 노드 연결 완료")


# -----------------------------------------------------------
# Main
# -----------------------------------------------------------
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--lo", required=True)
    parser.add_argument("--baked", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--use-hi-reference", action="store_true", default=True, help="Blend HI reference textures into albedo")
    parser.add_argument("--no-hi-reference", dest="use_hi_reference", action="store_false", help="Disable HI reference blending")
    args = parser.parse_args()

    lo_path = os.path.abspath(args.lo)
    baked_dir = os.path.abspath(args.baked)
    out_path = os.path.abspath(args.out)

    print(f"📦 Importing low-poly asset: {lo_path}")

    # Clear existing scene objects to avoid exporting default cube or duplicates
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

    existing_mesh_names = {obj.name for obj in bpy.context.scene.objects if obj.type == "MESH"}
    imported_meshes = import_mesh(lo_path, existing_mesh_names)

    if not imported_meshes:
        print("❌ GLB 로드 실패 (메쉬 오브젝트 없음)")
        return

    for mesh_obj in imported_meshes:
        ensure_uv(mesh_obj)
        assign_baked_textures(mesh_obj, baked_dir, use_hi_reference=args.use_hi_reference)

    out_ext = os.path.splitext(out_path)[1].lower()
    if out_ext in {".glb", ".gltf"}:
        export_format = 'GLB' if out_ext == '.glb' else 'GLTF'
        print(f"💾 Exporting {export_format} → {out_path}")
        bpy.ops.export_scene.gltf(
            filepath=out_path,
            export_format=export_format,
            export_image_format='AUTO',
            export_materials='EXPORT',
        )
    elif out_ext == ".obj":
        print(f"💾 Exporting OBJ → {out_path}")
        bpy.ops.object.select_all(action='DESELECT')
        if imported_meshes:
            bpy.context.view_layer.objects.active = imported_meshes[0]
        for mesh_obj in imported_meshes:
            mesh_obj.select_set(True)
        try:
            bpy.ops.wm.obj_export(filepath=out_path, export_selected_objects=True, export_materials=True)
        except AttributeError:
            bpy.ops.export_scene.obj(filepath=out_path, use_selection=True)
    else:
        print(f"⚠️ 미지원 확장자({out_ext})로 요청되어 GLB로 내보냅니다.")
        bpy.ops.export_scene.gltf(
            filepath=out_path,
            export_format='GLB',
            export_image_format='AUTO',
            export_materials='EXPORT',
        )

    print(f"✅ Export 완료 → {out_path}")


if __name__ == "__main__":
    main()
