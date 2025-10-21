from pydantic import BaseModel
from typing import Optional, Dict, Any
from datetime import datetime


class GroupBase(BaseModel):
    """그룹 기본 스키마"""
    meta_data: Dict[str, Any]


class GroupCreate(GroupBase):
    """그룹 생성 스키마"""
    pass


class GroupUpdate(BaseModel):
    """그룹 수정 스키마"""
    meta_data: Optional[Dict[str, Any]] = None


class GroupInDB(GroupBase):
    """데이터베이스 그룹 스키마"""
    group_id: int
    created_at: datetime
    updated_at: datetime
    
    class Config:
        from_attributes = True


class Group(GroupInDB):
    """그룹 응답 스키마"""
    pass
