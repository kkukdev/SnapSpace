# 🚀 SSAFY Digital Twin - Admin Panel

BackEnd API를 테스트할 수 있는 관리자용 웹페이지입니다.

## 📋 기능

### 🏥 Health Check
- 서비스 상태 확인 (`GET /health`)
- 서비스 준비 상태 확인 (`GET /ready`)

### 👥 Groups Management
- **조회**
  - 전체 그룹 목록 조회 (페이징)
  - 특정 그룹 조회
  - 그룹의 스캔 목록 조회
- **생성/수정/삭제**
  - 새 그룹 생성 (JSON 메타데이터 포함)
  - 그룹 정보 수정
  - 그룹 삭제

### 📷 Scans Management
- **조회**
  - 전체 스캔 목록 조회 (페이징)
  - 특정 스캔 조회
- **생성/수정/삭제**
  - 새 스캔 생성 (그룹 ID, 메타데이터, 상태 설정)
  - 스캔 정보 수정
  - 스캔 삭제

### 📁 File Upload
- 업로드 서비스 상태 확인
- 3D 모델 파일 업로드 (.ply, .obj, .stl)

## 🛠️ 설치 및 실행

### 1. 의존성 설치
```bash
cd admin/my-app
npm install
```

### 2. 개발 서버 실행
```bash
npm run dev
```

서버가 `http://localhost:5173`에서 실행됩니다.

### 3. 백엔드 서버 확인
백엔드 서버가 `http://localhost:8000`에서 실행되고 있는지 확인하세요.

## 🎯 사용 방법

1. **브라우저에서 접속**: `http://localhost:5173`
2. **탭 선택**: Health Check, Groups, Scans, Upload 중 하나 선택
3. **API 테스트**: 
   - 단순 조회: 버튼 클릭만으로 실행
   - 데이터 생성/수정: 폼 필드 입력 후 버튼 클릭
   - 파일 업로드: 파일 선택 후 업로드 버튼 클릭
4. **결과 확인**: 각 요청의 응답이 실시간으로 표시됩니다.

## 🎨 UI 특징

- **모던 디자인**: 그라데이션 배경과 글래스모피즘 효과
- **반응형**: 모바일 및 태블릿 지원
- **실시간 피드백**: API 응답을 즉시 확인
- **구분된 상태**: 성공(초록)/실패(빨강) 색상으로 구분
- **JSON 포맷팅**: 응답 데이터가 가독성 있게 표시

## 🔧 기술 스택

- **Frontend**: React 19 + Vite
- **HTTP Client**: Axios
- **Styling**: CSS3 (Flexbox, Grid, CSS Variables)
- **Development**: ESLint, Hot Module Replacement

## 📝 API 엔드포인트

### Health Check
- `GET /health` - 서비스 헬스체크
- `GET /ready` - 서비스 준비 상태

### Groups
- `GET /api/v1/groups/` - 그룹 목록 조회
- `POST /api/v1/groups/` - 그룹 생성
- `GET /api/v1/groups/{id}` - 그룹 조회
- `PUT /api/v1/groups/{id}` - 그룹 수정
- `DELETE /api/v1/groups/{id}` - 그룹 삭제
- `GET /api/v1/groups/{id}/scans` - 그룹의 스캔 목록

### Scans  
- `GET /api/v1/scans/` - 스캔 목록 조회
- `POST /api/v1/scans/` - 스캔 생성
- `GET /api/v1/scans/{id}` - 스캔 조회
- `PUT /api/v1/scans/{id}` - 스캔 수정
- `DELETE /api/v1/scans/{id}` - 스캔 삭제

### Upload
- `GET /api/v1/upload/` - 업로드 상태 확인
- `POST /api/v1/upload/` - 파일 업로드

## 🔍 문제 해결

### CORS 에러가 발생하는 경우
백엔드 서버에서 CORS 설정이 되어있는지 확인하세요.

### 연결이 안 되는 경우
1. 백엔드 서버가 실행되고 있는지 확인 (`http://localhost:8000`)
2. 방화벽 설정 확인
3. 네트워크 연결 상태 확인

### 파일 업로드가 안 되는 경우
1. 파일 크기 제한 확인 (150-200MB)
2. 지원되는 파일 형식 확인 (.ply, .obj, .stl)
3. 서버 업로드 디렉토리 권한 확인