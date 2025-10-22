from fastapi import APIRouter, Depends, Query, status
from sqlalchemy.orm import Session
from typing import Optional

from app.schemas.scan import Scan, ScanCreate, ScanListResponse, ScanCreateResponse
from app.services.scan_service import scan_service
from app.utils.dependencies import get_db

router = APIRouter()


@router.get(
    "/", 
    response_model=ScanListResponse,
    summary="전체 스캔 목록 조회",
    description="데이터베이스에서 모든 스캔 데이터를 페이징하여 조회합니다.",
    responses={
        200: {
            "description": "스캔 목록 조회 성공",
            "content": {
                "application/json": {
                    "example": {
                        "message": "스캔 목록 조회가 완료되었습니다.",
                        "success": True,
                        "data": [
                            {
                                "scan_id": "SCAN_2024_001",
                                "group_id": 1,
                                "meta_data": {
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
                                },
                                "status": "UPLOADED",
                                "file_path": "/uploads/scans/SCAN_2024_001.pdf",
                                "memos": [
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
                                ],
                                "created_at": "2024-01-15T09:00:00Z",
                                "updated_at": "2024-01-15T12:00:00Z"
                            }
                        ],
                        "total": 150,
                        "skip": 0,
                        "limit": 100,
                        "timestamp": "2024-01-15T00:00:00Z"
                    }
                }
            }
        },
        422: {
            "description": "요청 데이터 검증 실패",
            "content": {
                "application/json": {
                    "example": {
                        "detail": [
                            {
                                "loc": ["query", "limit"],
                                "msg": "ensure this value is less than or equal to 1000",
                                "type": "value_error.number.not_le"
                            }
                        ]
                    }
                }
            }
        }
    }
)
async def get_scans(
    skip: int = Query(
        0, 
        ge=0, 
        description="건너뛸 레코드 수",
        example=0
    ),
    limit: int = Query(
        100, 
        ge=1, 
        le=1000, 
        description="조회할 레코드 수 (최대 1000개)",
        example=100
    ),
    db: Session = Depends(get_db)
):
    """
    ## 📄 전체 스캔 목록 조회
    
    데이터베이스에 저장된 모든 스캔 데이터를 페이징하여 조회합니다.
    
    ### 📋 파라미터
    - **skip**: 건너뛸 레코드 수 (기본값: 0)
    - **limit**: 조회할 레코드 수 (기본값: 100, 최대: 1000)
    
    ### 🔍 스캔 상태
    - `UPLOADED`: 업로드 완료
    - `PROCESSING`: 처리 중
    - `COMPLETED`: 완료 (`.obj` 파일로 변환됨)
    - `FAILED`: 처리 실패
    """
    return scan_service.get_scans(db=db, skip=skip, limit=limit)


@router.post(
    "/",
    response_model=ScanCreateResponse,
    status_code=status.HTTP_201_CREATED,
    summary="스캔 생성",
    description="새로운 스캔을 생성합니다.",
    tags=["scans"],
    responses={
        201: {
            "description": "스캔 생성 성공",
            "content": {
                "application/json": {
                    "example": {
                        "message": "created",
                        "success": True,
                        "data": {
                            "scan_id": 1,
                            "group_id": 1,
                            "meta_data": {},
                            "status": "UPLOADED",
                            "file_path": None,
                            "memos": None,
                            "created_at": "2024-01-15T09:00:00Z",
                            "updated_at": "2024-01-15T09:00:00Z"
                        },
                        "timestamp": "2024-01-15T09:00:00Z"
                    }
                }
            }
        },
        422: {
            "description": "요청 데이터 검증 실패",
            "content": {
                "application/json": {
                    "example": {
                        "detail": [
                            {
                                "loc": ["body", "group_id"],
                                "msg": "field required",
                                "type": "value_error.missing"
                            }
                        ]
                    }
                }
            }
        }
    }
)
async def create_scan(
    scan_data: ScanCreate,
    db: Session = Depends(get_db)
):
    """
    ## 📄 스캔 생성
    
    새로운 스캔을 생성합니다. scan_id는 자동으로 생성됩니다.
    
    ### 📋 요청 데이터
    - **group_id**: 그룹 ID (필수)
    - **meta_data**: 스캔 메타데이터 (JSON 형태, 필수)
    - **status**: 스캔 상태 (기본값: UPLOADED)
    - **file_path**: 스캔 파일 경로 (선택사항)
    - **memos**: 메모 정보 (JSON 형태, 선택사항)
    
    ### 📝 예시 요청
    ```json
    {
        "group_id": 1,
        "meta_data": {},
        "memos": null
    }
    ```
    """
    return scan_service.create_scan(db=db, scan_data=scan_data)


@router.get(
    "/{scan_id}",
    response_model=Scan,
    summary="스캔 단일 조회",
    description="스캔 ID로 특정 스캔을 조회합니다.",
    tags=["scans"]
)
async def get_scan(
    scan_id: int,
    db: Session = Depends(get_db)
):
    """
    ## 📄 스캔 단일 조회
    
    스캔 ID로 특정 스캔을 조회합니다.
    
    ### 📋 파라미터
    - **scan_id**: 조회할 스캔의 고유 ID
    """
    return scan_service.get_scan(db=db, scan_id=scan_id)


