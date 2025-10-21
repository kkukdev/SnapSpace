# PostgreSQL 데이터베이스 연결 설정
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker
from app.config import settings
from app.models.base import Base

# PostgreSQL 데이터베이스 연결 (한글 인코딩 설정)
engine = create_engine(
    settings.DATABASE_URL,
    echo=False,  # SQL 쿼리 로그 출력 여부
    pool_pre_ping=True,  # 연결 상태 확인
    connect_args={
        "options": "-c timezone=Asia/Seoul -c client_encoding=UTF8"  # 한국 시간대 및 UTF-8 설정
    }
)
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)


def get_db():
    """데이터베이스 세션을 가져오는 의존성"""
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()


def create_tables():
    """데이터베이스 테이블 생성"""
    try:
        print("🔧 테이블 생성 시작...")
        Base.metadata.create_all(bind=engine)
        print("✅ 모든 테이블이 성공적으로 생성되었습니다!")
        
        # 생성된 테이블 목록 확인
        from sqlalchemy import inspect
        inspector = inspect(engine)
        tables = inspector.get_table_names()
        print(f"📋 생성된 테이블: {tables}")
        
    except Exception as e:
        print(f"❌ 테이블 생성 중 오류 발생: {e}")
        raise
