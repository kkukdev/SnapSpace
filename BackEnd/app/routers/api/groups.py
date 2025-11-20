from fastapi import APIRouter, Depends, Query, status
from sqlalchemy.orm import Session
from typing import Optional

from app.schemas.base import BaseResponse
from app.schemas.group import Group, GroupCreate, GroupCreateResponse, GroupUpdate, GroupScansResponse
from app.services.group_service import group_service
from app.utils.dependencies import get_db

router = APIRouter()


@router.get(
    "/", 
    response_model=BaseResponse,
    summary="전체 그룹 목록 조회",
    description="데이터베이스에서 모든 그룹 데이터를 페이징하여 조회합니다.",
    tags=["groups"],
    responses={
        200: {
            "description": "그룹 목록 조회 성공",
            "content": {
                "application/json": {
                    "example": {
                        "message": "그룹 목록 조회가 완료되었습니다.",
                        "success": True,
                        "data": {
                            "groups": [
                                {
                                    "group_id": 1,
                                    "name": "제조공장 A",
                                    "meta_data": {
                                        "location": "서울시 강남구",
                                        "type": "manufacturing"
                                    },
                                    "created_at": "2024-01-15T09:00:00Z",
                                    "updated_at": "2024-01-15T09:00:00Z"
                                }
                            ],
                            "total": 1,
                            "skip": 0,
                            "limit": 100
                        },
                        "timestamp": "2024-01-15T09:00:00Z"
                    }
                }
            }
        }
    }
)
async def get_groups(
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
    ## 📄 전체 그룹 목록 조회
    
    데이터베이스에 저장된 모든 그룹 데이터를 페이징하여 조회합니다.
    
    ### 📋 파라미터
    - **skip**: 건너뛸 레코드 수 (기본값: 0)
    - **limit**: 조회할 레코드 수 (기본값: 100, 최대: 1000)
    """
    return group_service.get_groups(db=db, skip=skip, limit=limit)


@router.post(
    "/",
    response_model=GroupCreateResponse,
    status_code=status.HTTP_201_CREATED,
    summary="그룹 생성",
    description="새로운 그룹을 생성합니다.",
    tags=["groups"],
    responses={
        201: {
            "description": "그룹 생성 성공",
            "content": {
                "application/json": {
                    "example": {
                        "message": "created",
                        "success": True,
                        "data": {
                            "group_id": 1,
                            "name": "1공정라인",
                            "meta_data": {},
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
                                "loc": ["body", "meta_data"],
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
async def create_group(
    group_data: GroupCreate,
    db: Session = Depends(get_db)
):
    """
    ## 📄 그룹 생성
    
    새로운 그룹을 생성합니다. group_id는 자동으로 생성됩니다.
    
    ### 📋 요청 데이터
    - **name**: 그룹 이름 (필수)
    - **meta_data**: 그룹 메타데이터 (JSON 형태, 필수)
    
    ### 📝 예시 요청
    ```json
    {
        "name": "1공정라인",
        "meta_data": {}
    }
    ```
    """
    return group_service.create_group(db=db, group_data=group_data)


@router.get(
    "/{group_id}",
    response_model=GroupCreateResponse,
    summary="그룹 단일 조회",
    description="그룹 ID로 특정 그룹을 조회합니다.",
    tags=["groups"],
    responses={
        200: {
            "description": "그룹 조회 성공",
            "content": {
                "application/json": {
                    "example": {
                        "message": "그룹 조회가 완료되었습니다.",
                        "success": True,
                        "data": {
                            "group_id": 1,
                            "name": "제조공장 A",
                            "meta_data": {
                                "location": "서울시 강남구",
                                "type": "manufacturing"
                            },
                            "created_at": "2024-01-15T09:00:00Z",
                            "updated_at": "2024-01-15T09:00:00Z"
                        },
                        "timestamp": "2024-01-15T09:00:00Z"
                    }
                }
            }
        },
        404: {
            "description": "그룹을 찾을 수 없음",
            "content": {
                "application/json": {
                    "example": {
                        "detail": "그룹을 찾을 수 없습니다."
                    }
                }
            }
        }
    }
)
async def get_group(
    group_id: int,
    db: Session = Depends(get_db)
):
    """
    ## 📄 그룹 단일 조회
    
    그룹 ID로 특정 그룹을 조회합니다.
    
    ### 📋 파라미터
    - **group_id**: 조회할 그룹의 고유 ID
    """
    return group_service.get_group(db=db, group_id=group_id)


@router.put(
    "/{group_id}",
    response_model=BaseResponse,
    summary="그룹 수정",
    description="그룹 정보를 수정합니다.",
    tags=["groups"],
    responses={
        200: {
            "description": "그룹 수정 성공",
            "content": {
                "application/json": {
                    "example": {
                        "message": "그룹이 성공적으로 수정되었습니다.",
                        "success": True,
                        "data": {
                            "group_id": 1,
                            "name": "제조공장 A (수정됨)",
                            "meta_data": {
                                "location": "서울시 강남구",
                                "type": "manufacturing"
                            },
                            "created_at": "2024-01-15T09:00:00Z",
                            "updated_at": "2024-01-15T10:00:00Z"
                        },
                        "timestamp": "2024-01-15T10:00:00Z"
                    }
                }
            }
        },
        404: {
            "description": "그룹을 찾을 수 없음"
        }
    }
)
async def update_group(
    group_id: int,
    group_data: GroupUpdate,
    db: Session = Depends(get_db)
):
    """
    ## 📄 그룹 수정
    
    그룹 정보를 수정합니다.
    
    ### 📋 파라미터
    - **group_id**: 수정할 그룹의 고유 ID
    
    ### 📋 요청 데이터
    - **name**: 그룹 이름 (선택사항)
    - **meta_data**: 그룹 메타데이터 (JSON 형태, 선택사항)
    """
    return group_service.update_group(db=db, group_id=group_id, group_data=group_data)


@router.delete(
    "/{group_id}",
    response_model=BaseResponse,
    summary="그룹 삭제",
    description="그룹을 삭제합니다.",
    tags=["groups"],
    responses={
        200: {
            "description": "그룹 삭제 성공",
            "content": {
                "application/json": {
                    "example": {
                        "message": "그룹이 성공적으로 삭제되었습니다.",
                        "success": True,
                        "timestamp": "2024-01-15T10:00:00Z"
                    }
                }
            }
        },
        400: {
            "description": "그룹에 스캔이 있어 삭제할 수 없음",
            "content": {
                "application/json": {
                    "example": {
                        "message": "그룹에 3개의 스캔이 있어 삭제할 수 없습니다.",
                        "success": False,
                        "timestamp": "2024-01-15T10:00:00Z"
                    }
                }
            }
        },
        404: {
            "description": "그룹을 찾을 수 없음"
        }
    }
)
async def delete_group(
    group_id: int,
    db: Session = Depends(get_db)
):
    """
    ## 📄 그룹 삭제
    
    그룹을 삭제합니다. 관련된 스캔이 있는 경우 삭제할 수 없습니다.
    
    ### 📋 파라미터
    - **group_id**: 삭제할 그룹의 고유 ID
    """
    return group_service.delete_group(db=db, group_id=group_id)


@router.get(
    "/{group_id}/scans",
    response_model=GroupScansResponse,
    summary="그룹의 스캔 목록 조회",
    description="특정 그룹에 속한 스캔 목록을 조회합니다.",
    tags=["groups"],
    responses={
        200: {
            "description": "스캔 목록 조회 성공",
            "content": {
                "application/json": {
                    "example": {
                        "message": "그룹의 스캔 목록 조회가 완료되었습니다.",
                        "success": True,
                        "data": [
                            {
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
                                "original_file_path": "/uploads/scans/scan_001.pdf",
                                "retouched_file_path": None,
                                "memos": [
                                    {
                                        "type": "text",
                                        "content": "Check conveyor belt alignment",
                                        "position": { "x": 1.25, "y": 1.5, "z": -3.4 }
                                    }
                                ],
                                "created_at": "2024-01-15T09:00:00Z",
                                "updated_at": "2024-01-15T09:00:00Z"
                            },
                            {
                                "scan_id": 2,
                                "group_id": 1,
                                "meta_data": {
                                    "anchors": [
                                        {
                                            "qr_code": "QR_ANCHOR_002",
                                            "position": {"x": 2.5, "y": 3.0, "z": -1.8}
                                        }
                                    ],
                                    "scan_info": {
                                        "name": "2공정라인",
                                        "description": "스마트폰 조립 라인입니다.",
                                        "floor": "1",
                                        "section": "B-2"
                                    }
                                },
                                "status": "COMPLETED",
                                "original_file_path": "/uploads/scans/scan_002.pdf",
                                "retouched_file_path": "/uploads/scans/scan_002_retouched.pdf",
                                "memos": [
                                    {
                                        "type": "voice",
                                        "content": "/path/to/voice_memo_02.mp3",
                                        "position": { "x": 2.5, "y": 3.0, "z": -1.8 }
                                    }
                                ],
                                "created_at": "2024-01-15T10:00:00Z",
                                "updated_at": "2024-01-15T11:00:00Z"
                            }
                        ],
                        "total": 2,
                        "timestamp": "2024-01-15T09:00:00Z"
                    }
                }
            }
        },
        404: {
            "description": "그룹을 찾을 수 없음",
            "content": {
                "application/json": {
                    "example": {
                        "message": "그룹을 찾을 수 없습니다.",
                        "success": False,
                        "data": [],
                        "total": 0,
                        "timestamp": "2024-01-15T09:00:00Z"
                    }
                }
            }
        }
    }
)
async def get_group_scans(
    group_id: int,
    db: Session = Depends(get_db)
):
    """
    ## 📄 그룹의 스캔 목록 조회
    
    특정 그룹에 속한 스캔 목록을 조회합니다.
    
    ### 📋 파라미터
    - **group_id**: 조회할 그룹의 고유 ID
    """
    return group_service.get_group_scans_only(db=db, group_id=group_id)
