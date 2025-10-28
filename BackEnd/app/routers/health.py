from fastapi import APIRouter
from app.schemas.base import BaseResponse

router = APIRouter()


@router.get("/health", response_model=BaseResponse)
async def health_check():
    """서비스 헬스체크"""
    return BaseResponse(message="Service is healthy")


@router.get("/ready", response_model=BaseResponse)
async def readiness_check():
    """서비스 준비 상태 체크"""
    return BaseResponse(message="Service is ready")
