from typing import Any, Dict, List, Optional
from sqlalchemy.orm import Session
from fastapi import HTTPException, status
from app.database import SessionLocal

from app.crud.group import group
from app.schemas.group import GroupCreate, GroupUpdate, GroupCreateResponse
from app.schemas.base import BaseResponse
from app.services.base import BaseService


class GroupService(BaseService):
    """그룹 서비스 클래스"""
    
    def __init__(self):
        super().__init__()

    def create_group(self, db: Session, group_data: GroupCreate) -> GroupCreateResponse:
        """그룹 생성"""
        # 의존성 주입된 db가 제너레이터인 경우를 대비해 새로운 세션 생성
        if hasattr(db, '__iter__'):  # 제너레이터인지 확인
            db = SessionLocal()
        
        try:
            # 직접 Group 모델 인스턴스 생성
            from app.models.group import Group
            db_group = Group(meta_data=group_data.meta_data)
            db.add(db_group)
            db.commit()
            db.refresh(db_group)
            
            return GroupCreateResponse(
                message="created",
                success=True,
                data=db_group
            )
        except Exception as e:
            db.rollback()
            raise HTTPException(
                status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                detail=f"그룹 생성 중 오류가 발생했습니다: {str(e)}"
            )
        finally:
            if hasattr(db, '__iter__'):  # 새로 생성한 세션인 경우에만 닫기
                db.close()

    def get_group(self, db: Session, group_id: int) -> GroupCreateResponse:
        """그룹 조회"""
        try:
            db_group = group.get_by_id(db, group_id=group_id)
            if not db_group:
                raise HTTPException(
                    status_code=status.HTTP_404_NOT_FOUND,
                    detail="그룹을 찾을 수 없습니다."
                )
            return GroupCreateResponse(
                message="그룹 조회가 완료되었습니다.",
                success=True,
                data=db_group
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
            
            return self.create_response(
                message="그룹 목록 조회가 완료되었습니다.",
                data={
                    "groups": groups,
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
            db_group = group.get_by_id(db, group_id=group_id)
            if not db_group:
                return self.create_response(
                    message="그룹을 찾을 수 없습니다.",
                    success=False
                )
            
            updated_group = group.update(db, db_obj=db_group, obj_in=group_data)
            return self.create_response(
                message="그룹이 성공적으로 수정되었습니다.",
                data=updated_group
            )
        except Exception as e:
            return self.create_response(
                message=f"그룹 수정 중 오류가 발생했습니다: {str(e)}",
                success=False
            )

    def delete_group(self, db: Session, group_id: int) -> BaseResponse:
        """그룹 삭제"""
        try:
            db_group = group.get_by_id(db, group_id=group_id)
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
            
            return self.create_response(
                message="그룹과 스캔 정보 조회가 완료되었습니다.",
                data=db_group
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
            
            return self.create_response(
                message="메타데이터 검색이 완료되었습니다.",
                data={
                    "groups": groups,
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
