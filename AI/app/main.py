import asyncio
import logging
from contextlib import asynccontextmanager
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.services.websocket_manager import websocket_manager
from app.config import settings
from app.routers import health, websocket

# 로깅 설정
logging.basicConfig(
    level=getattr(logging, settings.log_level),
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

@asynccontextmanager
async def lifespan(app: FastAPI):
    """앱 시작/종료 시 실행되는 함수"""
    # 시작 시
    logger.info("Starting AI Service...")
    
    # Backend 서버에 연결 시도
    connection_success = await websocket_manager.connect_to_backend()
    if connection_success:
        logger.info("Successfully connected to Backend server")
        # 하트비트 시작
        asyncio.create_task(websocket_manager.start_heartbeat())
    else:
        logger.warning("Failed to connect to Backend server, will retry...")
        # 백그라운드에서 재연결 시도
        asyncio.create_task(websocket_manager.reconnect())
    
    yield
    
    # 종료 시
    logger.info("Shutting down AI Service...")
    await websocket_manager.disconnect()

app = FastAPI(
    title="AI Service", 
    version="1.0.0",
    lifespan=lifespan
)

# CORS 설정
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(websocket.router, prefix="/ws", tags=["websocket"])
app.include_router(health.router, tags=["health"])


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        app, 
        host=settings.host,  # .env.ai에서 읽어온 값 사용
        port=settings.port   # .env.ai에서 읽어온 값 사용
    )