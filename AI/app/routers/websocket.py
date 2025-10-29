from fastapi import APIRouter, HTTPException
from typing import List
from app.services.websocket_manager import websocket_manager
from datetime import datetime


router = APIRouter()

@router.get("/status")
async def websocket_status():
    """WebSocket 연결 상태 확인"""
    return {
        "connected": websocket_manager.is_connected,
        "reconnect_attempts": websocket_manager.reconnect_attempts
    }

@router.post("/test")
async def test_send_message():
    """Backend 서버에게 테스트 메시지 전송"""
    if not websocket_manager.is_connected:
        raise HTTPException(status_code=503, detail="Not connected to Backend server")
    
    test_message = {
        "type": "test",
        "message": "Hello from AI server!",
        "timestamp": datetime.now().isoformat()
    }
    
    await websocket_manager.send_message(test_message)
    return {"message": "Test message sent to Backend server"}