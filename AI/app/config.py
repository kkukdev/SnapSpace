from pydantic_settings import BaseSettings
from typing import Optional
import os

class Settings(BaseSettings):
    # WebSocket 설정
    backend_websocket_url: str
    # Backend API URL (WebSocket URL에서 추출)
    backend_api_url: Optional[str] = None  # None이면 websocket_url에서 자동 추출
    websocket_ping_interval: int = 30
    websocket_ping_timeout: int = 10
    websocket_reconnect_interval: int = 5
    websocket_max_reconnect_attempts: int = 10
    
    # 파일 처리 설정
    # 네트워크 공유 폴더 기본 경로 (Windows UNC 경로) - 선택적 (None이면 로컬 경로 사용)
    network_storage_base: Optional[str] = None
    # 로컬 경로 또는 네트워크 경로 (상대 경로는 network_storage_base 또는 로컬 storage 기준)
    uploads_directory: str = "uploads"  # storage/uploads로 해석
    outputs_directory: str = "outputs"  # storage/outputs로 해석
    # 로컬 임시 디렉토리 (네트워크 대신 로컬에서 처리하여 성능 향상)
    local_temp_dir: str = None  # None이면 시스템 임시 디렉토리 사용
    max_concurrent_tasks: int = 3
    max_retry_attempts: int = 3
    processing_timeout: int = 3600
    blender_executable: Optional[str] = None
    keep_texture_temp_artifacts: bool = False
    
    @property
    def storage_base_path(self) -> str:
        """
        저장소 기본 경로를 반환합니다.
        - Docker 환경: /project_root/storage
        - 로컬 환경: 프로젝트 루트의 storage
        - network_storage_base가 설정되어 있고 접근 가능하면 네트워크 경로 사용
        """
        # 네트워크 경로가 설정되어 있고 접근 가능하면 사용
        if self.network_storage_base:
            try:
                # 네트워크 경로 접근 가능 여부 확인
                if os.path.exists(self.network_storage_base):
                    return self.network_storage_base
            except Exception:
                pass  # 접근 불가하면 로컬 경로 사용
        
        # Docker 환경 확인
        if os.path.exists("/project_root/storage"):
            return "/project_root/storage"
        
        # 로컬 환경: 프로젝트 루트 찾기
        # AI/app/config.py -> AI/app -> AI -> 프로젝트 루트
        _config_file_dir = os.path.dirname(__file__)  # AI/app
        _ai_dir = os.path.dirname(_config_file_dir)  # AI
        _project_root = os.path.dirname(_ai_dir)  # 프로젝트 루트 (S13P31S102)
        return os.path.abspath(os.path.join(_project_root, "storage"))
    
    # 로깅 설정
    log_level: str = "INFO"
    log_file: str = "logs/ai_server.log"
    
    # 서버 설정
    host: str = "0.0.0.0"
    port: int = 8001
    
    class Config:
        env_file = "ai.env"
        env_file_encoding = "utf-8"
        case_sensitive = False
    
    def __init__(self, **kwargs):
        super().__init__(**kwargs)
        # backend_api_url이 없으면 websocket_url에서 추출
        if not self.backend_api_url:
            # ws://host:port/path -> http://host:port
            # wss://host:port/path -> https://host:port
            ws_url = self.backend_websocket_url
            if ws_url.startswith("ws://"):
                self.backend_api_url = ws_url.replace("ws://", "http://").split("/")[0]
            elif ws_url.startswith("wss://"):
                self.backend_api_url = ws_url.replace("wss://", "https://").split("/")[0]
            else:
                # 기본값 설정
                self.backend_api_url = "http://localhost:8000"

# 전역 설정 인스턴스
settings = Settings()