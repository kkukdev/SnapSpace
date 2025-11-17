import argparse
import os
import shutil
import struct
import subprocess
import sys
import time
from dataclasses import dataclass
from typing import Optional, Sequence, Tuple


ROOT_DIR = os.path.dirname(__file__)
SCRIPTS_DIR = os.path.join(ROOT_DIR, "scripts")
ALIGN_SCRIPT = os.path.join(ROOT_DIR, "align_meshes.py")

DEFAULT_HI = os.path.join("datasets", "obj", "mainhall.obj")
DEFAULT_LO = os.path.join("datasets", "obj", "mainhall_auto_flat.obj")
TEXTURE_EXTENSIONS = (".png", ".jpg", ".jpeg", ".tga", ".exr")


@dataclass(frozen=True)
class PipelinePaths:
    hi_input: str
    hi_for_pipeline: str
    lo: str
    final_asset: str
    blender: str
    outdir: str
    pipeline_dir: str
    original_tex_dir: str
    baked_raw_dir: str
    intermediate_glb: str


@dataclass(frozen=True)
class ICPSettings:
    samples: int
    voxel_ratio: float
    max_iter: int


@dataclass(frozen=True)
class BakeSettings:
    maps: str
    resolution: int
    ray_distance: float
    uv_policy: str


@dataclass(frozen=True)
class InpaintSettings:
    enabled: bool
    maps: str
    device: str
    use_ai_inpaint: bool
    use_controlnet: bool
    guidance_scale: float
    inference_steps: int


def to_windows_path(path: str) -> str:
    """Translate WSL-style /mnt/<drive>/... paths into Windows drive notation when needed."""
    if os.name != "nt" or not path:
        return path

    path = path.strip().strip('"')
    lowered = path.lower()

    def convert_fragment(fragment: str) -> str:
        parts = fragment.split("/", 3)
        if len(parts) >= 3 and len(parts[2]) == 1:
            drive = parts[2].upper()
            remainder = parts[3] if len(parts) >= 4 else ""
            remainder = remainder.replace("/", "\\")
            return f"{drive}:\\{remainder}"
        return fragment

    if "/mnt/" in lowered:
        fragment = path[lowered.find("/mnt/") :]
        path = convert_fragment(fragment)

    if path.startswith("/mnt/") and len(path) > 5 and path[5].isalpha():
        if len(path) == 6:
            return f"{path[5].upper()}:\\"
        if len(path) > 6 and path[6] == "/":
            drive = path[5].upper()
            rest = path[7:].replace("/", "\\")
            return f"{drive}:\\{rest}"

    if path.startswith("\\mnt\\") and len(path) > 5 and path[5].isalpha():
        drive = path[5].upper()
        rest = path[7:].replace("/", "\\")
        return f"{drive}:\\{rest}"

    return path


def ensure_dir(path: Optional[str]) -> None:
    if path:
        os.makedirs(path, exist_ok=True)


def resolve_pipeline_paths(args: argparse.Namespace) -> PipelinePaths:
    hi_abs = os.path.abspath(args.hi)
    lo_abs = os.path.abspath(args.lo)
    final_asset = os.path.abspath(args.out)
    blender_exe = to_windows_path(args.blender)
    if not os.path.isabs(blender_exe):
        blender_exe = os.path.abspath(blender_exe)

    if not os.path.exists(hi_abs):
        raise FileNotFoundError(f"High mesh not found: {hi_abs}")
    if not os.path.exists(lo_abs):
        raise FileNotFoundError(f"Low mesh not found: {lo_abs}")
    if not os.path.exists(blender_exe):
        raise FileNotFoundError(
            f"Blender executable not found: {blender_exe}\n"
            "Please pass --blender with a valid blender.exe path (e.g., "
            r'"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe").'
        )

    outdir = os.path.abspath(args.outdir)
    ensure_dir(outdir)
    pipeline_dir = os.path.abspath(args.pipeline_dir) if args.pipeline_dir else os.path.join(outdir, "pipeline")
    ensure_dir(pipeline_dir)

    sanitized_hi = sanitize_glb(hi_abs, pipeline_dir)
    hi_for_pipeline = sanitized_hi if sanitized_hi != hi_abs else hi_abs

    original_tex_dir = os.path.join(pipeline_dir, "original_textures")
    baked_raw_dir = os.path.join(pipeline_dir, "baked_raw")
    intermediate_glb = os.path.join(pipeline_dir, "pre_inpaint.glb")
    ensure_dir(original_tex_dir)
    ensure_dir(baked_raw_dir)

    return PipelinePaths(
        hi_input=hi_abs,
        hi_for_pipeline=hi_for_pipeline,
        lo=lo_abs,
        final_asset=final_asset,
        blender=blender_exe,
        outdir=outdir,
        pipeline_dir=pipeline_dir,
        original_tex_dir=original_tex_dir,
        baked_raw_dir=baked_raw_dir,
        intermediate_glb=intermediate_glb,
    )


def should_use_hi_reference(args: argparse.Namespace) -> bool:
    if args.hi_ref_mode == "on":
        return True
    if args.hi_ref_mode == "off":
        return False
    return args.uv_policy == "keep"


def sanitize_glb(glb_path: str, work_dir: str) -> str:
    """Rewrite GLB files containing NaN values so Blender/Open3D can consume them safely."""
    extension = os.path.splitext(glb_path)[1].lower()
    if extension not in {".glb", ".gltf"}:
        return glb_path

    with open(glb_path, "rb") as fh:
        header = fh.read(12)
        if len(header) != 12:
            raise ValueError("Invalid GLB header.")
        magic, version, _ = struct.unpack("<4sII", header)
        if magic != b"glTF":
            raise ValueError("Input file is not a valid GLB.")

        json_header = fh.read(8)
        if len(json_header) != 8:
            raise ValueError("Invalid GLB JSON chunk.")
        json_length, json_type = struct.unpack("<I4s", json_header)
        if json_type != b"JSON":
            raise ValueError("Unexpected chunk type for JSON section.")
        json_bytes = fh.read(json_length)

        bin_header = fh.read(8)
        if len(bin_header) != 8:
            raise ValueError("Invalid GLB BIN chunk.")
        bin_length, bin_type = struct.unpack("<I4s", bin_header)
        if not bin_type.startswith(b"BIN"):
            raise ValueError("Unexpected chunk type for BIN section.")
        bin_bytes = fh.read(bin_length)

    json_text = json_bytes.decode("utf-8", errors="ignore")
    if "NaN" not in json_text:
        return glb_path

    sanitized_text = json_text.replace("NaN", "0")
    sanitized_bytes = sanitized_text.encode("utf-8")
    json_padding = (4 - (len(sanitized_bytes) % 4)) % 4
    json_padded = sanitized_bytes + (b" " * json_padding)

    bin_padding = (4 - (bin_length % 4)) % 4
    if bin_padding:
        bin_bytes = bin_bytes + (b"\x00" * bin_padding)
        bin_length += bin_padding

    total_length = 12 + 8 + len(json_padded) + 8 + bin_length

    ensure_dir(work_dir)
    base_name = os.path.splitext(os.path.basename(glb_path))[0]
    sanitized_path = os.path.join(work_dir, f"{base_name}_fixed{extension}")

    with open(sanitized_path, "wb") as out:
        out.write(struct.pack("<4sII", b"glTF", version, total_length))
        out.write(struct.pack("<I4s", len(json_padded), b"JSON"))
        out.write(json_padded)
        out.write(struct.pack("<I4s", bin_length, b"BIN\x00"))
        out.write(bin_bytes)

    print(f"[Pipeline] NaN detected. Sanitized GLB saved -> {sanitized_path}")
    return sanitized_path


def run_subprocess(cmd: Sequence[str], label: str) -> None:
    printable = " ".join(cmd)
    print(f"\n[Pipeline] {label}: {printable}")
    subprocess.run(cmd, check=True)


def run_blender(blender_exe: str, script_name: str, args_list: Sequence[str]) -> None:
    script_path = os.path.join(SCRIPTS_DIR, script_name)
    if not os.path.exists(script_path):
        raise FileNotFoundError(f"Blender script not found: {script_path}")

    blender_path = to_windows_path(blender_exe)
    if not os.path.isabs(blender_path):
        blender_path = os.path.abspath(blender_path)
    if not os.path.exists(blender_path):
        raise FileNotFoundError(
            f"Blender executable not found: {blender_path}\n"
            "Please pass --blender with a valid blender.exe path (e.g., "
            r'"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe").'
        )

    cmd = [
        blender_path,
        "-b",
        "--python",
        script_path.replace("\\", "/"),
        "--",
        *args_list,
    ]
    run_subprocess(cmd, label="Running Blender")


def run_python(script_name: str, args_list: Sequence[str]) -> None:
    script_path = os.path.join(SCRIPTS_DIR, script_name)
    if not os.path.exists(script_path):
        raise FileNotFoundError(f"Python script not found: {script_path}")

    cmd = [sys.executable, script_path, *args_list]
    run_subprocess(cmd, label="Running helper script")


def align_low_mesh(hi_path: str, lo_path: str, work_dir: str, icp: ICPSettings) -> Tuple[str, str]:
    if not os.path.exists(ALIGN_SCRIPT):
        raise FileNotFoundError(f"Alignment script missing: {ALIGN_SCRIPT}")

    align_dir = os.path.join(work_dir, "alignment")
    ensure_dir(align_dir)
    aligned_mesh = os.path.join(align_dir, "receiver_aligned.obj")
    report_path = os.path.join(align_dir, "alignment_report.json")

    cmd = [
        sys.executable,
        ALIGN_SCRIPT,
        "--hi",
        hi_path,
        "--lo",
        lo_path,
        "--out-mesh",
        aligned_mesh,
        "--report",
        report_path,
        "--samples",
        str(icp.samples),
        "--voxel-ratio",
        str(icp.voxel_ratio),
        "--max-iter",
        str(icp.max_iter),
    ]
    run_subprocess(cmd, label="Aligning meshes via ICP")
    return aligned_mesh, report_path


def extract_original_textures(blender_exe: str, hi_mesh: str, output_dir: str) -> None:
    ensure_dir(output_dir)
    run_blender(
        blender_exe,
        "extract_textures_blender.py",
        [
            "--input",
            hi_mesh.replace("\\", "/"),
            "--outdir",
            output_dir.replace("\\", "/"),
        ],
    )


def purge_texture_outputs(baked_dir: str) -> None:
    if not os.path.isdir(baked_dir):
        return
    for entry in os.listdir(baked_dir):
        if entry.lower().endswith(TEXTURE_EXTENSIONS):
            try:
                os.remove(os.path.join(baked_dir, entry))
            except OSError:
                pass


def bake_textures(
    blender_exe: str,
    hi_mesh: str,
    aligned_lo_mesh: str,
    baked_dir: str,
    intermediate_glb: str,
    original_tex_dir: str,
    settings: BakeSettings,
) -> None:
    ensure_dir(baked_dir)
    purge_texture_outputs(baked_dir)
    run_blender(
        blender_exe,
        "retouch_final_fixed.py",
        [
            "--hi",
            hi_mesh.replace("\\", "/"),
            "--lo",
            aligned_lo_mesh.replace("\\", "/"),
            "--maps",
            settings.maps,
            "--res",
            str(settings.resolution),
            "--ray",
            str(settings.ray_distance),
            "--uv-policy",
            settings.uv_policy,
            "--outdir",
            baked_dir.replace("\\", "/"),
            "--out",
            intermediate_glb.replace("\\", "/"),
            "--external-texture-dir",
            original_tex_dir.replace("\\", "/"),
            "--skip-inpaint",
        ],
    )


def inpaint_textures(baked_dir: str, original_tex_dir: str, settings: InpaintSettings, use_hi_reference: bool) -> None:
    cmd = [
        "--input",
        baked_dir,
        "--maps",
        settings.maps,
        "--device",
        settings.device,
        "--original-texture-dir",
        original_tex_dir,
    ]
    if settings.use_ai_inpaint:
        cmd.append("--use-ai")
        if settings.use_controlnet:
            cmd.append("--use-controlnet")
        else:
            cmd.append("--no-controlnet")
        cmd.extend(
            [
                "--guidance-scale",
                str(settings.guidance_scale),
                "--inference-steps",
                str(settings.inference_steps),
            ]
        )
    if use_hi_reference:
        cmd.append("--use-hi-reference")
    else:
        cmd.append("--no-hi-reference")
    run_python("texture_inpaint.py", cmd)


def export_final_asset(blender_exe: str, lo_mesh: str, baked_dir: str, final_asset: str, use_hi_reference: bool) -> None:
    ensure_dir(os.path.dirname(final_asset))
    export_args = [
        "--lo",
        lo_mesh.replace("\\", "/"),
        "--baked",
        baked_dir.replace("\\", "/"),
        "--out",
        final_asset.replace("\\", "/"),
    ]
    if not use_hi_reference:
        export_args.append("--no-hi-reference")
    run_blender(
        blender_exe,
        "retouch_export_fixed.py",
        export_args,
    )


class TexturePipeline:
    """High-level orchestration for the hybrid texture workflow."""

    def __init__(self, args: argparse.Namespace):
        self.args = args
        self.paths = resolve_pipeline_paths(args)
        self.icp = ICPSettings(args.icp_samples, args.icp_voxel_ratio, args.icp_max_iter)
        self.bake_settings = BakeSettings(args.maps, args.res, args.ray, args.uv_policy)
        self.inpaint_settings = InpaintSettings(
            enabled=not args.skip_inpaint,
            maps=args.maps,
            device=args.device,
            use_ai_inpaint=args.use_ai_inpaint,
            use_controlnet=args.use_controlnet,
            guidance_scale=args.guidance_scale,
            inference_steps=args.inference_steps,
        )
        self.use_hi_reference = should_use_hi_reference(args)
        self.start_time = 0.0

    def run(self) -> None:
        self.start_time = time.time()
        aligned_mesh, align_report = self._prepare_low_mesh()
        self._extract_textures()
        self._bake_textures(aligned_mesh)

        if self.args.stage == "bake":
            self._finalize_bake_stage()
            return

        self._maybe_inpaint()
        self._export_final_asset()
        self._print_summary(align_report)

    def _prepare_low_mesh(self) -> Tuple[str, Optional[str]]:
        if self.args.skip_align:
            print("[Pipeline] ICP alignment skipped (using provided low mesh)")
            return self.paths.lo, None
        return align_low_mesh(
            self.paths.hi_for_pipeline,
            self.paths.lo,
            self.paths.pipeline_dir,
            self.icp,
        )

    def _extract_textures(self) -> None:
        extract_original_textures(self.paths.blender, self.paths.hi_for_pipeline, self.paths.original_tex_dir)

    def _bake_textures(self, aligned_mesh: str) -> None:
        bake_textures(
            self.paths.blender,
            self.paths.hi_for_pipeline,
            aligned_mesh,
            self.paths.baked_raw_dir,
            self.paths.intermediate_glb,
            self.paths.original_tex_dir,
            self.bake_settings,
        )

    def _finalize_bake_stage(self) -> None:
        ensure_dir(os.path.dirname(self.paths.final_asset))
        shutil.copy2(self.paths.intermediate_glb, self.paths.final_asset)
        print(f"[Pipeline] Bake stage complete -> {self.paths.intermediate_glb}")
        print(f"[Pipeline] Bake output copied to : {self.paths.final_asset}")
        print(f"[Pipeline] Original textures      : {self.paths.original_tex_dir}")
        print(f"[Pipeline] Baked textures         : {self.paths.baked_raw_dir}")

    def _maybe_inpaint(self) -> None:
        if not self.use_hi_reference:
            print("[Pipeline] HI reference blending disabled (new UVs will rely on baked/inpainted textures only)")

        if not self.inpaint_settings.enabled:
            print("[Pipeline] Inpaint stage skipped -> histogram blending disabled")
            return

        inpaint_textures(
            self.paths.baked_raw_dir,
            self.paths.original_tex_dir,
            self.inpaint_settings,
            use_hi_reference=self.use_hi_reference,
        )

    def _export_final_asset(self) -> None:
        export_final_asset(
            self.paths.blender,
            self.paths.intermediate_glb,
            self.paths.baked_raw_dir,
            self.paths.final_asset,
            use_hi_reference=self.use_hi_reference,
        )

    def _print_summary(self, align_report: Optional[str]) -> None:
        elapsed = time.time() - self.start_time
        print("\n[Pipeline] All stages complete")
        if align_report:
            print(f"[Pipeline] Alignment report   : {align_report}")
        print(f"[Pipeline] Original textures  : {self.paths.original_tex_dir}")
        print(f"[Pipeline] Baked (pre-inpaint): {self.paths.baked_raw_dir}")
        print(f"[Pipeline] Final asset        : {self.paths.final_asset}")
        print(f"[Pipeline] Total runtime      : {elapsed:.2f}s")


def parse_args(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Hybrid texture workflow (ICP alignment -> Blender bake -> AI inpaint -> histogram blend -> final export)"
    )
    parser.add_argument("--hi", default=DEFAULT_HI, help="Original high-poly mesh (.glb/.gltf/.obj)")
    parser.add_argument("--lo", default=DEFAULT_LO, help="Optimized low-poly mesh (.glb/.gltf/.obj)")
    parser.add_argument("--out", required=True, help="Final mesh export path (.glb/.gltf/.obj)")
    parser.add_argument("--blender", required=True, help="Path to Blender executable")
    parser.add_argument("--maps", default="albedo,ao,normal", help="Comma separated maps to bake and refine")
    parser.add_argument("--res", type=int, default=2048, help="Bake resolution")
    parser.add_argument("--ray", type=float, default=0.05, help="Bake cage extrusion distance")
    parser.add_argument("--uv-policy", choices=["keep", "unwrap"], default="unwrap", help="UV policy for receiver mesh")
    parser.add_argument("--outdir", default="./outputs/hybrid", help="Directory for final + intermediate outputs")
    parser.add_argument("--pipeline-dir", help="Directory for intermediate artifacts (defaults to <outdir>/pipeline)")
    parser.add_argument("--device", choices=["auto", "cuda", "cpu"], default="auto", help="Device for AI inpainting")
    parser.add_argument("--hi-ref-mode", choices=["auto", "on", "off"], default="auto", help="HI reference usage: auto=only when UV kept, on=force blend, off=disable")
    parser.add_argument("--use-ai-inpaint", action="store_true", help="Enable Stable Diffusion + ControlNet inpainting")
    parser.add_argument("--use-controlnet", action="store_true", default=True, help="Enable ControlNet Tile during AI inpaint")
    parser.add_argument("--no-controlnet", dest="use_controlnet", action="store_false", help="Disable ControlNet Tile")
    parser.add_argument("--guidance-scale", type=float, default=7.5, help="Guidance scale for AI inpainting")
    parser.add_argument("--inference-steps", type=int, default=12, help="Inference steps for AI inpainting")
    parser.add_argument("--skip-align", action="store_true", help="Skip ICP alignment stage and reuse provided low mesh")
    parser.add_argument("--skip-inpaint", action="store_true", help="Skip AI/LAB inpainting (use baked textures as-is)")
    parser.add_argument("--icp-samples", type=int, default=250000, help="Sample count for ICP alignment")
    parser.add_argument("--icp-voxel-ratio", type=float, default=0.02, help="Base voxel ratio for ICP alignment")
    parser.add_argument("--icp-max-iter", type=int, default=60, help="Finest stage iteration count for ICP alignment")
    parser.add_argument(
        "--stage",
        choices=["bake", "final"],
        default="final",
        help="Select 'bake' to stop after producing pre_inpaint.glb, or 'final' to export the final asset",
    )
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> None:
    args = parse_args(argv)
    pipeline = TexturePipeline(args)
    pipeline.run()


if __name__ == "__main__":
    main()
