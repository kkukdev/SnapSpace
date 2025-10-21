from fastapi import APIRouter
from app.schemas.base import BaseResponse

router = APIRouter()


@router.get("/", response_model=BaseResponse)
async def token_info():
    """토큰 정보 조회"""
    return BaseResponse(message="Token endpoint")
