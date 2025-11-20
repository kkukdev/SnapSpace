from dataclasses import dataclass
from pathlib import Path
from typing import Optional

from .pipeline_utils import ensure_directories


@dataclass(frozen=True)
class PipelineDirectories:
    temp_dir: Path
    optimized_dir: Path
    final_dir: Path
    texture_dir: Path
    temp_dir_str: Optional[str] = None
    optimized_dir_str: Optional[str] = None
    final_dir_str: Optional[str] = None
    texture_dir_str: Optional[str] = None
    is_network_path: bool = False


@dataclass(frozen=True)
class PipelineContext:
    uploads_path: Path
    group_name: str
    folder_name: str
    directories: PipelineDirectories


def resolve_pipeline_paths(
    uploads_input_path: str,
    outputs_base_dir: str,
    group_name: str,
    folder_name: str,
) -> PipelineContext:
    """
    파이프라인 단계에서 사용할 디렉터리들을 계산합니다.

    네트워크 경로와 로컬 경로를 모두 지원하며, 최종적으로 temp/optimized/final/texture 경로를 제공합니다.
    """
    uploads_path = Path(uploads_input_path)
    outputs_root_str = str(outputs_base_dir)

    is_unc_path = outputs_root_str.startswith("\\\\") or outputs_root_str.startswith("//")

    import re

    mounted_pattern = re.compile(r'^[A-Z]:\\([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}|[A-Za-z0-9_-]+)\\')
    is_mounted_network = bool(mounted_pattern.match(outputs_root_str))
    is_network_path = is_unc_path or is_mounted_network

    if is_network_path:
        normalized = outputs_root_str.rstrip("\\/")

        if normalized.lower().endswith("\\outputs"):
            normalized = normalized[:-8]
        elif normalized.lower().endswith("/outputs"):
            normalized = normalized[:-8]
        elif normalized.lower().endswith("outputs"):
            normalized = normalized[:-7]

        normalized = normalized.rstrip("\\/")

        while "\\storage\\storage" in normalized:
            normalized = normalized.replace("\\storage\\storage", "\\storage")
        while "/storage/storage" in normalized:
            normalized = normalized.replace("/storage/storage", "/storage")
        while "\\storage/storage" in normalized:
            normalized = normalized.replace("\\storage/storage", "\\storage")
        while "/storage\\storage" in normalized:
            normalized = normalized.replace("/storage\\storage", "/storage")

        sep = "\\" if "\\" in normalized else "/"
        temp_dir_str = f"{normalized}{sep}temp"
        optimized_dir_str = f"{normalized}{sep}outputs{sep}optimized{sep}{group_name}{sep}{folder_name}"
        final_dir_str = f"{normalized}{sep}outputs{sep}final{sep}{group_name}{sep}{folder_name}"
        texture_dir_str = f"{final_dir_str}{sep}texture"

        temp_dir = Path(temp_dir_str)
        optimized_dir = Path(optimized_dir_str)
        final_dir = Path(final_dir_str)
        texture_dir = Path(texture_dir_str)

        directories = PipelineDirectories(
            temp_dir=temp_dir,
            optimized_dir=optimized_dir,
            final_dir=final_dir,
            texture_dir=texture_dir,
            temp_dir_str=temp_dir_str,
            optimized_dir_str=optimized_dir_str,
            final_dir_str=final_dir_str,
            texture_dir_str=texture_dir_str,
            is_network_path=True,
        )
    else:
        outputs_root = Path(outputs_base_dir)
        temp_dir = outputs_root.parent / "temp"
        optimized_dir = outputs_root / "optimized" / group_name / folder_name
        final_dir = outputs_root / "final" / group_name / folder_name
        texture_dir = final_dir / "texture"

        directories = PipelineDirectories(
            temp_dir=temp_dir,
            optimized_dir=optimized_dir,
            final_dir=final_dir,
            texture_dir=texture_dir,
            is_network_path=False,
        )

    # texture_dir 생성 비활성화 (현재 필요 없음)
    # ensure_directories((texture_dir, optimized_dir, final_dir, temp_dir))
    ensure_directories((optimized_dir, final_dir, temp_dir))

    return PipelineContext(
        uploads_path=uploads_path,
        group_name=group_name,
        folder_name=folder_name,
        directories=directories,
    )

