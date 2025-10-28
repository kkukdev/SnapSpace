from pydantic_settings import BaseSettings
from pydantic import Field
from dotenv import load_dotenv
import os

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
    
    # File upload settings
    UPLOAD_DIR: str = os.path.join(os.path.dirname(os.path.dirname(__file__)), "uploads")
    MAX_FILE_SIZE: int = 250 * 1024 * 1024  # 250MB
    ALLOWED_EXTENSIONS: list = [".ply", ".obj", ".stl", ".3ds", ".dae", ".x3d", ".fbx", ".glb", ".gltf"]
    
    class Config:
        env_file = ".env"

settings = Settings()
