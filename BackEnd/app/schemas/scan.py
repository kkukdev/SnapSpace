from pydantic import BaseModel, Field
from typing import Optional, Dict, Any, List
from datetime import datetime
from enum import Enum


class ScanStatus(str, Enum):
    """스캔 상태 열거형"""
    UPLOADED = "UPLOADED"
    COMPLETED = "COMPLETED"


class Scan(BaseModel):
    """스캔 응답 스키마"""
    scan_id: str = Field(..., description="스캔 고유 번호", example="SCAN_2024_001")
    group_id: int = Field(..., description="그룹 외래키", example=1)
    meta_data: Dict[str, Any] = Field(..., description="스캔본 메타데이터", example={
        "anchors": [
            {
                "qr_code": "QR_ANCHOR_001",
                "position": {"x": 1.25, "y": 1.5, "z": -3.4}
            }
        ],
        "scan_info": {
            "name": "1공정라인",
            "description": "스마트폰 생산 라인입니다.",
            "floor": "1",
            "section": "B-1"
        }
    })
    status: ScanStatus = Field(ScanStatus.UPLOADED, description="처리 상태")
    file_path: Optional[str] = Field(None, description="스캔 파일 경로", example="/uploads/scans/SCAN_2024_001.pdf")
    memos: Optional[List[Dict[str, Any]]] = Field(None, description="스캔에 포함된 메모 정보", example=[
        {
            "type": "text",
            "content": "Check conveyor belt alignment",
            "position": { "x": 1.25, "y": 1.5, "z": -3.4 }
        },
        {
            "type": "voice",
            "content": "/path/to/voice_memo_01.mp3",
            "position": { "x": 5.8, "y": 2.1, "z": -1.2 }
        }
    ])
    created_at: datetime = Field(..., description="스캔 데이터가 처음 DB에 기록된 시간")
    updated_at: datetime = Field(..., description="스캔 데이터가 마지막으로 수정된 시간")
    
    class Config:
        from_attributes = True


class ScanCreate(BaseModel):
    """스캔 생성 스키마"""
    scan_id: str = Field(..., description="스캔 고유 번호", example="SCAN_2024_001")
    group_id: int = Field(..., description="그룹 외래키", example=1)
    meta_data: Dict[str, Any] = Field(..., description="스캔본 메타데이터")
    status: ScanStatus = Field(ScanStatus.UPLOADED, description="처리 상태")
    file_path: Optional[str] = Field(None, description="스캔 파일 경로")
    memos: Optional[List[Dict[str, Any]]] = Field(None, description="스캔에 포함된 메모 정보")


class ScanUpdate(BaseModel):
    """스캔 수정 스키마"""
    meta_data: Optional[Dict[str, Any]] = Field(None, description="스캔본 메타데이터")
    status: Optional[ScanStatus] = Field(None, description="처리 상태")
    file_path: Optional[str] = Field(None, description="스캔 파일 경로")
    memos: Optional[List[Dict[str, Any]]] = Field(None, description="스캔에 포함된 메모 정보")


class ScanResponse(BaseModel):
    """스캔 응답 스키마 (BaseResponse 대체)"""
    message: str = Field(..., description="응답 메시지")
    success: bool = Field(True, description="요청 성공 여부")
    data: Optional[Scan] = Field(None, description="스캔 데이터")
    timestamp: str = Field(..., description="응답 시간")


class ScanListResponse(BaseModel):
    """스캔 목록 응답 스키마 (BaseResponse 대체)"""
    message: str = Field(..., description="응답 메시지")
    success: bool = Field(True, description="요청 성공 여부")
    data: List[Scan] = Field(..., description="스캔 목록")
    total: int = Field(..., description="전체 스캔 수")
    skip: int = Field(..., description="건너뛴 레코드 수")
    limit: int = Field(..., description="조회한 레코드 수")
    timestamp: str = Field(..., description="응답 시간")

