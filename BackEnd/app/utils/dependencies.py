from typing import Generator
from fastapi import Depends, HTTPException, status
from fastapi.security import HTTPBearer, HTTPAuthorizationCredentials

# 나중에 인증 시스템 추가 시 사용할 의존성들

security = HTTPBearer()


def get_current_user(credentials: HTTPAuthorizationCredentials = Depends(security)):
    """현재 사용자 정보를 가져오는 의존성 (나중에 구현)"""
    # TODO: JWT 토큰 검증 로직 구현
    pass


def get_db():
    """데이터베이스 세션을 가져오는 의존성 (나중에 구현)"""
    # TODO: 데이터베이스 세션 반환 로직 구현
    pass
