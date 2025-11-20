# obj_to_glb.py
import trimesh
import sys
import io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='ignore')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='ignore')


def convert_obj_to_glb(input_path, output_path=None):
    if output_path is None:
        output_path = input_path.replace('.obj', '.glb')

    print(f"🔹 Loading OBJ: {input_path}")
    mesh = trimesh.load(input_path)

    print("🔹 Exporting to GLB (binary format)...")
    mesh.export(output_path)

    print(f"✅ Done! Saved as {output_path}")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python obj_to_glb.py input.obj [output.glb]")
    else:
        input_file = sys.argv[1]
        output_file = sys.argv[2] if len(sys.argv) > 2 else None
        convert_obj_to_glb(input_file, output_file)
