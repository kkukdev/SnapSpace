import argparse
import os
import shutil
import sys


def sanitize_argv():
    if "--" in sys.argv:
        sys.argv = ["blender"] + sys.argv[sys.argv.index("--") + 1 :]
    else:
        sys.argv = ["blender"]


sanitize_argv()

import bpy  # noqa: E402


def import_mesh(path):
    extension = os.path.splitext(path)[1].lower()
    if extension in {".glb", ".gltf"}:
        bpy.ops.import_scene.gltf(filepath=path)
    elif extension == ".obj":
        try:
            bpy.ops.wm.obj_import(filepath=path)
        except AttributeError:
            bpy.ops.import_scene.obj(filepath=path)
    else:
        raise ValueError(f"Unsupported mesh format: {extension}")


def safe_name(name: str) -> str:
    base = os.path.splitext(name)[0] or "image"
    sanitized = []
    for ch in base:
        if ch.isalnum() or ch in ("_", "-"):
            sanitized.append(ch)
        else:
            sanitized.append("_")
    return "".join(sanitized).strip("_") or "image"


def export_images(output_dir: str):
    counts = {}
    saved = 0
    for image in bpy.data.images:
        if image.name == "Render Result":
            continue

        base = safe_name(image.name)
        counts[base] = counts.get(base, 0)
        if counts[base]:
            filename = f"{base}_{counts[base]}"
        else:
            filename = base
        counts[base] += 1

        if image.file_format == "JPEG":
            ext = ".jpg"
        else:
            ext = ".png"
        target_path = os.path.join(output_dir, filename + ext)

        source_path = bpy.path.abspath(image.filepath) if image.filepath else ""

        try:
            if source_path and os.path.exists(source_path):
                shutil.copy2(source_path, target_path)
            else:
                image.filepath_raw = target_path
                image.file_format = "PNG"
                image.save()
            print(f"[Extract] Saved {image.name} -> {target_path}")
            saved += 1
        except Exception as exc:
            print(f"[Extract] Failed to save {image.name}: {exc}")

    print(f"[Extract] Total textures exported: {saved}")


def main():
    parser = argparse.ArgumentParser(description="Extract embedded textures from GLB/OBJ using Blender")
    parser.add_argument("--input", required=True, help="Input GLB/OBJ path")
    parser.add_argument("--outdir", required=True, help="Directory to store extracted textures")
    args = parser.parse_args()

    input_path = os.path.abspath(args.input)
    output_dir = os.path.abspath(args.outdir)

    if not os.path.exists(input_path):
        raise FileNotFoundError(f"Input mesh not found: {input_path}")

    os.makedirs(output_dir, exist_ok=True)

    import_mesh(input_path)
    export_images(output_dir)


if __name__ == "__main__":
    main()
