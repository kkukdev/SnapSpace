from pydantic import BaseModel, Field
from typing import Optional, Dict, Any, List
from datetime import datetime


class GroupMetaData(BaseModel):
    """그룹 메타데이터 스키마"""
    name: str = Field(..., description="그룹 이름", example="1공정라인")
    description: Optional[str] = Field(None, description="그룹 설명", example="스마트폰 생산 라인")
    location: Optional[str] = Field(None, description="위치 정보", example="1층 B-1구역")
    manager: Optional[str] = Field(None, description="담당자", example="김철수")


class GroupBase(BaseModel):
    """그룹 기본 스키마"""
    meta_data: Dict[str, Any] = Field(..., description="그룹 메타데이터")


class GroupCreate(GroupBase):
    """그룹 생성 스키마"""
    pass


class GroupUpdate(BaseModel):
    """그룹 수정 스키마"""
    meta_data: Optional[Dict[str, Any]] = Field(None, description="그룹 메타데이터")


class GroupInDB(BaseModel):
    """데이터베이스 그룹 스키마"""
    group_id: int = Field(..., description="그룹 고유 번호")
    meta_data: Dict[str, Any] = Field(..., description="그룹 메타데이터")
    created_at: datetime = Field(..., description="그룹 데이터가 처음 DB에 기록된 시간")
    updated_at: datetime = Field(..., description="그룹 데이터가 마지막으로 수정된 시간")
    
    class Config:
        from_attributes = True


class Group(GroupInDB):
    """그룹 응답 스키마"""
    pass


class GroupCreateResponse(BaseModel):
    """그룹 생성 응답 스키마 (BaseResponse 활용)"""
    message: str = Field(..., description="응답 메시지")
    success: bool = Field(True, description="요청 성공 여부")
    data: Optional[Group] = Field(None, description="그룹 데이터")
    timestamp: datetime = Field(default_factory=datetime.utcnow, description="응답 시간")


class ScanInGroup(BaseModel):
    """그룹 내 스캔 데이터 스키마"""
    scan_id: int = Field(..., description="스캔 고유 번호")
    group_id: int = Field(..., description="그룹 외래키")
    meta_data: Dict[str, Any] = Field(..., description="스캔본 메타데이터")
    status: str = Field(..., description="처리 상태")
    file_path: Optional[str] = Field(None, description="스캔 파일 경로")
    memos: Optional[List[Dict[str, Any]]] = Field(None, description="스캔에 포함된 메모 정보")
    created_at: datetime = Field(..., description="스캔 데이터가 처음 DB에 기록된 시간")
    updated_at: datetime = Field(..., description="스캔 데이터가 마지막으로 수정된 시간")
    
    class Config:
        from_attributes = True


class GroupWithScans(BaseModel):
    """스캔 데이터를 포함한 그룹 응답 스키마"""
    group_id: int = Field(..., description="그룹 고유 번호")
    meta_data: Dict[str, Any] = Field(..., description="그룹 메타데이터")
    scans: List[ScanInGroup] = Field(..., description="그룹에 속한 스캔 목록")
    created_at: datetime = Field(..., description="그룹 데이터가 처음 DB에 기록된 시간")
    updated_at: datetime = Field(..., description="그룹 데이터가 마지막으로 수정된 시간")
    
    class Config:
        from_attributes = True


class GroupScansResponse(BaseModel):
    """그룹의 스캔 목록 응답 스키마 (그룹 정보 제외)"""
    message: str = Field(..., description="응답 메시지")
    success: bool = Field(True, description="요청 성공 여부")
    data: List[ScanInGroup] = Field(..., description="그룹에 속한 스캔 목록")
    total: int = Field(..., description="전체 스캔 수")
    timestamp: datetime = Field(default_factory=datetime.utcnow, description="응답 시간")
