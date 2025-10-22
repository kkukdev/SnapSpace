from typing import Any, Dict, List, Optional
from sqlalchemy.orm import Session
from datetime import datetime

from app.crud.scan import scan
from app.crud.group import group
from app.schemas.scan import ScanCreate, ScanUpdate, ScanResponse, ScanListResponse
from app.schemas.base import BaseResponse
from app.services.base import BaseService


class ScanService(BaseService):
    """스캔 서비스 클래스"""
    
    def __init__(self):
        super().__init__()

    def create_scan(self, db: Session, scan_data: ScanCreate) -> ScanResponse:
        """스캔 생성"""
        try:
            # 그룹 존재 여부 확인
            db_group = group.get_by_id(db, group_id=scan_data.group_id)
            if not db_group:
                return ScanResponse(
                    message="해당 그룹을 찾을 수 없습니다.",
                    success=False,
                    data=None,
                    timestamp=datetime.utcnow().isoformat() + "Z"
                )
            
            # 스캔 ID 중복 확인
            existing_scan = scan.get_by_scan_id(db, scan_id=scan_data.scan_id)
            if existing_scan:
                return ScanResponse(
                    message="이미 존재하는 스캔 ID입니다.",
                    success=False,
                    data=None,
                    timestamp=datetime.utcnow().isoformat() + "Z"
                )
            
            db_scan = scan.create(db, obj_in=scan_data)
            return ScanResponse(
                message="스캔이 성공적으로 생성되었습니다.",
                success=True,
                data=db_scan,
                timestamp=datetime.utcnow().isoformat() + "Z"
            )
        except Exception as e:
            return ScanResponse(
                message=f"스캔 생성 중 오류가 발생했습니다: {str(e)}",
                success=False,
                data=None,
                timestamp=datetime.utcnow().isoformat() + "Z"
            )

    def get_scan(self, db: Session, scan_id: str) -> ScanResponse:
        """스캔 조회"""
        try:
            db_scan = scan.get_by_scan_id(db, scan_id=scan_id)
            if not db_scan:
                return ScanResponse(
                    message="스캔을 찾을 수 없습니다.",
                    success=False,
                    data=None,
                    timestamp=datetime.utcnow().isoformat() + "Z"
                )
            return ScanResponse(
                message="스캔 조회가 완료되었습니다.",
                success=True,
                data=db_scan,
                timestamp=datetime.utcnow().isoformat() + "Z"
            )
        except Exception as e:
            return ScanResponse(
                message=f"스캔 조회 중 오류가 발생했습니다: {str(e)}",
                success=False,
                data=None,
                timestamp=datetime.utcnow().isoformat() + "Z"
            )

    def get_scans(self, db: Session, skip: int = 0, limit: int = 100) -> ScanListResponse:
        """스캔 목록 조회"""
        try:
            scans = scan.get_multi(db, skip=skip, limit=limit)
            total_count = scan.count(db)
            
            return ScanListResponse(
                message="스캔 목록 조회가 완료되었습니다.",
                success=True,
                data=scans,
                total=total_count,
                skip=skip,
                limit=limit,
                timestamp=datetime.utcnow().isoformat() + "Z"
            )
        except Exception as e:
            return ScanListResponse(
                message=f"스캔 목록 조회 중 오류가 발생했습니다: {str(e)}",
                success=False,
                data=[],
                total=0,
                skip=skip,
                limit=limit,
                timestamp=datetime.utcnow().isoformat() + "Z"
            )

    def get_scans_by_group(
        self, db: Session, group_id: int, skip: int = 0, limit: int = 100
    ) -> ScanListResponse:
        """그룹별 스캔 조회"""
        try:
            # 그룹 존재 여부 확인
            db_group = group.get_by_id(db, group_id=group_id)
            if not db_group:
                return ScanListResponse(
                    message="해당 그룹을 찾을 수 없습니다.",
                    success=False,
                    data=[],
                    total=0,
                    skip=skip,
                    limit=limit,
                    timestamp=datetime.utcnow().isoformat() + "Z"
                )
            
            scans = scan.get_by_group_id(db, group_id=group_id, skip=skip, limit=limit)
            
            return ScanListResponse(
                message="그룹별 스캔 조회가 완료되었습니다.",
                success=True,
                data=scans,
                total=len(scans),
                skip=skip,
                limit=limit,
                timestamp=datetime.utcnow().isoformat() + "Z"
            )
        except Exception as e:
            return ScanListResponse(
                message=f"그룹별 스캔 조회 중 오류가 발생했습니다: {str(e)}",
                success=False,
                data=[],
                total=0,
                skip=skip,
                limit=limit,
                timestamp=datetime.utcnow().isoformat() + "Z"
            )

    def get_scans_by_status(
        self, db: Session, status: str, skip: int = 0, limit: int = 100
    ) -> ScanListResponse:
        """상태별 스캔 조회"""
        try:
            scans = scan.get_by_status(db, status=status, skip=skip, limit=limit)
            
            return ScanListResponse(
                message="상태별 스캔 조회가 완료되었습니다.",
                success=True,
                data=scans,
                total=len(scans),
                skip=skip,
                limit=limit,
                timestamp=datetime.utcnow().isoformat() + "Z"
            )
        except Exception as e:
            return ScanListResponse(
                message=f"상태별 스캔 조회 중 오류가 발생했습니다: {str(e)}",
                success=False,
                data=[],
                total=0,
                skip=skip,
                limit=limit,
                timestamp=datetime.utcnow().isoformat() + "Z"
            )

    def update_scan(self, db: Session, scan_id: str, scan_data: ScanUpdate) -> ScanResponse:
        """스캔 수정"""
        try:
            db_scan = scan.get_by_scan_id(db, scan_id=scan_id)
            if not db_scan:
                return ScanResponse(
                    message="스캔을 찾을 수 없습니다.",
                    success=False
                )
            
            updated_scan = scan.update(db, db_obj=db_scan, obj_in=scan_data)
            return ScanResponse(
                message="스캔이 성공적으로 수정되었습니다.",
                data=updated_scan
            )
        except Exception as e:
            return ScanResponse(
                message=f"스캔 수정 중 오류가 발생했습니다: {str(e)}",
                success=False
            )

    def update_scan_status(self, db: Session, scan_id: str, status: str) -> ScanResponse:
        """스캔 상태 업데이트"""
        try:
            db_scan = scan.update_status(db, scan_id=scan_id, status=status)
            if not db_scan:
                return ScanResponse(
                    message="스캔을 찾을 수 없습니다.",
                    success=False
                )
            
            return ScanResponse(
                message="스캔 상태가 성공적으로 업데이트되었습니다.",
                data=db_scan
            )
        except Exception as e:
            return ScanResponse(
                message=f"스캔 상태 업데이트 중 오류가 발생했습니다: {str(e)}",
                success=False
            )

    def update_scan_file_path(self, db: Session, scan_id: str, file_path: str) -> ScanResponse:
        """스캔 파일 경로 업데이트"""
        try:
            db_scan = scan.update_file_path(db, scan_id=scan_id, file_path=file_path)
            if not db_scan:
                return ScanResponse(
                    message="스캔을 찾을 수 없습니다.",
                    success=False
                )
            
            return ScanResponse(
                message="스캔 파일 경로가 성공적으로 업데이트되었습니다.",
                data=db_scan
            )
        except Exception as e:
            return ScanResponse(
                message=f"스캔 파일 경로 업데이트 중 오류가 발생했습니다: {str(e)}",
                success=False
            )

    def update_scan_memos(self, db: Session, scan_id: str, memos: List[Dict[str, Any]]) -> ScanResponse:
        """스캔 메모 업데이트"""
        try:
            db_scan = scan.update_memos(db, scan_id=scan_id, memos=memos)
            if not db_scan:
                return ScanResponse(
                    message="스캔을 찾을 수 없습니다.",
                    success=False
                )
            
            return ScanResponse(
                message="스캔 메모가 성공적으로 업데이트되었습니다.",
                data=db_scan
            )
        except Exception as e:
            return ScanResponse(
                message=f"스캔 메모 업데이트 중 오류가 발생했습니다: {str(e)}",
                success=False
            )

    def delete_scan(self, db: Session, scan_id: str) -> ScanResponse:
        """스캔 삭제"""
        try:
            db_scan = scan.get_by_scan_id(db, scan_id=scan_id)
            if not db_scan:
                return ScanResponse(
                    message="스캔을 찾을 수 없습니다.",
                    success=False
                )
            
            scan.remove(db, id=scan_id)
            return ScanResponse(
                message="스캔이 성공적으로 삭제되었습니다."
            )
        except Exception as e:
            return ScanResponse(
                message=f"스캔 삭제 중 오류가 발생했습니다: {str(e)}",
                success=False
            )

    def get_scan_stats_by_group(self, db: Session, group_id: int) -> ScanResponse:
        """그룹별 스캔 통계 조회"""
        try:
            # 그룹 존재 여부 확인
            db_group = group.get_by_id(db, group_id=group_id)
            if not db_group:
                return ScanResponse(
                    message="해당 그룹을 찾을 수 없습니다.",
                    success=False
                )
            
            stats = scan.get_stats_by_group(db, group_id=group_id)
            
            return ScanResponse(
                message="그룹별 스캔 통계 조회가 완료되었습니다.",
                data={
                    "group_id": group_id,
                    "stats": stats
                }
            )
        except Exception as e:
            return ScanResponse(
                message=f"스캔 통계 조회 중 오류가 발생했습니다: {str(e)}",
                success=False
            )

    def search_scans_by_metadata(
        self, db: Session, metadata_filter: Dict[str, Any], skip: int = 0, limit: int = 100
    ) -> ScanResponse:
        """메타데이터로 스캔 검색"""
        try:
            scans = scan.get_multi_by_metadata(
                db, metadata_filter=metadata_filter, skip=skip, limit=limit
            )
            
            return ScanResponse(
                message="메타데이터 검색이 완료되었습니다.",
                data={
                    "scans": scans,
                    "filter": metadata_filter,
                    "skip": skip,
                    "limit": limit
                }
            )
        except Exception as e:
            return ScanResponse(
                message=f"스캔 검색 중 오류가 발생했습니다: {str(e)}",
                success=False
            )


# 서비스 인스턴스 생성
scan_service = ScanService()
