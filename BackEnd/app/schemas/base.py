from pydantic import BaseModel
from datetime import datetime
from typing import Optional


class BaseSchema(BaseModel):
    """모든 스키마의 기본 클래스"""
    class Config:
        from_attributes = True


class BaseResponse(BaseSchema):
    """기본 응답 스키마"""
    message: str
    success: bool = True


class ErrorResponse(BaseSchema):
    """에러 응답 스키마"""
    message: str
    success: bool = False
    error_code: Optional[str] = None
