"""
경로 처리 유틸리티 함수
- Windows 네트워크 공유 폴더 경로 처리
- Docker 경로와 로컬 경로 변환
"""
import os
import logging

logger = logging.getLogger(__name__)


def normalize_network_path(base_path: str, relative_path: str = "") -> str:
    """
    네트워크 공유 폴더 경로를 정규화합니다.
    
    Args:
        base_path: 기본 경로 (예: "\\\\70.12.246.48\\storage")
        relative_path: 상대 경로 (예: "uploads/file.obj")
        
    Returns:
        정규화된 전체 경로
    """
    # UNC 경로 정규화 (백슬래시 통일)
    base = base_path.replace('/', '\\')
    
    # UNC 경로 형식 확인
    if base.startswith('\\\\'):
        # UNC 경로인 경우
        if relative_path:
            # 상대 경로가 있으면 결합
            relative = relative_path.replace('/', '\\').lstrip('\\')
            full_path = f"{base}\\{relative}" if not base.endswith('\\') else f"{base}{relative}"
        else:
            full_path = base
    else:
        # 일반 경로인 경우
        if relative_path:
            full_path = os.path.join(base, relative_path)
        else:
            full_path = base
    
    return full_path


def convert_to_network_path(file_path: str, network_base: str) -> str:
    """
    파일 경로를 네트워크 공유 폴더 경로로 변환합니다.
    
    Args:
        file_path: 변환할 파일 경로 (Docker 경로 또는 로컬 경로)
        network_base: 네트워크 공유 폴더 기본 경로 (예: "\\\\70.12.246.48\\storage")
        
    Returns:
        네트워크 경로로 변환된 경로
    """
    # 이미 존재하는 경로면 그대로 반환
    if os.path.exists(file_path):
        # 네트워크 경로인지 확인
        if file_path.startswith('\\\\') or file_path.startswith('//'):
            return file_path
        # 파일이 존재하면 네트워크 경로로 변환 시도
        pass
    
    # Docker 내부 경로 패턴들
    docker_patterns = [
        "/storage/",           # Docker: /storage/uploads/...
        "/project_root/storage/",  # Docker: /project_root/storage/uploads/...
    ]
    
    # Docker 경로 패턴 확인 및 변환
    for pattern in docker_patterns:
        if file_path.startswith(pattern):
            # Docker 경로에서 storage 이후 부분 추출
            relative_path = file_path.replace(pattern, "").replace("/", "\\")
            network_path = normalize_network_path(network_base, relative_path)
            logger.info(f"경로 변환 (Docker → Network): {file_path} -> {network_path}")
            return network_path
    
    # 로컬 프로젝트 루트 경로 패턴 (Windows)
    # 예: C:\Users\...\final-pjt\S13P31S102\storage\...
    project_storage_patterns = [
        "\\storage\\",
        "/storage/",
    ]
    
    for pattern in project_storage_patterns:
        if pattern in file_path:
            # 프로젝트 루트 기준 storage 이후 부분 추출
            parts = file_path.split(pattern, 1)
            if len(parts) > 1:
                relative_path = parts[1].replace("/", "\\")
                network_path = normalize_network_path(network_base, relative_path)
                logger.info(f"경로 변환 (Local → Network): {file_path} -> {network_path}")
                return network_path
    
    # 상대 경로인 경우 네트워크 기본 경로에 추가
    if not os.path.isabs(file_path):
        if file_path.startswith("storage/") or file_path.startswith("../storage/"):
            # storage/ 제거
            relative = file_path.replace("storage/", "").replace("../storage/", "")
            network_path = normalize_network_path(network_base, relative)
            logger.info(f"경로 변환 (Relative → Network): {file_path} -> {network_path}")
            return network_path
    
    # 변환 실패 시 원본 반환 (경고 로그)
    logger.warning(f"경로 변환 실패, 원본 사용: {file_path}")
    return file_path


def ensure_network_path_accessible(network_path: str) -> bool:
    """
    네트워크 경로 접근 가능 여부를 확인합니다.
    
    Args:
        network_path: 확인할 네트워크 경로
        
    Returns:
        접근 가능하면 True, 아니면 False
    """
    try:
        if os.path.exists(network_path):
            return True
        # 디렉토리인 경우 부모 디렉토리 확인
        parent = os.path.dirname(network_path)
        if parent and os.path.exists(parent):
            return True
        return False
    except Exception as e:
        logger.warning(f"네트워크 경로 접근 확인 실패: {network_path}, 오류: {e}")
        return False

