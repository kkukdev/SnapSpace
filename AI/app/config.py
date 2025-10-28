from pydantic_settings import BaseSettings
from typing import Optional
import os

class Settings(BaseSettings):
    # WebSocket 설정
    backend_websocket_url: str = "ws://localhost:8001/ws/ai"
    websocket_ping_interval: int = 30
    websocket_ping_timeout: int = 10
    websocket_reconnect_interval: int = 5
    websocket_max_reconnect_attempts: int = 10
    
    # 파일 처리 설정
    uploads_directory: str = "storage/uploads"
    outputs_directory: str = "storage/outputs"
    max_concurrent_tasks: int = 3
    max_retry_attempts: int = 3
    processing_timeout: int = 3600
    
    # 로깅 설정
    log_level: str = "INFO"
    log_file: str = "logs/ai_server.log"
    
    # 서버 설정
    host: str = "0.0.0.0"
    port: int = 8000
    
    class Config:
        env_file = "ai.env"
        env_file_encoding = "utf-8"
        case_sensitive = False

# 전역 설정 인스턴스
settings = Settings()