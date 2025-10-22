from fastapi import APIRouter
from app.schemas.base import BaseResponse

router = APIRouter()


@router.get("/", response_model=BaseResponse)
async def root():
    """루트 엔드포인트"""
    return BaseResponse(message="Hello World")
