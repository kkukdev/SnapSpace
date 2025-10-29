import asyncio
import json
import logging
from typing import Optional, Dict, Any
from websockets import connect, WebSocketServerProtocol
from websockets.exceptions import ConnectionClosed, WebSocketException

from app.config import settings
from app.schemas.websocket_schemas import (
    WSMessageType, FileListMessage, ProcessingStatusUpdate,
    ProcessingStartMessage, ProcessingProgressMessage, 
    ProcessingCompleteMessage, ProcessingErrorMessage,
    HeartbeatMessage, ConnectionStatusMessage
)

logger = logging.getLogger(__name__)

class WebSocketManager:
    def __init__(self):
        self.connection: Optional[WebSocketServerProtocol] = None
        self.is_connected = False
        self.reconnect_attempts = 0
        self.max_reconnect_attempts = settings.websocket_max_reconnect_attempts
        self.reconnect_interval = settings.websocket_reconnect_interval
        self._message_loop_task: Optional[asyncio.Task] = None
        
    async def connect_to_backend(self) -> bool:
        """백엔드 서버에 WebSocket 연결"""
        try:
            self.connection = await connect(
                settings.backend_websocket_url,
                ping_interval=settings.websocket_ping_interval,
                ping_timeout=settings.websocket_ping_timeout
            )
            self.is_connected = True
            self.reconnect_attempts = 0
            
            # 연결 상태 알림
            await self.send_message(ConnectionStatusMessage(status="connected"))
            
            # 메시지 수신 루프 시작
            self._message_loop_task = asyncio.create_task(self.start_message_loop())
            
            logger.info(f"WebSocket connected to {settings.backend_websocket_url}")
            return True
            
        except Exception as e:
            logger.error(f"Failed to connect to backend: {e}")
            return False
    
    async def disconnect(self):
        """WebSocket 연결 종료"""
        if self._message_loop_task:
            self._message_loop_task.cancel()
            try:
                await self._message_loop_task
            except asyncio.CancelledError:
                pass
        
        if self.connection:
            await self.connection.close()
            self.is_connected = False
            logger.info("WebSocket disconnected")
    
    async def start_message_loop(self):
        """메시지 수신 루프 시작"""
        if not self.is_connected or not self.connection:
            logger.warning("WebSocket not connected, cannot start message loop")
            return
        
        try:
            async for message in self.connection:
                try:
                    message_data = json.loads(message)
                    await self.handle_message(message_data)
                except json.JSONDecodeError as e:
                    logger.error(f"Failed to parse message: {e}")
                except Exception as e:
                    logger.error(f"Error processing message: {e}")
        except ConnectionClosed:
            logger.warning("WebSocket connection closed")
            self.is_connected = False
            # 재연결 시도
            asyncio.create_task(self.reconnect())
        except Exception as e:
            logger.error(f"Message loop error: {e}")
            self.is_connected = False
            # 재연결 시도
            asyncio.create_task(self.reconnect())
    
    async def send_message(self, message: Any) -> bool:
        """백엔드 서버에 메시지 전송"""
        if not self.is_connected or not self.connection:
            logger.warning("WebSocket not connected, cannot send message")
            return False
        
        try:
            if hasattr(message, 'dict'):
                message_data = message.dict()
            else:
                message_data = message
                
            await self.connection.send(json.dumps(message_data, default=str))
            logger.debug(f"Message sent: {message_data}")
            return True
            
        except (ConnectionClosed, WebSocketException) as e:
            logger.error(f"Failed to send message: {e}")
            self.is_connected = False
            return False
    
    async def send_processing_start(self, scan_id: int) -> bool:
        """처리 시작 알림 전송"""
        message = ProcessingStartMessage(scan_id=scan_id)
        return await self.send_message(message)
    
    async def send_processing_progress(self, scan_id: int, progress: int) -> bool:
        """처리 진행률 업데이트 전송"""
        message = ProcessingProgressMessage(scan_id=scan_id, progress=progress)
        return await self.send_message(message)
    
    async def send_processing_complete(self, scan_id: int, output_file_path: str) -> bool:
        """처리 완료 알림 전송"""
        message = ProcessingCompleteMessage(scan_id=scan_id, output_file_path=output_file_path)
        return await self.send_message(message)
    
    async def send_processing_error(self, scan_id: int, error_message: str) -> bool:
        """처리 에러 알림 전송"""
        message = ProcessingErrorMessage(scan_id=scan_id, error_message=error_message)
        return await self.send_message(message)
    
    async def send_heartbeat(self) -> bool:
        """하트비트 전송"""
        message = HeartbeatMessage()
        return await self.send_message(message)
    
    async def handle_message(self, message_data: Dict[str, Any]):
        """받은 메시지 처리"""
        try:
            message_type = message_data.get("type")
            
            if message_type == WSMessageType.FILE_LIST:
                await self._handle_file_list(message_data)
            elif message_type == WSMessageType.HEARTBEAT:
                await self._handle_heartbeat(message_data)
            else:
                logger.warning(f"Unknown message type: {message_type}")
                
        except Exception as e:
            logger.error(f"Error handling message: {e}")
    
    async def _handle_file_list(self, message_data: Dict[str, Any]):
        """파일 목록 처리"""
        try:
            file_list = FileListMessage(**message_data)
            logger.info(f"Received file list with {len(file_list.files)} files")
            
            # TODO: 파일 처리 로직 구현 (4단계에서)
            for file_request in file_list.files:
                logger.info(f"Processing file: {file_request.scan_id}")
                # 여기서 실제 파일 처리 로직이 들어갈 예정
                
        except Exception as e:
            logger.error(f"Error handling file list: {e}")
    
    async def _handle_heartbeat(self, message_data: Dict[str, Any]):
        """하트비트 응답"""
        logger.debug("Received heartbeat from backend")
        # 필요시 응답 전송
    
    async def start_heartbeat(self):
        """하트비트 시작"""
        while self.is_connected:
            try:
                await self.send_heartbeat()
                await asyncio.sleep(settings.websocket_ping_interval)
            except Exception as e:
                logger.error(f"Heartbeat error: {e}")
                break
    
    async def reconnect(self) -> bool:
        """재연결 시도"""
        if self.reconnect_attempts >= self.max_reconnect_attempts:
            logger.error("Max reconnection attempts reached")
            return False
        
        self.reconnect_attempts += 1
        logger.info(f"Attempting to reconnect ({self.reconnect_attempts}/{self.max_reconnect_attempts})")
        
        await asyncio.sleep(self.reconnect_interval)
        return await self.connect_to_backend()

# 전역 WebSocket 관리자 인스턴스
websocket_manager = WebSocketManager()