from fastapi import APIRouter, Depends, Query, status
from sqlalchemy.orm import Session
from typing import Optional

from app.schemas.scan import Scan, ScanCreate, ScanUpdate, ScanListResponse, ScanCreateResponse
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


@router.put(
    "/{scan_id}",
    response_model=ScanCreateResponse,
    summary="스캔 수정",
    description="스캔 ID로 특정 스캔의 데이터를 수정합니다.",
    tags=["scans"],
    responses={
        200: {
            "description": "스캔 수정 성공",
            "content": {
                "application/json": {
                    "example": {
                        "message": "스캔이 성공적으로 수정되었습니다.",
                        "success": True,
                        "data": {
                            "scan_id": 1,
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
                                }
                            ],
                            "created_at": "2024-01-15T09:00:00Z",
                            "updated_at": "2024-01-15T12:00:00Z"
                        },
                        "timestamp": "2024-01-15T12:00:00Z"
                    }
                }
            }
        },
        404: {
            "description": "스캔을 찾을 수 없음",
            "content": {
                "application/json": {
                    "example": {
                        "message": "스캔을 찾을 수 없습니다.",
                        "success": False,
                        "timestamp": "2024-01-15T12:00:00Z"
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
                                "loc": ["body", "status"],
                                "msg": "value is not a valid enumeration member; permitted: 'UPLOADED', 'COMPLETED'",
                                "type": "type_error.enum"
                            }
                        ]
                    }
                }
            }
        }
    }
)
async def update_scan(
    scan_id: int,
    scan_data: ScanUpdate,
    db: Session = Depends(get_db)
):
    """
    ## 📄 스캔 수정
    
    스캔 ID로 특정 스캔의 데이터를 수정합니다. 모든 필드는 선택사항이며, 제공된 필드만 업데이트됩니다.
    
    ### 📋 파라미터
    - **scan_id**: 수정할 스캔의 고유 ID
    
    ### 📋 요청 데이터 (모든 필드 선택사항)
    - **meta_data**: 스캔 메타데이터 (JSON 형태)
    - **status**: 스캔 상태 (UPLOADED, COMPLETED)
    - **file_path**: 스캔 파일 경로
    - **memos**: 메모 정보 (JSON 형태)
    
    ### 📝 예시 요청
    ```json
    {
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
            }
        ]
    }
    ```
    """
    return scan_service.update_scan(db=db, scan_id=scan_id, scan_data=scan_data)


@router.delete(
    "/{scan_id}",
    response_model=ScanCreateResponse,
    summary="스캔 삭제",
    description="스캔 ID로 특정 스캔을 삭제합니다.",
    tags=["scans"],
    responses={
        200: {
            "description": "스캔 삭제 성공",
            "content": {
                "application/json": {
                    "example": {
                        "message": "스캔이 성공적으로 삭제되었습니다.",
                        "success": True,
                        "timestamp": "2024-01-15T12:00:00Z"
                    }
                }
            }
        },
        404: {
            "description": "스캔을 찾을 수 없음",
            "content": {
                "application/json": {
                    "example": {
                        "message": "스캔을 찾을 수 없습니다.",
                        "success": False,
                        "timestamp": "2024-01-15T12:00:00Z"
                    }
                }
            }
        },
        500: {
            "description": "서버 내부 오류",
            "content": {
                "application/json": {
                    "example": {
                        "message": "스캔 삭제 중 오류가 발생했습니다: [오류 메시지]",
                        "success": False,
                        "timestamp": "2024-01-15T12:00:00Z"
                    }
                }
            }
        }
    }
)
async def delete_scan(
    scan_id: int,
    db: Session = Depends(get_db)
):
    """
    ## 📄 스캔 삭제
    
    스캔 ID로 특정 스캔을 데이터베이스에서 완전히 삭제합니다.
    
    ### ⚠️ 주의사항
    - 이 작업은 **되돌릴 수 없습니다**
    - 삭제된 스캔 데이터는 복구할 수 없습니다
    - 관련된 파일들도 함께 삭제를 고려해야 합니다
    
    ### 📋 파라미터
    - **scan_id**: 삭제할 스캔의 고유 ID
    
    ### 🔍 응답
    - **200**: 삭제 성공
    - **404**: 스캔을 찾을 수 없음
    - **500**: 서버 내부 오류
    """
    return scan_service.delete_scan(db=db, scan_id=scan_id)


