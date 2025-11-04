from pydantic_settings import BaseSettings
from typing import Optional
import os

class Settings(BaseSettings):
    # WebSocket 설정
    backend_websocket_url: str = "ws://70.12.246.48:8000/ws"
    websocket_ping_interval: int = 30
    websocket_ping_timeout: int = 10
    websocket_reconnect_interval: int = 5
    websocket_max_reconnect_attempts: int = 10
    
    # 파일 처리 설정
    # 네트워크 공유 폴더 기본 경로 (Windows UNC 경로)
    network_storage_base: str = "\\\\70.12.246.48\\storage"
    # 로컬 경로 또는 네트워크 경로 (상대 경로는 network_storage_base 기준)
    uploads_directory: str = "uploads"  # network_storage_base/uploads로 해석
    outputs_directory: str = "outputs"  # network_storage_base/outputs로 해석
    # 로컬 임시 디렉토리 (네트워크 대신 로컬에서 처리하여 성능 향상)
    local_temp_dir: str = None  # None이면 시스템 임시 디렉토리 사용
    max_concurrent_tasks: int = 3
    max_retry_attempts: int = 3
    processing_timeout: int = 3600
    
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

# 전역 설정 인스턴스
settings = Settings()