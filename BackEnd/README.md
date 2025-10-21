# FastAPI Project

모듈화된 FastAPI 프로젝트 구조입니다.

## 프로젝트 구조

```
BackEnd/
├── app/                    # 메인 애플리케이션
│   ├── main.py            # FastAPI 앱 인스턴스 + 실행 코드
│   ├── config.py          # 환경 변수 설정
│   ├── database.py        # PostgreSQL 연결
│   ├── models/            # SQLAlchemy 모델 (준비됨)
│   ├── schemas/           # Pydantic 스키마 (준비됨)
│   ├── routers/           # API 라우터 (기본 엔드포인트)
│   ├── services/          # 비즈니스 로직 (준비됨)
│   └── utils/             # 유틸리티 함수 (준비됨)
├── tests/                 # 테스트 파일 (기본 구조)
├── requirements.txt       # Python 의존성
├── docker-compose.yml     # Docker Compose 설정
├── .env                  # 환경 변수 (PostgreSQL 설정)
└── run.py                # 개발 서버 실행 스크립트
```

## 실행 방법

### 필수 요구사항
- **Docker** 설치 필요
- **.env 파일** 필수 (데이터베이스 설정)

### 1. 환경 변수 설정
```bash
# .env 파일 생성 (BackEnd/.env)
POSTGRES_DB=fastapi_db
POSTGRES_USER=postgres
POSTGRES_PASSWORD=password
POSTGRES_HOST=localhost
POSTGRES_PORT=5432
```

### 2. 전체 스택 실행
```bash
# PostgreSQL + FastAPI 함께 실행
docker-compose up -d

# 로그 확인
docker-compose logs -f

# 중지
docker-compose down
```

### 3. 개발 모드 (로컬에서 FastAPI만 실행)
```bash
# 가상환경 활성화
venv\Scripts\activate     # Windows
# 또는
source venv/bin/activate  # Linux/Mac

# 의존성 설치
pip install -r requirements.txt

# PostgreSQL만 Docker로 실행
docker-compose up postgres -d

# FastAPI 서버 실행
python -m app.main
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
