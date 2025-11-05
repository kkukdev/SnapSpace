from pydantic import BaseModel, Field, field_validator
from typing import Optional, Dict, Any, List
from datetime import datetime
from enum import Enum


class ScanStatus(str, Enum):
    """스캔 상태 열거형"""
    UPLOADED = "UPLOADED"
    COMPLETED = "COMPLETED"


class Scan(BaseModel):
    """스캔 응답 스키마"""
    scan_id: int = Field(..., description="스캔 고유 번호", example=1)
    group_id: int = Field(..., description="그룹 외래키", example=1)
    meta_data: Dict[str, Any] = Field(..., description="스캔본 메타데이터")
    status: ScanStatus = Field(ScanStatus.UPLOADED, description="처리 상태")
    original_file_path: Optional[str] = Field(None, description="원본 스캔 파일 경로")
    retouched_file_path: Optional[str] = Field(None, description="리터치된 스캔 파일 경로")
    memos: Optional[List[Dict[str, Any]]] = Field(None, description="스캔에 포함된 메모 정보")
    created_at: datetime = Field(..., description="스캔 데이터가 처음 DB에 기록된 시간")
    updated_at: datetime = Field(..., description="스캔 데이터가 마지막으로 수정된 시간")
    
    @field_validator('status', mode='before')
    @classmethod
    def validate_status(cls, v):
        """status 필드를 ScanStatus enum으로 변환"""
        if isinstance(v, ScanStatus):
            return v
        if isinstance(v, str):
            # 'ScanStatus.UPLOADED' 형식의 문자열 처리
            if v.startswith('ScanStatus.'):
                v = v.replace('ScanStatus.', '')
            try:
                return ScanStatus(v)
            except ValueError:
                # 유효하지 않은 값인 경우 기본값 반환
                return ScanStatus.UPLOADED
        return ScanStatus.UPLOADED
    
    class Config:
        from_attributes = True


class ScanCreate(BaseModel):
    """스캔 생성 스키마"""
    group_id: int = Field(..., description="그룹 외래키", example=1)
    meta_data: Dict[str, Any] = Field(..., description="스캔본 메타데이터")
    status: ScanStatus = Field(ScanStatus.UPLOADED, description="처리 상태")
    original_file_path: Optional[str] = Field(None, description="원본 스캔 파일 경로")
    retouched_file_path: Optional[str] = Field(None, description="리터치된 스캔 파일 경로")
    memos: Optional[List[Dict[str, Any]]] = Field(None, description="스캔에 포함된 메모 정보")


class ScanUpdate(BaseModel):
    """스캔 수정 스키마"""
    meta_data: Optional[Dict[str, Any]] = Field(None, description="스캔본 메타데이터")
    status: Optional[ScanStatus] = Field(None, description="처리 상태")
    original_file_path: Optional[str] = Field(None, description="원본 스캔 파일 경로")
    retouched_file_path: Optional[str] = Field(None, description="리터치된 스캔 파일 경로")
    memos: Optional[List[Dict[str, Any]]] = Field(None, description="스캔에 포함된 메모 정보")


class ScanCreateResponse(BaseModel):
    """스캔 생성 응답 스키마 (BaseResponse 활용)"""
    message: str = Field(..., description="응답 메시지")
    success: bool = Field(True, description="요청 성공 여부")
    data: Optional[Scan] = Field(None, description="스캔 데이터")
    timestamp: datetime = Field(default_factory=datetime.utcnow, description="응답 시간")


class ScanListResponse(BaseModel):
    """스캔 목록 응답 스키마 (BaseResponse 대체)"""
    message: str = Field(..., description="응답 메시지")
    success: bool = Field(True, description="요청 성공 여부")
    data: List[Scan] = Field(..., description="스캔 목록")
    total: int = Field(..., description="전체 스캔 수")
    skip: int = Field(..., description="건너뛴 레코드 수")
    limit: int = Field(..., description="조회한 레코드 수")
    timestamp: str = Field(..., description="응답 시간")

