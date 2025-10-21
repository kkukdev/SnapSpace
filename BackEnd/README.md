# FastAPI Project

모듈화된 FastAPI 프로젝트 구조입니다.

## 프로젝트 구조

```
BackEnd/
├── app/                    # 메인 애플리케이션
│   ├── main.py            # FastAPI 앱 인스턴스
│   ├── config.py          # 설정 관리
│   ├── database.py        # DB 연결 (PostgreSQL용)
│   ├── models/            # SQLAlchemy 모델들
│   ├── schemas/           # Pydantic 스키마들
│   ├── routers/           # API 라우터들
│   ├── services/          # 비즈니스 로직
│   └── utils/             # 유틸리티 함수들
├── tests/                 # 테스트 파일들
├── requirements.txt       # 의존성
├── .env                  # 환경 변수
└── run.py                # 개발 서버 실행
```

## 실행 방법

### 방법 1: 로컬에서 직접 실행

#### 1. 환경 설정
```bash
# 가상환경 활성화
source venv/bin/activate  # Linux/Mac
# 또는
venv\Scripts\activate     # Windows

# 의존성 설치
pip install -r requirements.txt
```

#### 2. PostgreSQL 설정 (Docker 사용)
```bash
# PostgreSQL 컨테이너 실행
docker run --name postgres-db -e POSTGRES_PASSWORD=password -e POSTGRES_DB=fastapi_db -p 5432:5432 -d postgres:15

# 또는 Docker Compose 사용
docker-compose up postgres -d
```

#### 3. 서버 실행
```bash
# 서버 실행 (두 가지 방법)
python -m app.main        # 직접 실행
python run.py            # 실행 스크립트 사용
```

### 방법 2: Docker Compose로 전체 스택 실행

#### 1. 환경 변수 설정
```bash
# .env 파일 생성 (예시)
POSTGRES_DB=fastapi_db
POSTGRES_USER=postgres
POSTGRES_PASSWORD=password
POSTGRES_HOST=localhost
POSTGRES_PORT=5432
```

#### 2. 전체 스택 실행
```bash
# PostgreSQL + FastAPI 함께 실행
docker-compose up -d

# 로그 확인
docker-compose logs -f

# 중지
docker-compose down
```

## 기본 엔드포인트

- `GET /api/v1/` - 루트 엔드포인트
- `GET /health` - 서비스 헬스체크
- `GET /ready` - 서비스 준비 상태
- `GET /api/v1/docs` - Swagger UI

## 다음 단계

1. **SQLAlchemy 모델 정의** - 데이터베이스 테이블 구조 만들기
2. **Alembic 마이그레이션 설정** - 데이터베이스 스키마 버전 관리
3. **CRUD API 구현** - 실제 데이터 조작 API 만들기
