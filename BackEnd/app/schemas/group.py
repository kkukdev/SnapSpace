from pydantic import BaseModel, Field
from typing import Optional, Dict, Any
from datetime import datetime


class GroupMetaData(BaseModel):
    """그룹 메타데이터 스키마"""
    name: str = Field(..., description="그룹 이름", example="1공정라인")
    description: Optional[str] = Field(None, description="그룹 설명", example="스마트폰 생산 라인")
    location: Optional[str] = Field(None, description="위치 정보", example="1층 B-1구역")
    manager: Optional[str] = Field(None, description="담당자", example="김철수")


class GroupBase(BaseModel):
    """그룹 기본 스키마"""
    meta_data: GroupMetaData = Field(..., description="그룹 메타데이터")


class GroupCreate(GroupBase):
    """그룹 생성 스키마"""
    pass


class GroupUpdate(BaseModel):
    """그룹 수정 스키마"""
    meta_data: Optional[GroupMetaData] = Field(None, description="그룹 메타데이터")


class GroupInDB(GroupBase):
    """데이터베이스 그룹 스키마"""
    group_id: int = Field(..., description="그룹 고유 번호")
    created_at: datetime = Field(..., description="그룹 데이터가 처음 DB에 기록된 시간")
    updated_at: datetime = Field(..., description="그룹 데이터가 마지막으로 수정된 시간")
    
    class Config:
        from_attributes = True


class Group(GroupInDB):
    """그룹 응답 스키마"""
    pass
