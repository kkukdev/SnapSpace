from typing import Any, Dict, List, Optional
from sqlalchemy.orm import Session
from fastapi import HTTPException, status

from app.crud.group import group
from app.schemas.group import GroupCreate, GroupUpdate, GroupCreateResponse, Group
from app.schemas.base import BaseResponse
from app.services.base import BaseService


class GroupService(BaseService):
    """그룹 서비스 클래스"""
    
    def __init__(self):
        super().__init__()

    def create_group(self, db: Session, group_data: GroupCreate) -> GroupCreateResponse:
        """그룹 생성"""
        try:
            # CRUD의 create 메서드 사용
            db_group = group.create(db, obj_in=group_data)
            
            # SQLAlchemy 모델을 Pydantic 스키마로 변환
            group_schema = Group.model_validate(db_group)
            
            return GroupCreateResponse(
                message="created",
                success=True,
                data=group_schema
            )
        except Exception as e:
            db.rollback()
            raise HTTPException(
                status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                detail=f"그룹 생성 중 오류가 발생했습니다: {str(e)}"
            )

    def get_group(self, db: Session, group_id: int) -> GroupCreateResponse:
        """그룹 조회"""
        try:
            db_group = group.get_by_group_id(db, group_id=group_id)
            if not db_group:
                raise HTTPException(
                    status_code=status.HTTP_404_NOT_FOUND,
                    detail="그룹을 찾을 수 없습니다."
                )
            
            # SQLAlchemy 모델을 Pydantic 스키마로 변환
            group_schema = Group.model_validate(db_group)
            
            return GroupCreateResponse(
                message="그룹 조회가 완료되었습니다.",
                success=True,
                data=group_schema
            )
        except HTTPException:
            raise
        except Exception as e:
            raise HTTPException(
                status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                detail=f"그룹 조회 중 오류가 발생했습니다: {str(e)}"
            )

    def get_groups(self, db: Session, skip: int = 0, limit: int = 100) -> BaseResponse:
        """그룹 목록 조회"""
        try:
            groups = group.get_multi(db, skip=skip, limit=limit)
            total_count = group.count(db)
            
            # SQLAlchemy 모델들을 Pydantic 스키마로 변환
            groups_schema = [Group.model_validate(group_obj) for group_obj in groups]
            
            return self.create_response(
                message="그룹 목록 조회가 완료되었습니다.",
                data={
                    "groups": groups_schema,
                    "total": total_count,
                    "skip": skip,
                    "limit": limit
                }
            )
        except Exception as e:
            return self.create_response(
                message=f"그룹 목록 조회 중 오류가 발생했습니다: {str(e)}",
                success=False
            )

    def update_group(self, db: Session, group_id: int, group_data: GroupUpdate) -> BaseResponse:
        """그룹 수정"""
        try:
            db_group = group.get_by_group_id(db, group_id=group_id)
            if not db_group:
                return self.create_response(
                    message="그룹을 찾을 수 없습니다.",
                    success=False
                )
            
            updated_group = group.update(db, db_obj=db_group, obj_in=group_data)
            
            # SQLAlchemy 모델을 Pydantic 스키마로 변환
            group_schema = Group.model_validate(updated_group)
            
            return self.create_response(
                message="그룹이 성공적으로 수정되었습니다.",
                data=group_schema
            )
        except Exception as e:
            return self.create_response(
                message=f"그룹 수정 중 오류가 발생했습니다: {str(e)}",
                success=False
            )

    def delete_group(self, db: Session, group_id: int) -> BaseResponse:
        """그룹 삭제"""
        try:
            db_group = group.get_by_group_id(db, group_id=group_id)
            if not db_group:
                return self.create_response(
                    message="그룹을 찾을 수 없습니다.",
                    success=False
                )
            
            # 관련 스캔이 있는지 확인
            scans_count = group.get_scans_count(db, group_id=group_id)
            if scans_count > 0:
                return self.create_response(
                    message=f"그룹에 {scans_count}개의 스캔이 있어 삭제할 수 없습니다.",
                    success=False
                )
            
            group.remove(db, id=group_id)
            return self.create_response(
                message="그룹이 성공적으로 삭제되었습니다."
            )
        except Exception as e:
            return self.create_response(
                message=f"그룹 삭제 중 오류가 발생했습니다: {str(e)}",
                success=False
            )

    def get_group_with_scans(self, db: Session, group_id: int) -> BaseResponse:
        """스캔 정보와 함께 그룹 조회"""
        try:
            db_group = group.get_with_scans(db, group_id=group_id)
            if not db_group:
                return self.create_response(
                    message="그룹을 찾을 수 없습니다.",
                    success=False
                )
            
            # SQLAlchemy 모델을 Pydantic 스키마로 변환
            group_schema = Group.model_validate(db_group)
            
            return self.create_response(
                message="그룹과 스캔 정보 조회가 완료되었습니다.",
                data=group_schema
            )
        except Exception as e:
            return self.create_response(
                message=f"그룹 조회 중 오류가 발생했습니다: {str(e)}",
                success=False
            )

    def search_groups_by_metadata(
        self, db: Session, metadata_filter: Dict[str, Any], skip: int = 0, limit: int = 100
    ) -> BaseResponse:
        """메타데이터로 그룹 검색"""
        try:
            groups = group.get_multi_by_metadata(
                db, metadata_filter=metadata_filter, skip=skip, limit=limit
            )
            
            # SQLAlchemy 모델들을 Pydantic 스키마로 변환
            groups_schema = [Group.model_validate(group_obj) for group_obj in groups]
            
            return self.create_response(
                message="메타데이터 검색이 완료되었습니다.",
                data={
                    "groups": groups_schema,
                    "filter": metadata_filter,
                    "skip": skip,
                    "limit": limit
                }
            )
        except Exception as e:
            return self.create_response(
                message=f"그룹 검색 중 오류가 발생했습니다: {str(e)}",
                success=False
            )


# 서비스 인스턴스 생성
group_service = GroupService()
