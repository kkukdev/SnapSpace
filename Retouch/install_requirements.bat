@echo off
echo ========================================
echo AI 환경 설치 시작
echo ========================================

echo 가상 환경 생성 중...
py -3.11 -m venv venv
if %errorlevel% neq 0 (
    echo ERROR: 가상 환경 생성 실패
    pause
    exit /b 1
)
echo 가상 환경 생성 완료!

echo.
echo 가상 환경 활성화 중...
call venv\Scripts\activate.bat
if %errorlevel% neq 0 (
    echo ERROR: 가상 환경 활성화 실패
    pause
    exit /b 1
)
echo 가상 환경 활성화 완료!

echo.
echo pip upgrade 중...
python.exe -m pip install --upgrade pip
if %errorlevel% neq 0 (
    echo ERROR: pip upgrade 실패
    pause
    exit /b 1
)
echo pip upgrade 완료!

echo.
echo [1/3] 기본 패키지 설치 중...
pip install -r requirements/requirements.txt
if %errorlevel% neq 0 (
    echo ERROR: 기본 패키지 설치 실패
    pause
    exit /b 1
)
echo 기본 패키지 설치 완료!

echo.
echo [2/3] PyTorch CUDA 12.4 설치 중...
pip install -r requirements/requirements-pytorch.txt
if %errorlevel% neq 0 (
    echo ERROR: PyTorch 설치 실패
    pause
    exit /b 1
)
echo PyTorch 설치 완료!

echo.
echo [3/3] PyTorch Geometric 설치 중...
pip install -r requirements/requirements-pyg.txt
if %errorlevel% neq 0 (
    echo ERROR: PyTorch Geometric 설치 실패
    pause
    exit /b 1
)
echo PyTorch Geometric 설치 완료!

echo.
echo ========================================
echo 설치 완료! CUDA 인식 확인 중...
echo ========================================
python -c "import torch; print(f'PyTorch version: {torch.__version__}'); print(f'CUDA available: {torch.cuda.is_available()}'); print(f'CUDA version: {torch.version.cuda}')"

echo.
echo 설치가 완료되었습니다!
pause
