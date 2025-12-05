# SnapSpace - 스마트폰기반 제조 현장 3D 스캔 솔루션

**프로젝트 기간**: SSAFY 13기 자율프로젝트  
**프로젝트명**: SnapSpace (SSAFY Digital Twin)  
**팀**: S102  
**팀원**: 이상엽, 오병국, 진형민, 이수정, 이용하, 김동현  

## 📋 프로젝트 개요

**SnapSpace**는 스마트폰을 활용한 제조 현장 3D 스캔 솔루션입니다. Android 기반 ARCore를 사용하여 실시간으로 공간을 스캔하고, 후처리 파이프라인을 통해 메시 최적화, 노이즈 제거 등의 후처리를 수행합니다.

## 🔎 주요 기능

### 📱 **실시간 3D 스캔**
스마트폰을 기반으로 공간이나 사물을 스캔하고 저장하여 서버로 전달합니다. 
스캔 중 위치를 기반으로 텍스트나 음성 메모를 남겨 함께 저장합니다.

1. Android ARcore기반으로 이미지에서 깊이 정보 추출
2. TSDF알고리즘을 사용하여 3차원 공간으로 깊이 정보를 누적하여 실시간 재구성
3. Marching Cubes알고리즘으로 TSDF로 표현된 3D 볼륨에서 mesh형태로 변환하여 모델을 생성

---
### 🤖 **데이터 후처리**
로컬 서버에 저장된 공간 or 사물 모델의 메시 최적화 및 노이즈 제거 후 원본과 함께 저장합니다.

- Optimizer
1. 소형 컴포넌트를 제거하여 3D Mesh 구조를 정리
2. 스캔 결과의 빈 공간을 재구성

- Denoiser
1. 표면 스무딩 및 노이즈 제거하여
2. RANSAC 기반 바닥 및 벽 정렬

---
### 🎨 **Unity 정합 툴**
스캔 데이터와 기존 모델을 정합하여 디지털 트윈 구축할 수 있습니다.
스캔한 모델과 메모를 함께 불러와 배치하거나 데이터 후처리 전/후를 스위칭할 수 있습니다.
또한 배치 결과를 저장하여 활용할 수 있습니다.


## 🏗️ 프로젝트 구조

```
S13P31S102/
├── App/                   # Android ARCore 스캐너 앱
│   ├── scanner/           # Android 애플리케이션
│   └── common/            # 공통 라이브러리 (C++/Java)
│
├── Retouch/               # 후처리 파이프라인 서버
│   ├── app/               # FastAPI 서버
│   ├── ai_pipeline/          # 데이터 후처리 파이프라인
│   │   ├── DeepMeshPrior/ # AI 보정 실험 폴더
│   │   ├── Polygon/       # 메시 최적화
│   │   └── Texture/       # 텍스처 정합
│   └── requirements/      # Python 의존성
│
├── BackEnd/               # 백엔드 API 서버
│   ├── app/               # FastAPI 애플리케이션
│   │   ├── models/        # 데이터베이스 모델
│   │   ├── routers/       # API 라우터
│   │   ├── services/      # 비즈니스 로직
│   │   └── schemas/       # Pydantic 스키마
│   └── docker-compose.yml # Docker 설정
│
├── admin/                 # 관리자 웹 대시보드
│   └── my-app/           # React + Vite 프론트엔드
│
├── Tool/                  # Unity 정합 툴
│   └── Assets/           # Unity 프로젝트
│
├── exec/                  # 실행 문서 및 시나리오
│   ├── 1. 환경세팅.md
│   └── 4. 시연 시나리오.md
│
└── storage/               # 스캔 데이터 저장소
    ├── uploads/          # 업로드된 원본 데이터
    └── outputs/          # 처리된 결과 데이터
```

## 🚀 빠른 시작

### 1. BackEnd 서버 실행

#### 필수 요구사항
- Docker 설치 필요
- PostgreSQL 데이터베이스

#### 환경 설정
```bash
cd BackEnd
# .env 파일 생성 (BackEnd/app/.env)
POSTGRES_DB=fastapi_db
POSTGRES_USER=postgres
POSTGRES_PASSWORD=password
POSTGRES_HOST=localhost
POSTGRES_PORT=5432
TIMEZONE_OFFSET=9
```

#### 실행
```bash
# Docker Compose로 전체 스택 실행
docker-compose up --build -d

# 로그 확인
docker-compose logs -f

# 개발 모드 (로컬 실행)
python -m venv venv
venv\Scripts\activate  # Windows
pip install -r requirements.txt
docker-compose up postgres -d
python -m app.main
```

**기본 엔드포인트**: `http://localhost:8000`
- API 문서: `http://localhost:8000/api/v1/docs`
- Health Check: `http://localhost:8000/health`

### 2. 후처리 파이프라인 서버 실행

#### 필수 요구사항
- Python 3.10+
- Blender 4.5 LTS+
- NVIDIA GPU + CUDA 빌드 PyTorch
- Windows 11 + WSL 또는 Linux

#### 환경 설정
```bash
cd Retouch
# retouch.env 파일 생성
BACKEND_WEBSOCKET_URL=ws://localhost:8000/ws
NETWORK_STORAGE_BASE=(공유폴더 주소)
UPLOADS_DIRECTORY=(uploads 폴더 주소)
OUTPUTS_DIRECTORY=(outputs 폴더 주소)
BLENDER_EXECUTABLE=(Blender.exe 경로)
HOST=0.0.0.0
PORT=8001
```

#### 실행
```bash
# 가상 환경 생성 및 의존성 설치
# Windows PowerShell
.\install_requirements.bat

# 또는 수동 설치
python -m venv venv
venv\Scripts\activate
pip install -r requirements/requirements.txt
pip install -r requirements/requirements-ai-inpaint.txt
pip install -r requirements/requirements-pytorch.txt
pip install -r requirements/requirements-pyg.txt

# 서버 실행
python -m app.main
```

### 3. Admin 대시보드 실행

#### 필수 요구사항
- Node.js 18+
- npm 또는 yarn

#### 실행
```bash
cd admin/my-app
npm install
npm run dev
```

**접속**: `http://localhost:5173`

### 4. Android 앱 빌드

#### 필수 요구사항
- Android Studio
- Android 7.0 이상 (API 24+)
- Google Play AR 서비스 설치 필요

#### 빌드
```bash
cd App/scanner
# Android Studio에서 프로젝트 열기
```

## 📱 사용 방법

### 1. 3D 스캔 프로세스

1. **Android 앱 실행**: SnapSpace 앱을 실행하고 스캔할 공간을 선택
2. **실시간 스캔**: ARCore를 사용하여 공간을 실시간으로 스캔
3. **데이터 업로드**: 스캔 완료 후 서버로 데이터 전송
4. **데이터 후처리**: 자동으로 메시 최적화, 노이즈 제거, 텍스처 정합 수행
5. **결과 확인**: 관리자 대시보드에서 처리 결과 확인

### 2. Unity 정합 툴 사용

1. Unity에서 `Tool` 프로젝트 열기
2. `Tools > OBJ Drop Watcher` 메뉴 실행
3. WatchConfig 에셋 생성 및 설정
4. 서버에서 그룹/스캔 데이터 조회
5. 스캔 데이터 Import 및 Prefab 생성
6. 기존 모델과 정합 작업 수행

자세한 사용법은 `Tool/README.md` 참조

## 🔧 기술 스택

### Backend
- **FastAPI**: RESTful API 및 WebSocket 서버
- **PostgreSQL**: 데이터베이스
- **SQLAlchemy**: ORM
- **Docker**: 컨테이너화

### Retouch
- **PyTorch**: 딥러닝 프레임워크 (실험용)
- **Open3D**: 3D 데이터 처리
- **Blender**: 3D 모델링 및 렌더링
- **Diffusers**: AI 인페인팅

### Mobile
- **ARCore**: AR 기능
- **C++/Java**: 네이티브 코드

### Tools
- **Unity**: 3D 정합 툴

## 📚 상세 문서

각 모듈별 상세 문서는 다음을 참조하세요:

- [BackEnd README](./BackEnd/README.md) - 백엔드 API 서버
- [Retouch README](./Retouch/README.md) - Retouch 파이프라인 서버
- [App README](./App/README.md) - Android 앱
- [Admin README](./admin/my-app/README.md) - 관리자 대시보드
- [Tool README](./Tool/README.md) - Unity 정합 툴

## 🔄 Git Commit Convention

### 세팅 활성화
```bash
# Windows
setting.bat 파일 실행 후 'y' 입력
```

### 커밋 템플릿 확인
```bash
git config --get commit.template
# -> .gitlab/.gitmessage.txt
```

### 커밋 방법
```bash
git commit
# 띄워지는 창에서 커밋 메시지 입력 후 저장
# 꼬리말에 지라 이슈번호 입력 시 자동 연결
```

## 🗂️ 데이터 구조

### 스토리지 구조
```
storage/
├── uploads/              # 업로드된 원본 스캔 데이터
│   └── {group_name}/    # 그룹별 폴더
│       └── {scan_id}.db # 스캔 데이터베이스
│
└── outputs/             # AI 처리 결과
    ├── optimized/       # 최적화된 메시
    └── final/           # 최종 처리 결과
```
