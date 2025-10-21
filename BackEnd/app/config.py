from pydantic_settings import BaseSettings
from typing import Optional
from dotenv import load_dotenv
import os

load_dotenv()

class Settings(BaseSettings):
    PROJECT_NAME: str = "FastAPI Project"
    VERSION: str = "1.0.0"
    DEBUG: bool = True
    
    # PostgreSQL Database settings
    DB_HOST: str = os.getenv("POSTGRES_HOST")
    DB_PORT: int = int(os.getenv("POSTGRES_PORT"))
    DB_NAME: str = os.getenv("POSTGRES_DB")
    DB_USER: str = os.getenv("POSTGRES_USER")
    DB_PASSWORD: str = os.getenv("POSTGRES_PASSWORD")
    
    @property
    def DATABASE_URL(self) -> str:
        return f"postgresql://{self.DB_USER}:{self.DB_PASSWORD}@{self.DB_HOST}:{self.DB_PORT}/{self.DB_NAME}"

    # API settings
    API_V1_STR: str = "/api/v1"
    
    class Config:
        env_file = ".env"


settings = Settings()
