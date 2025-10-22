from typing import Any, Dict, List, Optional, Union
from sqlalchemy.orm import Session
from sqlalchemy import and_, or_

from app.crud.base import BaseCRUD
from app.models.group import Group
from app.schemas.group import GroupCreate, GroupUpdate


class GroupCRUD(BaseCRUD[Group, GroupCreate, GroupUpdate]):
    """그룹 CRUD 클래스"""
    
    def get_by_id(self, db: Session, *, group_id: int) -> Optional[Group]:
        """그룹 ID로 조회"""
        return db.query(Group).filter(Group.group_id == group_id).first()

    def get_multi_by_metadata(
        self, db: Session, *, metadata_filter: Dict[str, Any], skip: int = 0, limit: int = 100
    ) -> List[Group]:
        """메타데이터 필터로 그룹 조회"""
        query = db.query(Group)
        
        # JSON 필드에서 특정 키-값 쌍 검색
        for key, value in metadata_filter.items():
            query = query.filter(Group.meta_data[key].astext == str(value))
        
        return query.offset(skip).limit(limit).all()

    def create_with_metadata(
        self, db: Session, *, metadata: Dict[str, Any]
    ) -> Group:
        """메타데이터와 함께 그룹 생성"""
        db_obj = Group(meta_data=metadata)
        db.add(db_obj)
        db.commit()
        db.refresh(db_obj)
        return db_obj

    def update_metadata(
        self, db: Session, *, group_id: int, metadata: Dict[str, Any]
    ) -> Optional[Group]:
        """그룹 메타데이터 업데이트"""
        db_obj = self.get_by_id(db, group_id=group_id)
        if db_obj:
            db_obj.meta_data = metadata
            db.add(db_obj)
            db.commit()
            db.refresh(db_obj)
        return db_obj

    def get_with_scans(self, db: Session, *, group_id: int) -> Optional[Group]:
        """스캔 정보와 함께 그룹 조회"""
        return db.query(Group).filter(Group.group_id == group_id).first()

    def get_scans_count(self, db: Session, *, group_id: int) -> int:
        """그룹의 스캔 수 조회"""
        group = self.get_by_id(db, group_id=group_id)
        return len(group.scans) if group else 0


# CRUD 인스턴스 생성
group = GroupCRUD(Group)
