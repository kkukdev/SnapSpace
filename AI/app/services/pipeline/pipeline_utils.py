import hashlib
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


