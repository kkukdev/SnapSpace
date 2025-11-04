FROM python:3.11-slim

WORKDIR /app

# 시스템 패키지 설치
RUN apt-get update && apt-get install -y \
    gcc \
    g++ \
    libx11-6 \
    libxext6 \
    libxrender1 \
    libxtst6 \
    libxi6 \
    libxrandr2 \
    libxfixes3 \
    libgl1 \
    libglx0 \
    libglvnd0 \
    libegl1 \
    libopengl0 \
    libgl1-mesa-dri \
    libglu1-mesa \
    libglib2.0-0 \
    libcom-err2 \
    zlib1g \
    libstdc++6 \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

# pip upgrade
RUN python -m pip install --upgrade pip

# Python 의존성 설치
COPY requirements/requirements.txt .
RUN pip install -r requirements.txt

# COPY requirements/requirements-pytorch.txt .
# RUN pip install -r requirements-pytorch.txt

# COPY requirements/requirements-pyg.txt .
# RUN pip install -r requirements-pyg.txt

# 애플리케이션 코드 복사
COPY app/ ./app/
COPY ai_pipeline/ ./ai_pipeline/

# 환경 변수 설정
ENV PYTHONPATH=/app

# 포트 노출
EXPOSE 8001

# 애플리케이션 실행
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8001"]