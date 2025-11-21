#!/bin/bash

echo "========================================"
echo "AI 환경 설치 시작"
echo "========================================"

echo ""
echo "가상 환경 생성 중..."
py -3.11 -m venv venv
if [ $? -ne 0 ]; then
    echo "ERROR: 가상 환경 생성 실패"
    exit 1
fi
echo "가상 환경 생성 완료!"

echo ""
echo "가상 환경 활성화 중..."
source venv/Scripts/activate
if [ $? -ne 0 ]; then
    echo "ERROR: 가상 환경 활성화 실패"
    exit 1
fi
echo "가상 환경 활성화 완료!"

echo ""
echo "pip upgrade 중..."
python.exe -m pip install --upgrade pip
if [ $? -ne 0 ]; then
    echo "ERROR: pip upgrade 실패"
    exit 1
fi
echo "pip upgrade 완료!"

echo ""
echo "[1/3] 기본 패키지 설치 중..."
pip install -r requirements/requirements.txt
if [ $? -ne 0 ]; then
    echo "ERROR: 기본 패키지 설치 실패"
    exit 1
fi
echo "기본 패키지 설치 완료!"

echo ""
echo "[2/3] PyTorch CUDA 12.4 설치 중..."
pip install -r requirements/requirements-pytorch.txt
if [ $? -ne 0 ]; then
    echo "ERROR: PyTorch 설치 실패"
    exit 1
fi
echo "PyTorch 설치 완료!"

echo ""
echo "[3/3] PyTorch Geometric 설치 중..."
pip install -r requirements/requirements-pyg.txt
if [ $? -ne 0 ]; then
    echo "ERROR: PyTorch Geometric 설치 실패"
    exit 1
fi
echo "PyTorch Geometric 설치 완료!"

echo ""
echo "========================================"
echo "설치 완료! CUDA 인식 확인 중..."
echo "========================================"
python -c "import torch; print(f'PyTorch version: {torch.__version__}'); print(f'CUDA available: {torch.cuda.is_available()}'); print(f'CUDA version: {torch.version.cuda}')"

echo ""
echo "설치가 완료되었습니다!"
