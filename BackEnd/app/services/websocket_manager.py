import asyncio
import json
import logging
from typing import List, Dict, Any, Optional
from fastapi import WebSocket
from datetime import datetime

from app.schemas.websocket_schemas import (
    WSMessageType, FileListMessage, ProcessingStatusUpdate,
    ProcessingStartMessage, ProcessingProgressMessage,
    ProcessingCompleteMessage, ProcessingErrorMessage,
    HeartbeatMessage, ConnectionStatusMessage
)

logger = logging.getLogger(__name__)

class WebSocketManager:
    def __init__(self):
        self.ai_connections: List[WebSocket] = []
        self.connection_info: Dict[WebSocket, Dict[str, Any]] = {}
    
    async def connect(self, websocket: WebSocket, client_id: str = None):
        """AI 서버 연결 수락"""
        await websocket.accept()
        
        if client_id is None:
            client_id = f"ai_{datetime.now().strftime('%Y%m%d_%H%M%S')}"
        
        self.ai_connections.append(websocket)
        self.connection_info[websocket] = {
            "client_id": client_id,
            "connected_at": datetime.now(),
            "last_heartbeat": datetime.now()
        }
        
        logger.info(f"AI server connected: {client_id}")
        return client_id
    
    def disconnect(self, websocket: WebSocket):
        """AI 서버 연결 해제"""
        if websocket in self.ai_connections:
            self.ai_connections.remove(websocket)
        
        if websocket in self.connection_info:
            client_id = self.connection_info[websocket]["client_id"]
            del self.connection_info[websocket]
            logger.info(f"AI server disconnected: {client_id}")
    
    async def send_to_ai(self, message: dict, websocket: WebSocket = None):
        """AI 서버에게 메시지 전송"""
        if websocket:
            # 특정 AI 서버에게 전송
            try:
                await websocket.send_text(json.dumps(message, default=str))
                logger.debug(f"Message sent to AI: {message}")
            except Exception as e:
                logger.error(f"Failed to send message to specific AI: {e}")
        else:
            # 모든 AI 서버에게 전송
            for connection in self.ai_connections:
                try:
                    await connection.send_text(json.dumps(message, default=str))
                    logger.debug(f"Message sent to AI: {message}")
                except Exception as e:
                    logger.error(f"Failed to send message to AI: {e}")
    
    async def send_file_list(self, files: List[dict], websocket: WebSocket = None):
        """AI 서버에게 파일 목록 전송"""
        message = {
            "type": "file_list",
            "files": files
        }
        await self.send_to_ai(message, websocket)
    
    async def broadcast_to_clients(self, message: dict):
        """모든 클라이언트(Postman 등)에게 메시지 브로드캐스트"""
        for connection in self.ai_connections:  # 모든 연결에 전송
            try:
                await connection.send_text(json.dumps(message, default=str))
                logger.debug(f"Broadcasted message: {message}")
            except Exception as e:
                logger.error(f"Failed to broadcast message: {e}")

    async def handle_message(self, websocket: WebSocket, message_data: dict):
        """AI 서버로부터 받은 메시지 처리"""
        try:
            message_type = message_data.get("type")
            
            # Postman에게 메시지 브로드캐스트
            # await self.broadcast_to_clients(message_data)

            if message_type == WSMessageType.PROCESSING_START:
                await self._handle_processing_start(websocket, message_data)
            elif message_type == WSMessageType.PROCESSING_PROGRESS:
                await self._handle_processing_progress(websocket, message_data)
            elif message_type == WSMessageType.PROCESSING_COMPLETE:
                await self._handle_processing_complete(websocket, message_data)
            elif message_type == WSMessageType.PROCESSING_ERROR:
                await self._handle_processing_error(websocket, message_data)
            elif message_type == WSMessageType.HEARTBEAT:
                await self._handle_heartbeat(websocket, message_data)
            elif message_type == WSMessageType.CONNECTION_STATUS:
                await self._handle_connection_status(websocket, message_data)
            else:
                logger.warning(f"Unknown message type: {message_type}")
                
        except Exception as e:
            logger.error(f"Error handling message: {e}")
    
    async def _handle_processing_start(self, websocket: WebSocket, message_data: dict):
        """처리 시작 처리"""
        scan_id = message_data.get("scan_id")
        logger.info(f"AI started processing scan_id: {scan_id}")
        # TODO: DB 상태 업데이트 (PROCESSING)
    
    async def _handle_processing_progress(self, websocket: WebSocket, message_data: dict):
        """처리 진행률 업데이트"""
        scan_id = message_data.get("scan_id")
        progress = message_data.get("progress")
        logger.info(f"AI processing progress for scan_id {scan_id}: {progress}%")
        # TODO: DB 상태 업데이트 (진행률)
    
    async def _handle_processing_complete(self, websocket: WebSocket, message_data: dict):
        """처리 완료 처리"""
        scan_id = message_data.get("scan_id")
        output_file_path = message_data.get("output_file_path")
        logger.info(f"AI completed processing scan_id {scan_id}: {output_file_path}")
        # TODO: DB 상태 업데이트 (COMPLETED)
    
    async def _handle_processing_error(self, websocket: WebSocket, message_data: dict):
        """처리 에러 처리"""
        scan_id = message_data.get("scan_id")
        error_message = message_data.get("error_message")
        logger.error(f"AI processing error for scan_id {scan_id}: {error_message}")
        # TODO: DB 상태 업데이트 (ERROR)
    
    async def _handle_heartbeat(self, websocket: WebSocket, message_data: dict):
        """하트비트 응답"""
        if websocket in self.connection_info:
            self.connection_info[websocket]["last_heartbeat"] = datetime.now()
        logger.debug("Received heartbeat from AI")
    
    async def _handle_connection_status(self, websocket: WebSocket, message_data: dict):
        """연결 상태 처리"""
        status = message_data.get("status")
        logger.info(f"AI connection status: {status}")
    
    def get_connection_count(self) -> int:
        """연결된 AI 서버 수 반환"""
        return len(self.ai_connections)
    
    def get_connection_info(self) -> List[dict]:
        """연결된 AI 서버 정보 반환"""
        return [
            {
                "client_id": info["client_id"],
                "connected_at": info["connected_at"].isoformat(),
                "last_heartbeat": info["last_heartbeat"].isoformat()
            }
            for info in self.connection_info.values()
        ]

# 전역 WebSocket 관리자 인스턴스
websocket_manager = WebSocketManager()