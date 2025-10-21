from fastapi import APIRouter
from app.schemas.base import BaseResponse

router = APIRouter()


@router.get("/", response_model=BaseResponse)
async def get_scans():
    """스캔 목록 조회"""
    return BaseResponse(message="Scans endpoint")
