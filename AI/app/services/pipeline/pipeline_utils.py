import hashlib
import shutil
import unicodedata
from pathlib import Path
from typing import Iterable, Optional


def sanitize_filename(name: str) -> str:
    normalized = unicodedata.normalize("NFKD", name or "")
    ascii_name = normalized.encode("ascii", "ignore").decode("ascii")
    safe = "".join(c if c.isalnum() or c in ("-", "_") else "_" for c in ascii_name)
    safe = safe.strip("_")
    if safe:
        return safe
    digest = hashlib.sha1((name or "").encode("utf-8")).hexdigest()[:16]
    return f"file_{digest}"


def ensure_directories(dirs: Iterable[Path]) -> None:
    for directory in dirs:
        directory.mkdir(parents=True, exist_ok=True)


def ensure_utf8_copy(
    source: Path,
    target_dir: Path,
    *,
    logger,
    prefix: str,
    decode_errors: str = "ignore",
) -> Path:
    """
    텍스트 기반 메쉬 파일(예: OBJ)이 UTF-8로 읽히지 않을 경우, UTF-8로 재인코딩한 사본을 생성하여 반환합니다.

    Args:
        source: 원본 파일 경로
        target_dir: 사본을 저장할 디렉토리
        logger: 로그 출력을 위한 logger
        prefix: 로그 프리픽스 문자열
        decode_errors: 디코딩 실패 시 처리 옵션 (기본값: ignore)

    Returns:
        UTF-8로 안전하게 읽을 수 있는 파일 경로 (원본 또는 사본)
    """
    suffix = source.suffix.lower()
    if suffix not in {".obj", ".mtl"}:
        return source

    try:
        source.read_text(encoding="utf-8")
        return source
    except UnicodeDecodeError:
        prefix = prefix.strip()
        target_dir.mkdir(parents=True, exist_ok=True)
        sanitized_path = target_dir / f"{source.stem}_utf8{source.suffix}"

        raw_bytes: Optional[bytes] = None
        try:
            raw_bytes = source.read_bytes()
            decoded = raw_bytes.decode("utf-8", errors=decode_errors)
            sanitized_path.write_text(decoded, encoding="utf-8")
            logger.info(f"{prefix} UTF-8 변환된 사본 생성: {sanitized_path}")
            return sanitized_path
        except Exception as exc:
            logger.warning(f"{prefix} UTF-8 사본 생성 실패: {exc}")
            raise


def sanitize_material_assets(
    obj_path: Path,
    original_obj_dir: Path,
    sanitized_dir: Path,
    *,
    logger,
    prefix: str,
) -> Path:
    """
    OBJ가 참조하는 MTL 및 텍스처 파일을 로컬 작업 디렉토리로 복사하고,
    map_Ka를 map_Kd로 변환하여 Blender에서 정상적으로 인식하도록 수정합니다.
    """
    prefix = prefix.strip()
    sanitized_dir.mkdir(parents=True, exist_ok=True)

    try:
        obj_text = obj_path.read_text(encoding="utf-8")
    except Exception:
        obj_text = obj_path.read_text(encoding="utf-8", errors="ignore")

    mtllibs = []
    new_lines = []
    for line in obj_text.splitlines():
        stripped = line.strip()
        if stripped.lower().startswith("mtllib"):
            parts = stripped.split(None, 1)
            if len(parts) == 2:
                original_ref = parts[1].strip()
                mtl_name = Path(original_ref).name
                mtllibs.append((original_ref, mtl_name))
                new_lines.append(f"mtllib {mtl_name}")
            else:
                new_lines.append(line)
        else:
            new_lines.append(line)

    if new_lines:
        trailing_newline = "\n" if obj_text.endswith("\n") else ""
        obj_path.write_text("\n".join(new_lines) + trailing_newline, encoding="utf-8")

    for original_ref, mtl_name in mtllibs:
        source_candidates = []
        ref_path = Path(original_ref)
        if ref_path.is_absolute():
            source_candidates.append(ref_path)
        else:
            source_candidates.append(original_obj_dir / ref_path)
            source_candidates.append(original_obj_dir / mtl_name)
            source_candidates.append(obj_path.parent / ref_path)
            source_candidates.append(obj_path.parent / mtl_name)

        source_mtl = next((path for path in source_candidates if path.exists()), None)
        if not source_mtl:
            logger.warning(f"{prefix} MTL 파일을 찾을 수 없습니다: {original_ref}")
            continue

        dest_mtl = sanitized_dir / mtl_name
        dest_mtl.parent.mkdir(parents=True, exist_ok=True)

        try:
            mtl_text = source_mtl.read_text(encoding="utf-8")
        except Exception:
            mtl_text = source_mtl.read_text(encoding="utf-8", errors="ignore")

        modified_lines = []
        texture_files = []
        for line in mtl_text.splitlines():
            stripped = line.strip()
            if stripped.lower().startswith("map_"):
                tokens = stripped.split()
                if len(tokens) >= 2:
                    map_token = tokens[0]
                    options = tokens[1:-1] if len(tokens) > 2 else []
                    file_token = tokens[-1]

                    if map_token.lower() == "map_ka":
                        map_token = "map_Kd"

                    texture_files.append(file_token)
                    file_name = Path(file_token).name
                    if options:
                        new_line = " ".join([map_token, *options, file_name])
                    else:
                        new_line = f"{map_token} {file_name}"
                    modified_lines.append(new_line)
                else:
                    modified_lines.append(line)
            else:
                modified_lines.append(line)

        trailing_newline = "\n" if mtl_text.endswith("\n") else ""
        dest_mtl.write_text("\n".join(modified_lines) + trailing_newline, encoding="utf-8")
        logger.info(f"{prefix} MTL 사본 생성 및 보정: {dest_mtl}")

        for texture_ref in texture_files:
            tex_path = Path(texture_ref)
            tex_candidates = []
            if tex_path.is_absolute():
                tex_candidates.append(tex_path)
            else:
                tex_candidates.append(source_mtl.parent / tex_path)
                tex_candidates.append(original_obj_dir / tex_path)

            dest_texture = sanitized_dir / tex_path.name
            for candidate in tex_candidates:
                if candidate.exists():
                    if candidate.resolve() == dest_texture.resolve():
                        break
                    try:
                        shutil.copy2(candidate, dest_texture)
                        logger.info(f"{prefix} 텍스처 복사: {candidate} -> {dest_texture}")
                    except Exception as exc:
                        logger.warning(f"{prefix} 텍스처 복사 실패: {candidate} -> {dest_texture} ({exc})")
                    break
            else:
                logger.warning(f"{prefix} 텍스처 파일을 찾을 수 없습니다: {texture_ref}")

    return obj_path


