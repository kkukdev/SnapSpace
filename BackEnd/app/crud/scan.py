from typing import Any, Dict, List, Optional, Union
from sqlalchemy.orm import Session
from sqlalchemy import and_, or_

from app.crud.base import BaseCRUD
from app.models.scan import Scan
from app.schemas.scan import ScanCreate, ScanUpdate


class ScanCRUD(BaseCRUD[Scan, ScanCreate, ScanUpdate]):
    """스캔 CRUD 클래스"""
    
    def get_multi(
        self, db: Session, *, skip: int = 0, limit: int = 100
    ) -> List[Scan]:
        """다중 레코드 조회 (scan_id 기준 오름차순 정렬)"""
        return (
            db.query(Scan)
            .order_by(Scan.scan_id.asc())
            .offset(skip)
            .limit(limit)
            .all()
        )
    
    def create(self, db: Session, *, obj_in: ScanCreate) -> Scan:
        """스캔 생성 (scan_id 자동 생성)"""
        # scan_id는 자동 생성되므로 명시적으로 제외하고 생성
        db_obj = Scan(
            group_id=obj_in.group_id,
            meta_data=obj_in.meta_data,
            status=obj_in.status.value if obj_in.status else "UPLOADED",
            file_path=obj_in.file_path,
            memos=obj_in.memos
        )
        db.add(db_obj)
        db.commit()
        db.refresh(db_obj)
        return db_obj
    
    def get_by_scan_id(self, db: Session, *, scan_id: int) -> Optional[Scan]:
        """스캔 ID로 조회"""
        return db.query(Scan).filter(Scan.scan_id == scan_id).first()

    def get_by_group_id(
        self, db: Session, *, group_id: int, skip: int = 0, limit: int = 100
    ) -> List[Scan]:
        """그룹 ID로 스캔 목록 조회"""
        return (
            db.query(Scan)
            .filter(Scan.group_id == group_id)
            .offset(skip)
            .limit(limit)
            .all()
        )

    def get_by_status(
        self, db: Session, *, status: str, skip: int = 0, limit: int = 100
    ) -> List[Scan]:
        """상태별 스캔 조회"""
        return (
            db.query(Scan)
            .filter(Scan.status == status)
            .offset(skip)
            .limit(limit)
            .all()
        )

    def get_by_group_and_status(
        self, db: Session, *, group_id: int, status: str, skip: int = 0, limit: int = 100
    ) -> List[Scan]:
        """그룹과 상태로 스캔 조회"""
        return (
            db.query(Scan)
            .filter(and_(Scan.group_id == group_id, Scan.status == status))
            .offset(skip)
            .limit(limit)
            .all()
        )

    def update_status(
        self, db: Session, *, scan_id: int, status: str
    ) -> Optional[Scan]:
        """스캔 상태 업데이트"""
        db_obj = self.get_by_scan_id(db, scan_id=scan_id)
        if db_obj:
            db_obj.status = status
            db.add(db_obj)
            db.commit()
            db.refresh(db_obj)
        return db_obj

    def update_file_path(
        self, db: Session, *, scan_id: int, file_path: str
    ) -> Optional[Scan]:
        """스캔 파일 경로 업데이트"""
        db_obj = self.get_by_scan_id(db, scan_id=scan_id)
        if db_obj:
            db_obj.file_path = file_path
            db.add(db_obj)
            db.commit()
            db.refresh(db_obj)
        return db_obj

    def update_memos(
        self, db: Session, *, scan_id: int, memos: List[Dict[str, Any]]
    ) -> Optional[Scan]:
        """스캔 메모 업데이트"""
        db_obj = self.get_by_scan_id(db, scan_id=scan_id)
        if db_obj:
            db_obj.memos = memos
            db.add(db_obj)
            db.commit()
            db.refresh(db_obj)
        return db_obj

    def get_multi_by_metadata(
        self, db: Session, *, metadata_filter: Dict[str, Any], skip: int = 0, limit: int = 100
    ) -> List[Scan]:
        """메타데이터 필터로 스캔 조회"""
        query = db.query(Scan)
        
        # JSON 필드에서 특정 키-값 쌍 검색
        for key, value in metadata_filter.items():
            query = query.filter(Scan.meta_data[key].astext == str(value))
        
        return query.offset(skip).limit(limit).all()

    def get_stats_by_group(self, db: Session, *, group_id: int) -> Dict[str, int]:
        """그룹별 스캔 통계 조회"""
        scans = self.get_by_group_id(db, group_id=group_id, skip=0, limit=1000)
        
        stats = {
            "total": len(scans),
            "uploaded": len([s for s in scans if s.status == "UPLOADED"]),
            "processing": len([s for s in scans if s.status == "PROCESSING"]),
            "completed": len([s for s in scans if s.status == "COMPLETED"]),
            "failed": len([s for s in scans if s.status == "FAILED"])
        }
        
        return stats


# CRUD 인스턴스 생성
scan = ScanCRUD(Scan)
