from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from app.routers import health, websocket
from app.routers.api import upload, scans, groups
from app.config import settings
from app.database import create_tables
from contextlib import asynccontextmanager
import logging

# 로깅 설정
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)


@asynccontextmanager
async def lifespan(app: FastAPI):
    """애플리케이션 생명주기 관리"""
    # 시작 시 실행
    create_tables()

    yield
    # 종료 시 실행 (필요한 경우)


app = FastAPI(
    title="SSAFY Digital Twin API",
    version="1.0.0",
    docs_url="/api/v1/docs",
    redoc_url="/api/v1/redoc",
    openapi_url="/api/v1/openapi.json",
    lifespan=lifespan
)

# CORS 미들웨어 추가
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # 모든 출처 허용 (개발 환경용)
    allow_credentials=True,
    allow_methods=["*"],  # 모든 HTTP 메서드 허용
    allow_headers=["*"],  # 모든 헤더 허용
)

# 라우터 등록
app.include_router(health.router, tags=["health"])
app.include_router(upload.router, prefix="/api/v1/upload", tags=["upload"])
app.include_router(scans.router, prefix="/api/v1/scans", tags=["scans"])
app.include_router(groups.router, prefix="/api/v1/groups", tags=["groups"])
app.include_router(websocket.router, prefix="/ws", tags=["websocket"])


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        "app.main:app",
        host="0.0.0.0",
        port=8000,
        reload=True,
        reload_dirs=["app"]
    )
