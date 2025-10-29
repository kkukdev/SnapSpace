## 서버 실행 방법

### ai.env 파일 생성
AI 서버 환경 변수 세팅을 위해 .env 파일을 생성해주세요.
```bash
# ai.env 파일 작성 양식
BACKEND_WEBSOCKET_URL=ws://localhost:8000/ws
WEBSOCKET_PING_INTERVAL=30
WEBSOCKET_PING_TIMEOUT=10
WEBSOCKET_RECONNECT_INTERVAL=5
WEBSOCKET_MAX_RECONNECT_ATTEMPTS=10

UPLOADS_DIRECTORY=storage/uploads
OUTPUTS_DIRECTORY=storage/outputs
MAX_CONCURRENT_TASKS=3
MAX_RETRY_ATTEMPTS=3
PROCESSING_TIMEOUT=3600

LOG_LEVEL=INFO
LOG_FILE=logs/ai_server.log

HOST=0.0.0.0
PORT=8000
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