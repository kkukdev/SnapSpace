from sqlalchemy.ext.declarative import declarative_base
from sqlalchemy import Column, DateTime
from datetime import datetime, timezone

from app.config import get_current_datetime

Base = declarative_base()


class BaseModel(Base):
    """모든 모델의 기본 클래스"""
    __abstract__ = True
    
    created_at = Column(DateTime, default=get_current_datetime, nullable=False)
    updated_at = Column(DateTime, default=get_current_datetime, onupdate=get_current_datetime, nullable=False)
