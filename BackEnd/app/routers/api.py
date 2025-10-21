from fastapi import APIRouter
from app.schemas.base import BaseResponse

router = APIRouter()


@router.get("/", response_model=BaseResponse)
async def root():
    """루트 엔드포인트"""
    return BaseResponse(message="Hello World")


@router.get("/health", response_model=BaseResponse)
async def health_check():
    """API 헬스체크"""
    return BaseResponse(message="API is running")
