## 서버 실행 방법(개발 모드)

### ai.env 파일 생성
AI 서버 환경 변수 세팅을 위해 .env 파일을 생성해주세요.
```bash
# ai.env 파일 작성 양식(예시)
BACKEND_WEBSOCKET_URL=(웹소켓 URL)
WEBSOCKET_PING_INTERVAL=(핑 확인 주기)
WEBSOCKET_PING_TIMEOUT=(핑 타임아웃)
WEBSOCKET_RECONNECT_INTERVAL=(재연결 시도 대기 시간)
WEBSOCKET_MAX_RECONNECT_ATTEMPTS=(웹소켓 최대 연결 횟수)

NETWORK_STORAGE_BASE=(공유폴더 주소)
UPLOADS_DIRECTORY=(uploads 폴더 주소)
OUTPUTS_DIRECTORY=(outputs 폴더 주소)

LOCAL_TEMP_DIR='./temp'
MAX_CONCURRENT_TASKS=(파이프라인 동시 동작 최대 횟수)
MAX_RETRY_ATTEMPTS=(파이프라인 재시도 동작 횟수)
PROCESSING_TIMEOUT=(파이프라인 타임아웃 시간)

LOG_LEVEL=INFO
LOG_FILE=logs/ai_server.log

HOST=(AI 서버 호스트 IP주소)
PORT=(AI 서버 포트 번호)

BLENDER_EXECUTABLE=(Blender.exe 실행 파일 위치)
KEEP_TEXTURE_TEMP_ARTIFACTS=(파이프라인 작동 중에 temp 파일 보존 여부 true/false)
```

### 가상 환경 설정

가상 환경 생성, 필요한 패키지를 모두 설치해줍니다.

```bash
# shell 환경 (ex. git bash)
source install_requirements.sh

# prompt 환경 (ex. Powershell)
source install_requirements.bat
```

### 서버 실행

AI 폴더에 위치한 상태로 다음과 같이 명령어를 입력합니다.

```bash
python -m app.main
```