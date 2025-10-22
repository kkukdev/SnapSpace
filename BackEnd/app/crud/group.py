from __future__ import annotations

from typing import Any, Dict, List, Optional, Union
from sqlalchemy.orm import Session
from sqlalchemy import and_, or_

from app.crud.base import BaseCRUD
from app.models.group import Group
from app.schemas.group import GroupCreate, GroupUpdate


class GroupCRUD(BaseCRUD[Group, GroupCreate, GroupUpdate]):
    """그룹 CRUD 클래스"""
    
    def get_multi(
        self, db: Session, *, skip: int = 0, limit: int = 100
    ) -> List[Group]:
        """다중 레코드 조회 (group_id 기준 오름차순 정렬)"""
        return (
            db.query(Group)
            .order_by(Group.group_id.asc())
            .offset(skip)
            .limit(limit)
            .all()
        )
    
    def create(self, db: Session, *, obj_in: GroupCreate) -> Group:
        """그룹 생성 (group_id 자동 생성)"""
        # group_id는 자동 생성되므로 명시적으로 제외하고 생성
        db_obj = Group(
            meta_data=obj_in.meta_data
        )
        db.add(db_obj)
        db.commit()
        db.refresh(db_obj)
        return db_obj
    
    def get_by_group_id(self, db: Session, *, group_id: int) -> Optional[Group]:
        """그룹 ID로 조회"""
        return db.query(Group).filter(Group.group_id == group_id).first()
    
    def get(self, db: Session, id: Any) -> Optional[Group]:
        """단일 레코드 조회 (group_id 사용)"""
        return db.query(Group).filter(Group.group_id == id).first()

    def get_multi_by_metadata(
        self, db: Session, *, metadata_filter: Dict[str, Any], skip: int = 0, limit: int = 100
    ) -> List[Group]:
        """메타데이터 필터로 그룹 조회"""
        query = db.query(Group)
        
        # JSON 필드에서 특정 키-값 쌍 검색
        for key, value in metadata_filter.items():
            query = query.filter(Group.meta_data[key].astext == str(value))
        
        return query.offset(skip).limit(limit).all()

    def update_metadata(
        self, db: Session, *, group_id: int, metadata: Dict[str, Any]
    ) -> Optional[Group]:
        """그룹 메타데이터 업데이트"""
        db_obj = self.get_by_group_id(db, group_id=group_id)
        if db_obj:
            db_obj.meta_data = metadata
            db.add(db_obj)
            db.commit()
            db.refresh(db_obj)
        return db_obj

    def get_with_scans(self, db: Session, *, group_id: int) -> Optional[Group]:
        """스캔 정보와 함께 그룹 조회"""
        from sqlalchemy.orm import joinedload
        from app.models.scan import Scan
        from sqlalchemy import asc
        
        group = db.query(Group).options(joinedload(Group.scans)).filter(Group.group_id == group_id).first()
        if group and group.scans:
            # 스캔 데이터를 scan_id 순서로 정렬
            group.scans.sort(key=lambda x: x.scan_id)
        return group

    def get_scans_count(self, db: Session, *, group_id: int) -> int:
        """그룹의 스캔 수 조회"""
        group = self.get_by_group_id(db, group_id=group_id)
        return len(group.scans) if group else 0

    def get_stats_by_group(self, db: Session, *, group_id: int) -> Dict[str, int]:
        """그룹별 통계 조회"""
        group = self.get_by_group_id(db, group_id=group_id)
        if not group:
            return {"total_scans": 0}
        
        scans = group.scans
        stats = {
            "total_scans": len(scans),
            "uploaded": len([s for s in scans if s.status == "UPLOADED"]),
            "processing": len([s for s in scans if s.status == "PROCESSING"]),
            "completed": len([s for s in scans if s.status == "COMPLETED"]),
            "failed": len([s for s in scans if s.status == "FAILED"])
        }
        
        return stats
    
    def remove(self, db: Session, *, id: int) -> Group:
        """레코드 삭제 (group_id 사용)"""
        obj = db.query(Group).filter(Group.group_id == id).first()
        if obj:
            db.delete(obj)
            db.commit()
        return obj


# CRUD 인스턴스 생성
group = GroupCRUD(Group)
