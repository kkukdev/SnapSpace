import os
import argparse
import numpy as np
import random
import shutil
import contextlib
import time

os.environ["CUDA_VISIBLE_DEVICES"] = "0"
os.environ["TF_CPP_MIN_LOG_LEVEL"] = "3"
os.environ["TF_FORCE_GPU_ALLOW_GROWTH"] = "true"
os.environ["CUDA_DEVICE_ORDER"] = "PCI_BUS_ID"
os.environ["TF_ENABLE_ONEDNN_OPTS"] = "0"

try:
    from skimage.exposure import match_histograms
    HAS_SKIMAGE = True
except ImportError:
    HAS_SKIMAGE = False

# ??OpenCV import (?택??- ?으?OpenCV 기능 비활?화)
try:
    import cv2
    HAS_OPENCV = True
except ImportError:
    HAS_OPENCV = False
    print("?️ OpenCV (cv2)가 ?치?? ?았?니?? OpenCV 기반 Inpainting? ?용?????습?다.")
    print("? ?치 방법: pip install opencv-python")

try:
    from PIL import Image
    HAS_PIL = True
except ImportError:
    HAS_PIL = False
    print("?️ PIL (Pillow)가 ?치?? ?았?니??")
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='ignore')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='ignore')


def color_transfer_bgr(source_bgr, reference_bgr):
    if not HAS_OPENCV:
        return source_bgr

    src = cv2.cvtColor(source_bgr, cv2.COLOR_BGR2LAB).astype(np.float32)
    ref = cv2.cvtColor(reference_bgr, cv2.COLOR_BGR2LAB).astype(np.float32)

    src_mean, src_std = cv2.meanStdDev(src)
    ref_mean, ref_std = cv2.meanStdDev(ref)

    src_std[src_std == 0] = 1.0
    ref_std[ref_std == 0] = 1.0

    src_mean = src_mean.reshape((1, 1, 3))
    src_std = src_std.reshape((1, 1, 3))
    ref_mean = ref_mean.reshape((1, 1, 3))
    ref_std = ref_std.reshape((1, 1, 3))

    result = ((src - src_mean) / src_std) * ref_std + ref_mean
    result = np.clip(result, 0, 255).astype(np.uint8)
    result_bgr = cv2.cvtColor(result, cv2.COLOR_LAB2BGR)
    return result_bgr



def preserve_hi_color_bgr(lo_bgr, hi_bgr, alpha=0.7):
    if not HAS_OPENCV:
        return lo_bgr
    try:
        hi_resized = cv2.resize(hi_bgr, (lo_bgr.shape[1], lo_bgr.shape[0]), interpolation=cv2.INTER_CUBIC)
        if HAS_SKIMAGE:
            lo_rgb = cv2.cvtColor(lo_bgr, cv2.COLOR_BGR2RGB)
            hi_rgb = cv2.cvtColor(hi_resized, cv2.COLOR_BGR2RGB)
            matched_rgb = match_histograms(lo_rgb, hi_rgb, channel_axis=-1)
            matched_bgr = cv2.cvtColor(np.clip(matched_rgb, 0, 255).astype(np.uint8), cv2.COLOR_RGB2BGR)
        else:
            matched_bgr = color_transfer_bgr(lo_bgr, hi_resized)
        blended = cv2.addWeighted(matched_bgr.astype(np.float32), 1.0 - alpha, hi_resized.astype(np.float32), alpha, 0.0)
        return np.clip(blended, 0, 255).astype(np.uint8)
    except Exception as exc:
        print(f'[warn] preserve_hi_color_bgr failed: {exc}')
        return lo_bgr


def blend_hi_color_preserve(lo_img_path, hi_img_path, alpha=0.6):
    if not HAS_OPENCV:
        return lo_img_path
    try:
        lo = cv2.imread(lo_img_path, cv2.IMREAD_COLOR)
        hi = cv2.imread(hi_img_path, cv2.IMREAD_COLOR)
        if lo is None or hi is None or lo.size == 0 or hi.size == 0:
            return lo_img_path
        hi_resized = cv2.resize(hi, (lo.shape[1], lo.shape[0]), interpolation=cv2.INTER_CUBIC)
        if HAS_SKIMAGE:
            lo_rgb = cv2.cvtColor(lo, cv2.COLOR_BGR2RGB)
            hi_rgb = cv2.cvtColor(hi_resized, cv2.COLOR_BGR2RGB)
            matched_rgb = match_histograms(lo_rgb, hi_rgb, channel_axis=-1)
            matched_bgr = cv2.cvtColor(np.clip(matched_rgb, 0, 255).astype(np.uint8), cv2.COLOR_RGB2BGR)
        else:
            matched_bgr = color_transfer_bgr(lo, hi_resized)
        blended = cv2.addWeighted(matched_bgr.astype(np.float32), 1.0 - alpha, hi_resized.astype(np.float32), alpha, 0.0)
        blended = np.clip(blended, 0, 255).astype(np.uint8)
        out_path = lo_img_path.replace('.png', '_hi_preserved.png')
        cv2.imwrite(out_path, blended)
        return out_path
    except Exception as exc:
        print(f'Color blend failed: {exc}')
        return lo_img_path

PIPELINE_CACHE = {}
CONTROLNET_CACHE = {}
PRESET_CONFIGS = {
    "ctrl": {
        "use_ai": True,
        "use_controlnet": True,
        "device": "cuda",
        "guidance_scale": 8.0,
        "inference_steps": 32,
        "max_side": 960,
        "controlnet_scale": 1.0,
    },
    "ctrl_preview": {
        "use_ai": True,
        "use_controlnet": False,
        "device": "cuda",
        "guidance_scale": 6.5,
        "inference_steps": 12,
        "max_side": 640,
        "controlnet_scale": 0.5,
    },
    "ctrl_hq": {
        "use_ai": True,
        "use_controlnet": True,
        "device": "cuda",
        "guidance_scale": 8.5,
        "inference_steps": 40,
        "max_side": 1024,
        "controlnet_scale": 1.0,
    },
}


def detect_anomalous_colors_lab(image, preserve_original=True):
    """
    LAB ?공?기반 ?계??마스???성 (기존 ??최???보존!)
    mean - 2.5*std ~ mean + 2.5*std 범위 밖의 ????"?머지 부??로 감?
    기존 ?상? 보존?고, ?공간/???는 부분만 ?근 ?으?채우?
    
    Args:
        image: BGR ??지 (cv2.imread??? ??지)
        preserve_original: 기존 ?상 보존 모드 (True: 기존 ?? 보존, False: 모든 ?상????감?)
    
    Returns:
        마스????지 (0=?상/기존 ?? 255=?머지 부?채울 ?역)
    """
    if not HAS_OPENCV:
        raise ImportError("OpenCV가 ?요?니?? pip install opencv-python")
    
    # LAB ?공간으?변??
    lab = cv2.cvtColor(image, cv2.COLOR_BGR2LAB)
    
    # LAB 채널??균????차 계산
    mean, std = cv2.meanStdDev(lab)
    mean = mean.flatten()
    std = std.flatten()
    
    # mean ± 2.5*std 범위 밖의 ?? 감? (?머지 부?
    lower_bound = (mean - 2.5 * std).astype(np.uint8)
    upper_bound = (mean + 2.5 * std).astype(np.uint8)
    
    # 범위 밖의 ????마스?로 ?성
    mask = cv2.inRange(lab, lower_bound, upper_bound)
    
    # 마스??반전 (범위 ?= ?머지 부?
    mask = cv2.bitwise_not(mask)
    
    # ??기존 ?상 보존: 검????색/?색 ?역?감? (?연?러???? 보존)
    if preserve_original:
        # L 채널 (밝기)?검????색 ?역 감?
        l_channel = lab[:, :, 0]
        
        # 검????역 (밝기 < 10)
        black_mask = (l_channel < 10).astype(np.uint8) * 255
        
        # ?색 ?역 (밝기 > 245)
        white_mask = (l_channel > 245).astype(np.uint8) * 255
        
        # ?상 분산????? ?역 (?색/?상???역)
        # ??? 주????상 분산 계산
        kernel_size = 5
        kernel = np.ones((kernel_size, kernel_size), np.float32) / (kernel_size * kernel_size)
        
        # ?채널??분산 계산
        lab_float = lab.astype(np.float32)
        mean_local = cv2.filter2D(lab_float, -1, kernel)
        diff = lab_float - mean_local
        variance = np.mean(diff ** 2, axis=2)
        low_variance_mask = (variance < 50).astype(np.uint8) * 255  # 분산????? ?역
        
        # 마스??결합: 검????색/?색 ?역?채우?
        combined_mask = cv2.bitwise_or(cv2.bitwise_or(black_mask, white_mask), low_variance_mask)
        
        # 기존 마스?? 교집??(?상???이면서 검????색/?색???역?
        mask = cv2.bitwise_and(mask, combined_mask)
    
    # 모폴로? ?산?로 마스???제 (?? ?이??거)
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3))
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)
    
    return mask


def detect_anomalous_colors(image, threshold=30):
    """
    ?상????검??? ?색, ?는 주???게 ?른 ????감??여 마스???성
    (기존 방법 - LAB ?계 방법????좋음)
    
    Args:
        image: PIL Image ?는 numpy array
        threshold: ?상 차이 ?계?
    
    Returns:
        마스????지 (0=?상, 255=?상)
    """
    if not HAS_OPENCV:
        raise ImportError("OpenCV가 ?요?니?? pip install opencv-python")
    
    if isinstance(image, Image.Image):
        img_array = np.array(image)
    else:
        img_array = image
    
    # RGB?LAB?변??(?상 차이????확?게 측정)
    lab = cv2.cvtColor(img_array, cv2.COLOR_RGB2LAB)
    l, a, b = cv2.split(lab)
    
    # 검????색 ?역 감?
    black_mask = (l < 10) & (np.abs(a - 128) < 10) & (np.abs(b - 128) < 10)
    white_mask = l > 245
    
    # 주? ?과 ?게 ?른 ?역 감? (edge detection + ?상 차이)
    gray = cv2.cvtColor(img_array, cv2.COLOR_RGB2GRAY)
    
    # 가?시??블러??이??거
    blurred = cv2.GaussianBlur(gray, (5, 5), 0)
    
    # ?플?시?으?급격??변??감?
    laplacian = cv2.Laplacian(blurred, cv2.CV_64F)
    edge_mask = np.abs(laplacian) > threshold
    
    # ?상 분산????? ?역 (?색/?상???역)
    kernel = np.ones((5, 5), np.float32) / 25
    mean_img = cv2.filter2D(img_array.astype(np.float32), -1, kernel)
    std_img = cv2.filter2D((img_array.astype(np.float32) - mean_img) ** 2, -1, kernel)
    std_mask = np.mean(std_img, axis=2) < 100  # ?상 분산????? ?역
    
    # 모든 마스??결합
    combined_mask = (black_mask | white_mask | (edge_mask & std_mask)).astype(np.uint8) * 255
    
    # 모폴로? ?산?로 마스???제
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3))
    combined_mask = cv2.morphologyEx(combined_mask, cv2.MORPH_CLOSE, kernel)
    combined_mask = cv2.morphologyEx(combined_mask, cv2.MORPH_OPEN, kernel)
    
    # ?? ?이??거
    contours, _ = cv2.findContours(combined_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    min_area = 10  # 최소 ?역 ?기
    clean_mask = np.zeros_like(combined_mask)
    for contour in contours:
        if cv2.contourArea(contour) > min_area:
            cv2.drawContours(clean_mask, [contour], -1, 255, -1)
    
    return clean_mask


def inpaint_with_opencv(image_path, mask_path=None, output_path=None, method='ns', use_lab_detection=True):
    """
    OpenCV??용??Inpainting (진짜 ??먹히???팅!)
    
    Args:
        image_path: ?력 ??지 경로
        mask_path: 마스????지 경로 (None?면 ?동 감?)
        output_path: 출력 ??지 경로
        method: 'telea' ?는 'ns' (Navier-Stokes) - 기본?'ns' 권장
        use_lab_detection: LAB ?계 기반 마스???용 (True 권장)
    
    Returns:
        보정????지 경로
    """
    if not HAS_OPENCV:
        raise ImportError("OpenCV가 ?요?니?? pip install opencv-python")
    
    # ??지 로드 (BGR ?식?로)
    img = cv2.imread(image_path, cv2.IMREAD_COLOR)
    if img is None:
        raise ValueError(f"??지?로드?????습?다: {image_path}")
    
    # 마스???성 ?는 로드
    if mask_path and os.path.exists(mask_path):
        mask = cv2.imread(mask_path, cv2.IMREAD_GRAYSCALE)
    else:
        # ??LAB ?계 기반 마스???성 (기존 ??최???보존!)
        if use_lab_detection:
            # preserve_original=True: 기존 ?상? 보존?고, ?공간?감?
            mask = detect_anomalous_colors_lab(img, preserve_original=True)
        else:
            # 기존 방법 (fallback)
            img_rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
            mask = detect_anomalous_colors(img_rgb)
    
    # 마스?? 비어?으??본 반환
    if np.sum(mask) == 0:
        print("?️ 보정???역??감??? ?았?니??")
        if output_path:
            cv2.imwrite(output_path, img)
            return output_path
        return image_path
    
    # ??Inpainting ?행 (?근 ?상 참고 강화: INPAINT_NS, radius=25)
    # radius??게 ?면 ???? 범위???근 ?을 참고?여 ?연?럽?채?
    if method == 'ns':
        # Navier-Stokes 방법 (?근 ?상 참고 강화??연?러??결과)
        inpainted = cv2.inpaint(img, mask, 25, cv2.INPAINT_NS)
    else:  # 'telea'
        # Telea 방법???근 ??참고 범위 ??
        inpainted = cv2.inpaint(img, mask, 15, cv2.INPAINT_TELEA)
    
    # 결과 ???
    if output_path is None:
        base, ext = os.path.splitext(image_path)
        output_path = f"{base}_inpainted{ext}"
    
    cv2.imwrite(output_path, inpainted)
    print(f"??Inpainting ?료 ??{output_path}")
    print(f"   보정???? ?? {np.sum(mask > 0)}")
    
    return output_path



def inpaint_with_ai(
    image_path,
    mask_path=None,
    output_path=None,
    use_controlnet=True,
    guidance_scale=7.5,
    num_inference_steps=20,
    reference_image_path=None,
    device_preference=None,
    max_side=1024,
    controlnet_scale=1.0,
    seed=None,
):
    # Run AI-assisted inpainting using Stable Diffusion (optional ControlNet).
    try:
        import torch
        from torch import autocast
        from diffusers import (
            ControlNetModel,
            StableDiffusionControlNetInpaintPipeline,
            StableDiffusionInpaintPipeline,
        )
        from diffusers.schedulers import DPMSolverMultistepScheduler
        from PIL import Image as PILImage
        import numpy as np
    except ImportError as exc:
        print(f"⚠️ Missing AI inpainting dependencies: {exc}")
        print("💡 Install with: pip install diffusers transformers accelerate")
        print("🔄 Falling back to OpenCV inpainting")
        return inpaint_with_opencv(image_path, mask_path, output_path)

    preference = (device_preference or "auto").lower()
    if preference not in {"auto", "cuda", "cpu"}:
        print(f"⚠️ Unknown device option '{device_preference}' -> using auto")
        preference = "auto"

    if preference == "auto":
        device = "cuda" if torch.cuda.is_available() else "cpu"
    elif preference == "cuda":
        if not torch.cuda.is_available():
            raise RuntimeError(
                "CUDA device was requested but torch.cuda.is_available() is False. "
                "Install the CUDA build of PyTorch or run from a GPU-enabled environment."
            )
        device = "cuda"
    else:
        device = preference

    torch_dtype = torch.float16 if device == "cuda" else torch.float32
    seed_value = seed if seed is not None else random.randrange(0, 2**31)
    print(f"\n🤖 AI Inpainting start (device={device}, seed={seed_value})")

    img = cv2.imread(image_path, cv2.IMREAD_COLOR)
    if img is None:
        raise ValueError(f"Failed to read image: {image_path}")

    if mask_path and os.path.exists(mask_path):
        mask = cv2.imread(mask_path, cv2.IMREAD_GRAYSCALE)
    else:
        mask = detect_anomalous_colors_lab(img)

    if mask is None:
        raise ValueError("Mask generation failed")
    if np.sum(mask) == 0:
        print("⚠️ No regions detected for inpainting; returning original image")
        if output_path:
            cv2.imwrite(output_path, img)
            return output_path
        return image_path

    ref_img_bgr = None
    if reference_image_path and os.path.exists(reference_image_path):
        ref_img_bgr = cv2.imread(reference_image_path, cv2.IMREAD_COLOR)
        if ref_img_bgr is None or ref_img_bgr.size == 0:
            print(f"⚠️ Reference texture is empty: {reference_image_path}")
            ref_img_bgr = None

    color_matched_path = None
    if ref_img_bgr is not None:
        preserved = preserve_hi_color_bgr(img, ref_img_bgr, alpha=0.7)
        if preserved is not None:
            img = preserved
            base_name = os.path.splitext(image_path)[0]
            color_matched_path = base_name + "_hi_preserved.png"
            try:
                cv2.imwrite(color_matched_path, img)
                print(f"🎯 Color preserved input saved → {os.path.basename(color_matched_path)}")
            except Exception as save_err:
                print(f"⚠️ Failed to save color preserved image: {save_err}")

    reference_image = None
    if ref_img_bgr is not None:
        reference_image = PILImage.fromarray(cv2.cvtColor(ref_img_bgr, cv2.COLOR_BGR2RGB))
        print(f"✅ Loaded reference texture: {os.path.basename(reference_image_path)} ({reference_image.width}x{reference_image.height})")

    untouched_bgr = img.copy()
    pil_image = PILImage.fromarray(cv2.cvtColor(img, cv2.COLOR_BGR2RGB))
    mask_pil = PILImage.fromarray(mask).convert("L")
    original_size = pil_image.size

    if reference_image is None:
        reference_image = pil_image.copy()

    resize_target = max(pil_image.size)
    resize_cap = max_side or 0
    if resize_cap and resize_target > resize_cap:
        scale = resize_cap / resize_target
        new_size = (int(pil_image.size[0] * scale), int(pil_image.size[1] * scale))
        pil_image = pil_image.resize(new_size, PILImage.LANCZOS)
        mask_pil = mask_pil.resize(new_size, PILImage.LANCZOS)
        reference_image = reference_image.resize(new_size, PILImage.LANCZOS)
        print(f"📐 Resized input from {original_size} to {new_size}")

    def get_pipeline(controlnet_enabled: bool):
        key = ("controlnet" if controlnet_enabled else "plain", device)
        target_device = torch.device("cuda") if device == "cuda" else torch.device("cpu")

        def prepare_pipeline(pipe_obj):
            if device == "cuda":
                torch.cuda.set_device(target_device.index or 0)
                torch.backends.cuda.matmul.allow_tf32 = True
                torch.backends.cudnn.benchmark = True
            pipe_obj = pipe_obj.to(target_device)
            module_specs = [
                ("unet", torch_dtype),
                ("vae", torch_dtype),
                ("text_encoder", torch.float32),
                ("controlnet", torch_dtype),
                ("image_encoder", torch_dtype),
            ]
            for name, module_dtype in module_specs:
                module = getattr(pipe_obj, name, None)
                if module is None:
                    continue
                module.to(device=target_device, dtype=module_dtype, non_blocking=True)
                try:
                    mod_device = next(module.parameters()).device
                except StopIteration:
                    mod_device = target_device
                if mod_device.type != target_device.type:
                    raise RuntimeError(f"{name} failed to move to {target_device} (currently {mod_device})")
            pipe_obj.scheduler = DPMSolverMultistepScheduler.from_config(pipe_obj.scheduler.config)
            if hasattr(pipe_obj, "enable_xformers_memory_efficient_attention"):
                try:
                    pipe_obj.enable_xformers_memory_efficient_attention()
                except Exception:
                    pass
            pipe_obj.enable_attention_slicing()
            pipe_obj.enable_vae_tiling()
            if hasattr(pipe_obj, "set_progress_bar_config"):
                pipe_obj.set_progress_bar_config(disable=False, position=0, leave=False)
            return pipe_obj

        pipe = PIPELINE_CACHE.get(key)
        if pipe is not None:
            return prepare_pipeline(pipe)

        try:
            if controlnet_enabled:
                ctrl_key = (device, "controlnet")
                controlnet = CONTROLNET_CACHE.get(ctrl_key)
                if controlnet is None:
                    controlnet = ControlNetModel.from_pretrained(
                        "lllyasviel/control_v11f1e_sd15_tile",
                        torch_dtype=torch_dtype,
                    )
                    CONTROLNET_CACHE[ctrl_key] = controlnet
                pipe = StableDiffusionControlNetInpaintPipeline.from_pretrained(
                    "runwayml/stable-diffusion-inpainting",
                    controlnet=controlnet,
                    torch_dtype=torch_dtype,
                    safety_checker=None,
                )
            else:
                pipe = StableDiffusionInpaintPipeline.from_pretrained(
                    "runwayml/stable-diffusion-inpainting",
                    torch_dtype=torch_dtype,
                    safety_checker=None,
                )
        except Exception as exc:
            if controlnet_enabled:
                print(f"⚠️ Failed to load ControlNet pipeline: {exc}. Using base inpaint instead.")
                return get_pipeline(False)
            raise RuntimeError(f"Failed to load diffusion pipeline: {exc}") from exc

        pipe = prepare_pipeline(pipe)
        PIPELINE_CACHE[key] = pipe
        return pipe
    try:
        pipe = get_pipeline(use_controlnet)
    except Exception as exc:
        print(f"⚠️ {exc}")
        print("🔄 Falling back to OpenCV inpainting")
        return inpaint_with_opencv(image_path, mask_path, output_path)

    prompt = "high quality texture restoration with locally coherent shading, seamless color continuity, detailed surface texture"
    avg_color = np.mean(img.reshape(-1, 3), axis=0)
    prompt += f", preserve original colors ({avg_color[2]:.0f},{avg_color[1]:.0f},{avg_color[0]:.0f})"
    if reference_image is not None and reference_image_path:
        prompt += f", match tone and lighting of {os.path.basename(reference_image_path)}"
    if 'color_matched_path' in locals() and color_matched_path:
        prompt += ", blend smoothly with surrounding matched colors"
    negative_prompt = "blurry, low quality, distorted texture, artifacts, text, watermark"

    control_image = reference_image

    try:
        step_start = time.perf_counter()
        print(
            f"🚀 Launch diffusion | steps={num_inference_steps} | "
            f"guidance={guidance_scale:.2f} | controlnet={'on' if use_controlnet else 'off'}"
        )
        generator = torch.Generator(device=device).manual_seed(int(seed_value))
        image_input = pil_image
        mask_input = mask_pil
        control_input = control_image
        autocast_ctx = autocast("cuda") if device == "cuda" else contextlib.nullcontext()
        with autocast_ctx:
            if use_controlnet:
                result = pipe(
                    prompt=prompt,
                    negative_prompt=negative_prompt,
                    image=image_input,
                    mask_image=mask_input,
                    control_image=control_input,
                    controlnet_conditioning_scale=controlnet_scale,
                    guidance_scale=guidance_scale,
                    num_inference_steps=num_inference_steps,
                    generator=generator,
                )
            else:
                result = pipe(
                    prompt=prompt,
                    negative_prompt=negative_prompt,
                    image=image_input,
                    mask_image=mask_input,
                    guidance_scale=guidance_scale,
                    num_inference_steps=num_inference_steps,
                    generator=generator,
                )
        if device == "cuda":
            torch.cuda.synchronize()
            allocated = torch.cuda.memory_allocated() / (1024**2)
            reserved = torch.cuda.memory_reserved() / (1024**2)
            print(f"📊 GPU memory | allocated={allocated:.1f}MB | reserved={reserved:.1f}MB")
        elapsed = time.perf_counter() - step_start
        print(f"✅ Diffusion finished in {elapsed:.1f}s")
    except Exception as exc:
        print(f"⚠️ Diffusion inference failed: {exc}")
        print("🔄 Falling back to OpenCV inpainting")
        return inpaint_with_opencv(image_path, mask_path, output_path)

    result_image = result.images[0]
    result_bgr = cv2.cvtColor(np.array(result_image), cv2.COLOR_RGB2BGR)
    if result_bgr.shape[1] != original_size[0] or result_bgr.shape[0] != original_size[1]:
        result_bgr = cv2.resize(result_bgr, original_size, interpolation=cv2.INTER_CUBIC)

    try:
        mask_uint8 = None
        blend_mask = cv2.resize(mask, (original_size[0], original_size[1]), interpolation=cv2.INTER_NEAREST)
        if blend_mask.ndim == 2:
            mask_uint8 = blend_mask.copy()
            blend_mask = blend_mask[:, :, None]
        else:
            mask_uint8 = cv2.cvtColor(blend_mask, cv2.COLOR_BGR2GRAY)
        mask_uint8 = mask_uint8.astype(np.uint8)
        blend_mask = blend_mask.astype(np.float32) / 255.0
        blend_mask = np.repeat(blend_mask, 3, axis=2)
        result_bgr = (
            result_bgr.astype(np.float32) * blend_mask
            + untouched_bgr.astype(np.float32) * (1.0 - blend_mask)
        )
        result_bgr = np.clip(result_bgr, 0, 255).astype(np.uint8)
    except Exception as blend_err:
        print(f"⚠️ Failed to blend masked result with original: {blend_err}")

    try:
        residual_threshold = 35
        residual_mask = cv2.inRange(
            result_bgr,
            (0, 0, 0),
            (residual_threshold, residual_threshold, residual_threshold),
        )
        if mask_uint8 is not None:
            residual_mask = cv2.bitwise_and(residual_mask, mask_uint8)
        residual_count = cv2.countNonZero(residual_mask)
        if residual_count > 0:
            print(f"🧼 Cleaning {residual_count} residual dark pixels via OpenCV inpaint")
            residual_mask = cv2.dilate(residual_mask, np.ones((5, 5), np.uint8), iterations=1)
            cleaned = cv2.inpaint(result_bgr, residual_mask, 5, cv2.INPAINT_TELEA)
            result_bgr = cleaned
    except Exception as cleanup_err:
        print(f"⚠️ Residual cleanup failed: {cleanup_err}")

    output_path = output_path or os.path.splitext(image_path)[0] + "_ai_inpainted.png"
    cv2.imwrite(output_path, result_bgr)
    print(f"✅ AI inpainting complete -> {output_path}")
    return output_path
def process_baked_textures(
    baked_dir,
    maps="albedo,ao,normal",
    use_ai=False,
    method='ns',
    use_controlnet=True,
    original_texture_dir=None,
    device_preference=None,
    use_hi_reference=True,
    guidance_scale=7.5,
    num_inference_steps=20,
    max_side=1024,
    controlnet_scale=1.0,
    seed=None,
):
    """
    Bake??모든 ?스처에 Inpainting ?용 (?본 ?스?참고!)
    
    Args:
        baked_dir: Bake???스처? ?는 ?렉?리
        maps: 처리???목록 (?표?구분)
        use_ai: AI 모델 ?용 ??
        method: Inpainting 방법 ('ns' 권장)
        use_controlnet: ControlNet Tile ?용 ?? (AI 모드?서?
        original_texture_dir: ?본 ?스??렉?리 (OBJ/MTL/PNG ?일 ?치, None?면 ?동 ?색)
        device_preference: ?바?스 지???션 ('auto', 'cuda', 'cpu')
    """
    processed = []
    
    # ???본 ?스??렉?리 ?동 ?색 (HI mesh ?일 경로?서)
    if original_texture_dir is None:
        # baked_dir???위 ?렉?리?서 ?본 ?스?찾기
        parent_dir = os.path.dirname(baked_dir)
        # 가?한 ?본 ?스?경로??
        possible_dirs = [
            parent_dir,
            os.path.join(parent_dir, "datasets", "obj"),
            os.path.dirname(parent_dir),
        ]
        for possible_dir in possible_dirs:
            if os.path.exists(possible_dir):
                # PNG ?일???는지 ?인
                png_files = [f for f in os.listdir(possible_dir) if f.lower().endswith('.png')]
                if png_files:
                    original_texture_dir = possible_dir
                    print(f"???본 ?스??렉?리 ?동 ?색: {original_texture_dir}")
                    break
    
    for map_name in maps.split(","):
        map_name = map_name.strip()
        tex_path = os.path.join(baked_dir, f"{map_name}.png")
        
        if not os.path.exists(tex_path):
            print(f"?️ {tex_path} ?음 ??{map_name} ?킵")
            continue
        
        # ???본 ?스?경로 찾기 (MTL ?일 ?싱 ?는 ?일?매칭)
        reference_image_path = None
        if original_texture_dir and os.path.exists(original_texture_dir):
            # 1. MTL ?일?서 ?스?경로 추출 ?도
            mtl_files = [f for f in os.listdir(original_texture_dir) if f.lower().endswith('.mtl')]
            if mtl_files:
                mtl_path = os.path.join(original_texture_dir, mtl_files[0])
                try:
                    with open(mtl_path, 'r', encoding='utf-8', errors='ignore') as f:
                        mtl_content = f.read()
                        # MTL ?일?서 map_Kd, map_Ks, map_Ns ?의 ?스?경로 찾기
                        # albedo??보통 map_Kd (diffuse texture)
                        if map_name == 'albedo':
                            # map_Kd 찾기 (?러 ??을 ???음)
                            map_kd_textures = []
                            for line in mtl_content.split('\n'):
                                line = line.strip()
                                if line.startswith('map_Kd'):
                                    # map_Kd texture.png ?태???됨
                                    texture_name = line.split()[-1] if len(line.split()) > 1 else None
                                    if texture_name:
                                        ref_path = os.path.join(original_texture_dir, texture_name)
                                        if os.path.exists(ref_path):
                                            map_kd_textures.append(ref_path)
                            
                            # ?러 ?스처? ?으?가?????일 ?택 (메인 ?스?
                            if map_kd_textures:
                                if len(map_kd_textures) == 1:
                                    reference_image_path = map_kd_textures[0]
                                else:
                                    # ?일 ?기??렬?여 가?????일 ?택
                                    textures_with_size = []
                                    for tex_path in map_kd_textures:
                                        try:
                                            size = os.path.getsize(tex_path)
                                            textures_with_size.append((tex_path, size))
                                        except:
                                            pass
                                    if textures_with_size:
                                        textures_with_size.sort(key=lambda x: x[1], reverse=True)
                                        reference_image_path = textures_with_size[0][0]
                                
                                if reference_image_path:
                                    print(f"   ??MTL?서 ?스?발견: {os.path.basename(reference_image_path)} ({len(map_kd_textures)}??스???택)")
                except Exception as e:
                    print(f"   ?️ MTL ?일 ?싱 ?패: {e}")
            
            # 2. MTL?서 찾? 못했?면 직접 ?일?매칭 ?도
            if not reference_image_path:
                # ?러 가?한 ?름 ?턴
                possible_names = [
                    f"{map_name}.png",
                    f"{map_name}.jpg",
                    "texture.png",
                    "texture.jpg",
                    "diffuse.png",
                    "diffuse.jpg",
                ]
                for name in possible_names:
                    ref_path = os.path.join(original_texture_dir, name)
                    if os.path.exists(ref_path):
                        reference_image_path = ref_path
                        break
                
                # 3. ?전???찾았?면 ?렉?리??모든 PNG ?일 ?가?????일 ?용 (albedo??경우)
                if not reference_image_path and map_name == 'albedo':
                    png_files = [f for f in os.listdir(original_texture_dir) if f.lower().endswith(('.png', '.jpg'))]
                    if png_files:
                        # ?일 ?기??렬 (가?????일??보통 메인 ?스?
                        png_files_with_size = []
                        for png_file in png_files:
                            png_path = os.path.join(original_texture_dir, png_file)
                            try:
                                size = os.path.getsize(png_path)
                                png_files_with_size.append((png_path, size))
                            except:
                                pass
                        if png_files_with_size:
                            # 가?????일 ?택
                            png_files_with_size.sort(key=lambda x: x[1], reverse=True)
                            reference_image_path = png_files_with_size[0][0]
                            print(f"   ??가?????스??일 ?용: {os.path.basename(reference_image_path)}")
        
        hi_reference_copy = None
        if use_hi_reference and reference_image_path and os.path.exists(reference_image_path):
            hi_reference_copy = os.path.join(baked_dir, f"{map_name}_hi_reference.png")
            if not os.path.exists(hi_reference_copy):
                try:
                    shutil.copy(reference_image_path, hi_reference_copy)
                except Exception as copy_err:
                    print(f"⚠️ Failed to cache HI reference: {copy_err}")

        inpaint_input_path = tex_path
        if map_name == "albedo" and use_hi_reference:
            ref_for_seed = None
            if hi_reference_copy and os.path.exists(hi_reference_copy):
                ref_for_seed = hi_reference_copy
            elif reference_image_path and os.path.exists(reference_image_path):
                ref_for_seed = reference_image_path

            if ref_for_seed:
                blended_candidate = blend_hi_color_preserve(tex_path, ref_for_seed, alpha=0.35)
                if blended_candidate and os.path.exists(blended_candidate):
                    inpaint_input_path = blended_candidate
                    print(f"   ?? HI color seed applied: {os.path.basename(blended_candidate)}")

        if use_ai:
            print(f"? AI Inpainting 처리 ? {map_name} (Stable Diffusion + ControlNet Tile)")
            if reference_image_path:
                print(f"   ?본 ?스?참고: {reference_image_path}")
            result_path = inpaint_with_ai(
                inpaint_input_path,
                use_controlnet=use_controlnet,
                reference_image_path=reference_image_path,
                device_preference=device_preference,
                guidance_scale=guidance_scale,
                num_inference_steps=num_inference_steps,
                max_side=max_side,
                controlnet_scale=controlnet_scale,
                seed=seed,
            )
        else:
            print(f"? Inpainting 처리 ? {map_name} (LAB ?계 기반 + NS 방법)")
            # ??LAB ?계 기반 마스??+ NS Inpainting ?용
            result_path = inpaint_with_opencv(inpaint_input_path, method=method, use_lab_detection=True)
        
        # ?본 백업 ?결과?교체 (Windows ?환)
        backup_path = tex_path.replace(".png", "_original.png")

        if os.path.exists(tex_path):
            if os.path.exists(backup_path):
                os.remove(backup_path)
            shutil.move(tex_path, backup_path)

        if not os.path.exists(result_path):
            print(f"[warn] Inpainted result missing: {result_path}")
            if os.path.exists(backup_path):
                shutil.copy(backup_path, tex_path)
                print(f"[warn] Restored original map because inpaint output was missing -> {os.path.basename(tex_path)}")
        else:
            if os.path.abspath(result_path) == os.path.abspath(tex_path):
                print(f"[info] Result already at target: {tex_path}")
            else:
                target_dir = os.path.dirname(tex_path)
                if target_dir and not os.path.exists(target_dir):
                    os.makedirs(target_dir, exist_ok=True)
                if os.path.exists(tex_path):
                    os.remove(tex_path)
                shutil.move(result_path, tex_path)

       
        processed.append(map_name)
    
    print(f"??{len(processed)}??스?Inpainting ?료")
    return processed


def main():
    parser = argparse.ArgumentParser(description='Texture Inpainting post process')
    parser.add_argument('--input', help='Input image file or baked directory')
    parser.add_argument('--mask', help='Optional mask image')
    parser.add_argument('--output', help='Output image path')
    parser.add_argument('--maps', default='albedo,ao,normal', help='Comma separated map list')
    parser.add_argument('--method', choices=['telea', 'ns'], default='ns', help='OpenCV inpaint method')
    parser.add_argument('--use-ai', action='store_true', help='Use Stable Diffusion based AI inpainting')
    parser.add_argument('--use-controlnet', action='store_true', default=True, help='Enable ControlNet Tile when using AI')
    parser.add_argument('--no-controlnet', dest='use_controlnet', action='store_false', help='Disable ControlNet Tile')
    parser.add_argument('--guidance-scale', type=float, default=7.5, help='Guidance scale for AI mode')
    parser.add_argument('--inference-steps', type=int, default=20, help='Inference steps for AI mode')
    parser.add_argument('--max-side', type=int, default=1024, help='Clamp the diffusion input so the longest side is at most this size')
    parser.add_argument('--controlnet-scale', type=float, default=1.0, help='Strength for ControlNet conditioning (0~2 recommended)')
    parser.add_argument('--preset', choices=sorted(PRESET_CONFIGS.keys()), help='Load predefined option bundle')
    parser.add_argument('--texture-root', help='Base dir containing extracted_textures/inpaint_masks/inpaint_results')
    parser.add_argument('--texture-file', help='Texture filename inside extracted_textures (e.g., 00_Image_3 or 00_Image_3.png)')
    parser.add_argument('--seed', type=int, help='Manual diffusion seed (omit for random)')
    parser.add_argument('--original-texture-dir', type=str, default=None, help='Directory containing original textures')
    parser.add_argument('--device', choices=['auto', 'cuda', 'cpu'], default='auto', help='Execution device for AI mode')
    parser.add_argument('--use-hi-reference', action='store_true', default=True, help='Leverage HI reference textures for seeding/blending')
    parser.add_argument('--no-hi-reference', dest='use_hi_reference', action='store_false', help='Disable HI reference usage')
    raw_cli_args = sys.argv[1:]
    args = parser.parse_args()
    cli_spec_use_controlnet = ("--use-controlnet" in raw_cli_args) or ("--no-controlnet" in raw_cli_args)

    if args.preset:
        preset = PRESET_CONFIGS[args.preset]
        args.use_ai = preset.get("use_ai", args.use_ai)
        if not cli_spec_use_controlnet:
            args.use_controlnet = preset.get("use_controlnet", args.use_controlnet)
        args.device = preset.get("device", args.device)
        args.guidance_scale = preset.get("guidance_scale", args.guidance_scale)
        args.inference_steps = preset.get("inference_steps", args.inference_steps)
        args.max_side = preset.get("max_side", args.max_side)
        args.controlnet_scale = preset.get("controlnet_scale", args.controlnet_scale)
        args.seed = preset.get("seed", args.seed)

    if args.texture_root and args.texture_file:
        root = os.path.abspath(args.texture_root)
        tex_name = args.texture_file
        if not os.path.splitext(tex_name)[1]:
            tex_name = f"{tex_name}.png"
        input_candidate = os.path.join(root, "extracted_textures", tex_name)
        if not os.path.exists(input_candidate):
            raise FileNotFoundError(f"Auto input not found: {input_candidate}")
        if not args.input:
            args.input = input_candidate
        if not args.mask:
            mask_name = os.path.splitext(tex_name)[0] + "_mask.png"
            mask_candidate = os.path.join(root, "inpaint_masks", mask_name)
            if os.path.exists(mask_candidate):
                args.mask = mask_candidate
        if not args.output:
            os.makedirs(os.path.join(root, "inpaint_results"), exist_ok=True)
            out_name = os.path.splitext(tex_name)[0] + "_inpaint.png"
            args.output = os.path.join(root, "inpaint_results", out_name)

    if not args.input:
        parser.error("--input is required (or provide --texture-root with --texture-file)")

    input_path = os.path.abspath(args.input)

    if os.path.isdir(input_path):
        process_baked_textures(
            input_path,
            args.maps,
            args.use_ai,
            args.method,
            args.use_controlnet,
            args.original_texture_dir,
            device_preference=args.device,
            use_hi_reference=args.use_hi_reference,
            guidance_scale=args.guidance_scale,
            num_inference_steps=args.inference_steps,
            max_side=args.max_side,
            controlnet_scale=args.controlnet_scale,
            seed=args.seed,
        )
    else:
        reference_image_path = None
        if args.original_texture_dir and os.path.exists(args.original_texture_dir):
            input_name = os.path.splitext(os.path.basename(input_path))[0]
            candidates = [
                os.path.join(args.original_texture_dir, f"{input_name}.png"),
                os.path.join(args.original_texture_dir, f"{input_name}.jpg"),
                os.path.join(args.original_texture_dir, 'texture.png'),
            ]
            for candidate in candidates:
                if os.path.exists(candidate):
                    reference_image_path = candidate
                    break

        if args.use_ai:
            inpaint_with_ai(
                input_path,
                args.mask,
                args.output,
                args.use_controlnet,
                args.guidance_scale,
                args.inference_steps,
                reference_image_path,
                device_preference=args.device,
                max_side=args.max_side,
                controlnet_scale=args.controlnet_scale,
                seed=args.seed,
            )
        else:
            inpaint_with_opencv(
                input_path,
                args.mask,
                args.output,
                args.method,
                use_lab_detection=True,
            )


if __name__ == '__main__':
    main()
