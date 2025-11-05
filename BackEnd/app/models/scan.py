from sqlalchemy import Column, String, Text, DateTime, JSON, ForeignKey, Integer
from sqlalchemy.orm import relationship
from app.models.base import BaseModel


class Scan(BaseModel):
    """스캔 데이터 모델"""
    __tablename__ = "scans"
    
    scan_id = Column(Integer, primary_key=True, autoincrement=True, index=True, comment="스캔 고유 번호")
    group_id = Column(Integer, ForeignKey("groups.group_id"), nullable=False, comment="그룹 외래키")
    meta_data = Column(JSON, nullable=False, comment="스캔본 메타데이터")
    status = Column(String, default="UPLOADED", nullable=False, comment="처리 상태")
    original_file_path = Column(Text, comment="원본 스캔 파일 경로")
    retouched_file_path = Column(Text, comment="리터치된 스캔 파일 경로")
    memos = Column(JSON, comment="스캔에 포함된 메모 정보")
    
    # 관계 설정
    group = relationship("Group", back_populates="scans")
    
    def __repr__(self):
        return f"<Scan(scan_id={self.scan_id}, status={self.status})>"
