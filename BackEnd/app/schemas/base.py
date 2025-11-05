from pydantic import BaseModel, Field
from datetime import datetime
from typing import Optional, Any, Dict, List, Generic, TypeVar

from app.config import get_current_datetime

DataT = TypeVar('DataT')


class BaseSchema(BaseModel):
    """모든 스키마의 기본 클래스"""
    class Config:
        from_attributes = True


class BaseResponse(BaseModel):
    """기본 응답 스키마"""
    message: str = Field(..., description="응답 메시지")
    success: bool = Field(True, description="요청 성공 여부")
    data: Optional[Any] = Field(None, description="응답 데이터")
    timestamp: datetime = Field(default_factory=get_current_datetime, description="응답 시간")


class ErrorResponse(BaseModel):
    """에러 응답 스키마"""
    message: str = Field(..., description="에러 메시지")
    success: bool = Field(False, description="요청 성공 여부")
    error_code: Optional[str] = Field(None, description="에러 코드")
    details: Optional[Dict[str, Any]] = Field(None, description="에러 상세 정보")
    timestamp: datetime = Field(default_factory=get_current_datetime, description="에러 발생 시간")
