from pydantic_settings import BaseSettings
from pydantic import Field
from dotenv import load_dotenv
import os
from datetime import datetime, timezone, timedelta

load_dotenv()

class Settings(BaseSettings):
    PROJECT_NAME: str = "FastAPI Project"
    VERSION: str = "1.0.0"
    DEBUG: bool = True
    
    # PostgreSQL Database settings
    DB_HOST: str = Field(default=os.getenv("POSTGRES_HOST"), alias="POSTGRES_HOST")
    DB_PORT: int = Field(default=os.getenv("POSTGRES_PORT"), alias="POSTGRES_PORT")
    DB_NAME: str = Field(default=os.getenv("POSTGRES_DB"), alias="POSTGRES_DB")
    DB_USER: str = Field(default=os.getenv("POSTGRES_USER"), alias="POSTGRES_USER")
    DB_PASSWORD: str = Field(default=os.getenv("POSTGRES_PASSWORD"), alias="POSTGRES_PASSWORD")
    
    @property
    def DATABASE_URL(self) -> str:
        return f"postgresql://{self.DB_USER}:{self.DB_PASSWORD}@{self.DB_HOST}:{self.DB_PORT}/{self.DB_NAME}"

    # API settings
    API_V1_STR: str = "/api/v1"
    
    # Timezone settings
    # .env에서 TIMEZONE_OFFSET 읽기 (없으면 UTC+0, 기본값은 UTC+9)
    TIMEZONE_OFFSET: int = Field(
        default=9,  # 기본값 UTC+9
        alias="TIMEZONE_OFFSET"
    )
    
    @property
    def TIMEZONE(self) -> timezone:
        """시간대 반환 (.env에서 TIMEZONE_OFFSET 읽기, 없으면 UTC+0)"""
        # .env에서 TIMEZONE_OFFSET 읽기
        tz_offset = os.getenv("TIMEZONE_OFFSET")
        if tz_offset is None:
            # .env에 없으면 UTC+0 사용
            offset_hours = 0
        else:
            # .env에 있으면 그 값 사용
            offset_hours = int(tz_offset)
        return timezone(timedelta(hours=offset_hours))
    
    # File upload settings
    # 프로젝트 루트의 storage/uploads 폴더 사용
    if os.path.exists("/project_root/storage"):
        # Docker 컨테이너 내부 (프로젝트 루트의 storage가 /project_root/storage로 마운트됨)
        UPLOAD_DIR: str = "/project_root/storage/uploads"
    else:
        # 로컬 환경: 프로젝트 루트 찾기
        # BackEnd/app/config.py -> BackEnd/app -> BackEnd -> 프로젝트 루트
        _config_file_dir = os.path.dirname(__file__)  # BackEnd/app
        _backend_dir = os.path.dirname(_config_file_dir)  # BackEnd
        _project_root = os.path.dirname(_backend_dir)  # 프로젝트 루트 (S13P31S102)
        UPLOAD_DIR: str = os.path.abspath(os.path.join(_project_root, "storage", "uploads"))
    MAX_FILE_SIZE: int = 250 * 1024 * 1024  # 250MB
    ALLOWED_EXTENSIONS: list = [".ply", ".obj", ".stl", ".3ds", ".dae", ".x3d", ".fbx", ".glb", ".gltf", ".zip"]
    
    class Config:
        env_file = ".env"

settings = Settings()


def get_current_datetime() -> datetime:
    """현재 시간을 설정된 시간대로 반환 (.env에서 TIMEZONE_OFFSET 읽기, 없으면 UTC+0)"""
    return datetime.now(settings.TIMEZONE)
