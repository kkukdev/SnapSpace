from typing import Any, Dict, Optional
from app.schemas.base import BaseResponse


class BaseService:
    """모든 서비스의 기본 클래스"""
    
    def __init__(self):
        pass
    
    def create_response(self, message: str, data: Optional[Any] = None, success: bool = True) -> BaseResponse:
        """표준 응답 생성"""
        response_data = {"message": message, "success": success}
        if data is not None:
            response_data["data"] = data
        return BaseResponse(**response_data)
