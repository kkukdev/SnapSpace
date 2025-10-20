from app.core.config import settings
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker

# pg8000 드라이버를 사용하도록 데이터베이스 URL 수정
database_url = str(settings.SQLALCHEMY_DATABASE_URI).replace("postgresql://", "postgresql+pg8000://")
engine = create_engine(database_url, pool_pre_ping=True)
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)
