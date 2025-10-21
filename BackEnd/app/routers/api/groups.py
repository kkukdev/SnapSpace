from fastapi import APIRouter
from app.schemas.base import BaseResponse

router = APIRouter()


@router.get("/", response_model=BaseResponse)
async def get_groups():
    """그룹 목록 조회"""
    return BaseResponse(message="Groups endpoint")
