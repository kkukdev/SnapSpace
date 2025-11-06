import asyncio
import json
import logging
from typing import List, Dict, Any, Optional
from fastapi import WebSocket
from datetime import datetime

from app.schemas.websocket_schemas import WSMessageType, FileListMessage
from app.config import get_current_datetime

logger = logging.getLogger(__name__)

class WebSocketManager:
    def __init__(self):
        self.ai_connections: List[WebSocket] = []
        self.connection_info: Dict[WebSocket, Dict[str, Any]] = {}
    
    async def connect(self, websocket: WebSocket, client_id: str = None):
        """AI 서버 연결 수락"""
        await websocket.accept()
        
        if client_id is None:
            client_id = f"ai_{get_current_datetime().strftime('%Y%m%d_%H%M%S')}"
        
        self.ai_connections.append(websocket)
        self.connection_info[websocket] = {
            "client_id": client_id,
            "connected_at": get_current_datetime(),
            "last_heartbeat": get_current_datetime()
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
    
    async def send_to_ai(self, message: Any, websocket: WebSocket = None):
        """AI 서버에게 메시지 전송"""
        # Pydantic 모델을 dict로 변환
        if hasattr(message, 'dict'):
            message_data = message.dict()
        elif hasattr(message, 'model_dump'):  # Pydantic v2
            message_data = message.model_dump()
        else:
            message_data = message

        if websocket:
            # 특정 AI 서버에게 전송
            try:
                await websocket.send_text(json.dumps(message_data, default=str))
                logger.debug(f"Message sent to AI: {message_data}")
            except Exception as e:
                logger.error(f"Failed to send message to specific AI: {e}")
        else:
            # 모든 AI 서버에게 전송
            for connection in self.ai_connections:
                try:
                    await connection.send_text(json.dumps(message_data, default=str))
                    logger.debug(f"Message sent to AI: {message_data}")
                except Exception as e:
                    logger.error(f"Failed to send message to AI: {e}")
    
    async def send_file_list(self, files: List[dict], websocket: WebSocket = None):
        """AI 서버에게 파일 목록 전송"""
        message = FileListMessage(files=files)
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
        
        # retouched_file_path 업데이트 및 status를 COMPLETED로 변경
        if scan_id and output_file_path:
            try:
                from app.database import SessionLocal
                from app.services.scan_service import scan_service
                from app.config import settings
                from pathlib import Path
                import re
                import os
                
                db = SessionLocal()
                try:
                    # 경로 변환: 네트워크 경로를 /project_root/storage 형식으로 변환
                    # upload_service.py와 config.py의 로직을 참고하여 처리
                    converted_path = output_file_path
                    
                    # 경로 정규화 (백슬래시를 슬래시로)
                    normalized = output_file_path.replace("\\", "/")
                    
                    # 1. 이미 /project_root로 시작하는 경우 그대로 사용
                    if normalized.startswith("/project_root"):
                        converted_path = normalized
                    
                    # 2. UNC 경로 처리: \\host\storage\... -> /project_root/storage/...
                    elif normalized.startswith("//") or normalized.startswith("\\\\"):
                        # //host/storage/... 또는 //host/storage 형식 처리
                        # storage 부분 찾기
                        if "/storage/" in normalized:
                            storage_idx = normalized.find("/storage/")
                            relative_path = normalized[storage_idx:]  # /storage/... 부터
                            converted_path = f"/project_root{relative_path}"
                        else:
                            # storage가 포함된 경우 찾기
                            parts = [p for p in normalized.strip("/").split("/") if p]
                            if "storage" in parts:
                                storage_idx = parts.index("storage")
                                relative_path = "/" + "/".join(parts[storage_idx:])
                                converted_path = f"/project_root{relative_path}"
                    
                    # 3. Windows 드라이브 경로 처리: C:\host\storage\... -> /project_root/storage/...
                    elif re.match(r'^[A-Z]:/', normalized):
                        # C:/host/storage/... 형식에서 storage 부분 추출
                        if "/storage/" in normalized:
                            storage_idx = normalized.find("/storage/")
                            relative_path = normalized[storage_idx:]  # /storage/... 부터
                            converted_path = f"/project_root{relative_path}"
                        else:
                            parts = [p for p in normalized.split("/") if p and p != ""]
                            if "storage" in parts:
                                storage_idx = parts.index("storage")
                                relative_path = "/" + "/".join(parts[storage_idx:])
                                converted_path = f"/project_root{relative_path}"
                    
                    # 4. 상대 경로나 storage로 시작하는 경우
                    elif normalized.startswith("storage/"):
                        converted_path = f"/project_root/{normalized}"
                    
                    # 5. 기타 경로에서 storage 찾기
                    else:
                        # storage가 포함된 경우 /project_root/storage/...로 변환
                        if "/storage/" in normalized:
                            storage_idx = normalized.find("/storage/")
                            relative_path = normalized[storage_idx:]  # /storage/... 부터
                            converted_path = f"/project_root{relative_path}"
                        elif normalized.startswith("storage"):
                            converted_path = f"/project_root/{normalized}"
                    
                    # 최종 정규화: 중복 슬래시 제거
                    converted_path = re.sub(r'/+', '/', converted_path)
                    # Windows 경로 구분자 남은 것 정리
                    converted_path = converted_path.replace("\\", "/")
                    
                    logger.info(f"Converting path: {output_file_path} -> {converted_path}")
                    
                    # retouched_file_path 업데이트
                    result = scan_service.update_scan_retouched_file_path(
                        db=db, 
                        scan_id=scan_id, 
                        retouched_file_path=converted_path
                    )
                    if result.success:
                        logger.info(f"Successfully updated retouched_file_path for scan_id={scan_id}: {converted_path}")
                    else:
                        logger.warning(f"Failed to update retouched_file_path for scan_id={scan_id}: {result.message}")
                    
                    # status를 COMPLETED로 업데이트
                    status_result = scan_service.update_scan_status(
                        db=db,
                        scan_id=scan_id,
                        status="COMPLETED"
                    )
                    if status_result.success:
                        logger.info(f"Successfully updated status to COMPLETED for scan_id={scan_id}")
                    else:
                        logger.warning(f"Failed to update status for scan_id={scan_id}: {status_result.message}")
                        
                finally:
                    db.close()
            except Exception as e:
                logger.error(f"Error updating retouched_file_path and status for scan_id={scan_id}: {str(e)}", exc_info=True)
    
    async def _handle_processing_error(self, websocket: WebSocket, message_data: dict):
        """처리 에러 처리"""
        scan_id = message_data.get("scan_id")
        error_message = message_data.get("error_message")
        logger.error(f"AI processing error for scan_id {scan_id}: {error_message}")
        # TODO: DB 상태 업데이트 (ERROR)
    
    async def _handle_heartbeat(self, websocket: WebSocket, message_data: dict):
        """하트비트 응답"""
        if websocket in self.connection_info:
            self.connection_info[websocket]["last_heartbeat"] = get_current_datetime()
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