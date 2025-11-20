from fastapi.testclient import TestClient
from app.main import app

client = TestClient(app)


def test_root():
    """루트 엔드포인트 테스트"""
    response = client.get("/api/v1/")
    assert response.status_code == 200
    assert response.json()["message"] == "Hello World"


def test_health():
    """헬스체크 엔드포인트 테스트"""
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["message"] == "Service is healthy"


def test_ready():
    """준비 상태 체크 엔드포인트 테스트"""
    response = client.get("/ready")
    assert response.status_code == 200
    assert response.json()["message"] == "Service is ready"
