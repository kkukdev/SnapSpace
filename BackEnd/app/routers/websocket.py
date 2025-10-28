from fastapi import APIRouter, WebSocket, WebSocketDisconnect, HTTPException
from typing import List
import json
import logging
from datetime import datetime

from app.services.websocket_manager import websocket_manager
from app.schemas.websocket_schemas import FileListMessage

logger = logging.getLogger(__name__)

router = APIRouter()

@router.websocket("")
async def websocket_endpoint(websocket: WebSocket):
    """AI 서버 WebSocket 엔드포인트"""
    client_id = None
    
    try:
        # AI 서버 연결 수락
        client_id = await websocket_manager.connect(websocket)
        
        while True:
            # AI 서버로부터 메시지 수신
            data = await websocket.receive_text()
            message_data = json.loads(data)
            
            # 메시지 처리
            await websocket_manager.handle_message(websocket, message_data)
            
    except WebSocketDisconnect:
        websocket_manager.disconnect(websocket)
        logger.info(f"AI server disconnected: {client_id}")
    except Exception as e:
        logger.error(f"WebSocket error: {e}")
        websocket_manager.disconnect(websocket)

@router.get("/status")
async def get_ai_connections():
    """연결된 AI 서버 상태 조회"""
    return {
        "connection_count": websocket_manager.get_connection_count(),
        "connections": websocket_manager.get_connection_info()
    }

@router.post("/send-files")
async def send_files_to_ai(files: List[dict]):
    """AI 서버에게 파일 목록 전송"""
    if websocket_manager.get_connection_count() == 0:
        raise HTTPException(status_code=503, detail="No AI servers connected")
    
    await websocket_manager.send_file_list(files)
    return {"message": f"File list sent to {websocket_manager.get_connection_count()} AI server(s)"}

@router.post("/test")
async def test_ai_connection():
    """AI 서버 연결 테스트"""
    if websocket_manager.get_connection_count() == 0:
        raise HTTPException(status_code=503, detail="Not connected to AI server")
    
    test_message = {
        "type": "test",
        "message": "Hello from Backend server!",
        "timestamp": datetime.now().isoformat()
    }
    
    await websocket_manager.send_to_ai(test_message)
    return {"message": "Test message sent to AI servers"}