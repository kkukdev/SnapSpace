from pydantic import BaseModel
from typing import Optional, Dict, Any, List
from datetime import datetime


class ScanBase(BaseModel):
    """스캔 기본 스키마"""
    group_id: int
    meta_data: Dict[str, Any]
    status: str = "UPLOADED"
    file_path: Optional[str] = None
    memos: Optional[List[Dict[str, Any]]] = None


class ScanCreate(ScanBase):
    """스캔 생성 스키마"""
    scan_id: str


class ScanUpdate(BaseModel):
    """스캔 수정 스키마"""
    meta_data: Optional[Dict[str, Any]] = None
    status: Optional[str] = None
    file_path: Optional[str] = None
    memos: Optional[List[Dict[str, Any]]] = None


class ScanInDB(ScanBase):
    """데이터베이스 스캔 스키마"""
    scan_id: str
    created_at: datetime
    updated_at: datetime
    
    class Config:
        from_attributes = True


class Scan(ScanInDB):
    """스캔 응답 스키마"""
    pass
