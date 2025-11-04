import asyncio
import os
import logging
from typing import Dict, Optional, List
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime
from pathlib import Path

from app.config import settings
from app.utils.path_utils import convert_to_network_path, normalize_network_path

logger = logging.getLogger(__name__)

class FileProcessor:
    def __init__(self):
        self.executor = ThreadPoolExecutor(max_workers=settings.max_concurrent_tasks)
        self.processing_tasks: Dict[int, asyncio.Task] = {}
        self.processing_status: Dict[int, Dict] = {}
    
    async def process_file(self, scan_id: int, file_path: str, group_id: str, metadata: Dict) -> bool:
        """파일 처리 시작"""
        try:
            # 처리 상태 초기화
            self.processing_status[scan_id] = {
                "status": "PROCESSING",
                "progress": 0,
                "started_at": datetime.now(),
                "file_path": file_path,
                "group_id": group_id,
                "metadata": metadata
            }
            
            from app.services.websocket_manager import websocket_manager
            # Backend에 처리 시작 알림
            await websocket_manager.send_processing_start(scan_id)
            
            # 비동기 파일 처리 시작
            task = asyncio.create_task(self._process_file_async(scan_id, file_path))
            self.processing_tasks[scan_id] = task
            
            return True
            
        except Exception as e:
            logger.error(f"Failed to start processing file {scan_id}: {e}")

            from app.services.websocket_manager import websocket_manager
            await websocket_manager.send_processing_error(scan_id, str(e))
            return False
    
    async def _process_file_async(self, scan_id: int, file_path: str):
        """비동기 파일 처리"""
        try:
            # 백엔드에서 받은 경로를 네트워크 경로로 변환
            network_file_path = convert_to_network_path(file_path, settings.network_storage_base)
            
            # 파일 존재 확인
            if not os.path.exists(network_file_path):
                raise FileNotFoundError(f"File not found: {network_file_path} (원본 경로: {file_path})")
            
            # 변환된 경로 사용
            file_path = network_file_path
            
            # 진행률 업데이트
            await self._update_progress(scan_id, 10)
            
            # AI 파이프라인 실행
            output_path = await self._run_ai_pipeline(scan_id, file_path)
            
            # 진행률 업데이트
            await self._update_progress(scan_id, 90)
            
            # 처리 완료
            self.processing_status[scan_id]["status"] = "COMPLETED"
            self.processing_status[scan_id]["progress"] = 100
            self.processing_status[scan_id]["completed_at"] = datetime.now()
            self.processing_status[scan_id]["output_path"] = output_path
            
            from app.services.websocket_manager import websocket_manager
            # Backend에 완료 알림
            await websocket_manager.send_processing_complete(scan_id, output_path)
            
            logger.info(f"File processing completed: {scan_id} -> {output_path}")
            
        except Exception as e:
            logger.error(f"File processing failed for {scan_id}: {e}")
            self.processing_status[scan_id]["status"] = "ERROR"
            self.processing_status[scan_id]["error"] = str(e)

            from app.services.websocket_manager import websocket_manager
            await websocket_manager.send_processing_error(scan_id, str(e))
        
        finally:
            # 작업 완료 후 정리
            if scan_id in self.processing_tasks:
                del self.processing_tasks[scan_id]
    
    async def _run_ai_pipeline(self, scan_id: int, file_path: str) -> str:
        """AI 파이프라인 실행"""
        loop = asyncio.get_event_loop()
        
        # ThreadPoolExecutor에서 실행
        output_path = await loop.run_in_executor(
            self.executor,
            self._run_ai_pipeline_sync,
            scan_id,
            file_path
        )
        
        return output_path
    
    def _run_ai_pipeline_sync(self, scan_id: int, file_path: str) -> str:
        """동기 AI 파이프라인 실행"""
        try:
            # AI 파이프라인 실행
            from app.services.ai_pipeline import AIPipeline
            
            # 진행률 콜백 함수 정의
            def progress_callback(progress: int, message: str):
                # 비동기 함수를 동기 컨텍스트에서 호출하기 위해 asyncio.run 사용
                import asyncio
                try:
                    loop = asyncio.get_event_loop()
                    if loop.is_running():
                        # 이미 실행 중인 이벤트 루프가 있으면 Task로 생성
                        asyncio.create_task(self._update_progress(scan_id, progress))
                    else:
                        # 이벤트 루프가 없으면 새로 생성
                        asyncio.run(self._update_progress(scan_id, progress))
                except RuntimeError:
                    # 이벤트 루프 관련 에러 무시
                    pass
                logger.info(f"[AI Pipeline] {progress}% - {message}")
            
            # AI 파이프라인 실행 (outputs 루트 전달)
            # 네트워크 경로로 변환
            outputs_dir = normalize_network_path(settings.network_storage_base, settings.outputs_directory)
            ai_pipeline = AIPipeline(progress_callback=progress_callback)
            final_output_path = ai_pipeline.run_full_pipeline(file_path, outputs_dir)
            
            logger.info(f"AI pipeline completed: {file_path} -> {final_output_path}")
            return final_output_path
            
        except Exception as e:
            logger.error(f"AI pipeline failed: {e}")
            raise
    
    async def _update_progress(self, scan_id: int, progress: int):
        """진행률 업데이트"""
        if scan_id in self.processing_status:
            self.processing_status[scan_id]["progress"] = progress

            from app.services.websocket_manager import websocket_manager
            await websocket_manager.send_processing_progress(scan_id, progress)
    
    def get_processing_status(self, scan_id: int) -> Optional[Dict]:
        """처리 상태 조회"""
        return self.processing_status.get(scan_id)
    
    def get_all_processing_status(self) -> Dict[int, Dict]:
        """모든 처리 상태 조회"""
        return self.processing_status.copy()
    
    def cancel_processing(self, scan_id: int) -> bool:
        """처리 취소"""
        if scan_id in self.processing_tasks:
            self.processing_tasks[scan_id].cancel()
            del self.processing_tasks[scan_id]
            
            if scan_id in self.processing_status:
                self.processing_status[scan_id]["status"] = "CANCELLED"
            
            logger.info(f"Processing cancelled for scan_id: {scan_id}")
            return True
        
        return False

# 전역 파일 처리기 인스턴스
file_processor = FileProcessor()