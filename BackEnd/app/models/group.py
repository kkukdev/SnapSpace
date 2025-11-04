from sqlalchemy import Column, Integer, DateTime, JSON
from sqlalchemy.orm import relationship
from app.models.base import BaseModel


class Group(BaseModel):
    """그룹(스캔할 장소 큰 범주) 모델"""
    __tablename__ = "groups"
    
    group_id = Column(Integer, primary_key=True, index=True, autoincrement=True)
    meta_data = Column(JSON, nullable=False, comment="그룹 메타데이터")
    
    # 관계 설정
    scans = relationship("Scan", back_populates="group")
    
    def __repr__(self):
        return f"<Group(group_id={self.group_id})>"
