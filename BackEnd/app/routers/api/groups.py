from fastapi import APIRouter, Depends, Query, status
from sqlalchemy.orm import Session
from typing import Optional

from app.schemas.base import BaseResponse
from app.schemas.group import Group, GroupCreate, GroupCreateResponse
from app.services.group_service import group_service
from app.utils.dependencies import get_db

router = APIRouter()


@router.get(
    "/", 
    response_model=BaseResponse,
    summary="전체 그룹 목록 조회",
    description="데이터베이스에서 모든 그룹 데이터를 페이징하여 조회합니다.",
    tags=["groups"]
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
    - **meta_data**: 그룹 메타데이터 (JSON 형태, 필수)
    
    ### 📝 예시 요청
    ```json
    {
        "meta_data": {}
    }
    ```
    """
    return group_service.create_group(db=db, group_data=group_data)


@router.get(
    "/{group_id}",
    response_model=Group,
    summary="그룹 단일 조회",
    description="그룹 ID로 특정 그룹을 조회합니다.",
    tags=["groups"]
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
