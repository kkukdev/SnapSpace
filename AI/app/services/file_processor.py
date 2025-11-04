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
            
            # 사용할 경로 결정
            final_file_path = None
            
            # 1. 네트워크 경로 확인
            if os.path.exists(network_file_path):
                final_file_path = network_file_path
                logger.info(f"네트워크 경로 사용: {network_file_path}")
            # 2. 원본 경로 확인
            elif os.path.exists(file_path):
                final_file_path = file_path
                logger.warning(f"네트워크 경로 접근 실패, 원본 경로 사용: {file_path} (네트워크 경로: {network_file_path})")
            else:
                # 3. Docker 경로를 상대 경로로 변환 시도
                # /project_root/storage/uploads/... -> storage/uploads/...
                alternative_paths = []
                
                # Docker 경로 패턴 제거
                if file_path.startswith("/project_root/"):
                    alternative_path = file_path.replace("/project_root/", "", 1)
                    alternative_paths.append(alternative_path)
                    # ../storage/uploads/... 형태도 시도
                    alternative_paths.append(f"../{alternative_path}")
                
                # /storage/로 시작하는 경우
                if file_path.startswith("/storage/"):
                    alternative_path = file_path.replace("/storage/", "storage/", 1)
                    alternative_paths.append(alternative_path)
                    alternative_paths.append(f"../{alternative_path}")
                
                # 프로젝트 루트 찾기 (AI 서비스 실행 위치 기준)
                # AI/app/services/file_processor.py -> AI/app/services -> AI/app -> AI -> 프로젝트 루트
                try:
                    current_file_dir = os.path.dirname(os.path.abspath(__file__))  # AI/app/services
                    app_dir = os.path.dirname(current_file_dir)  # AI/app
                    ai_dir = os.path.dirname(app_dir)  # AI
                    project_root = os.path.dirname(ai_dir)  # 프로젝트 루트 (S13P31S102)
                    
                    # 프로젝트 루트 기준 경로 생성
                    if file_path.startswith("/project_root/"):
                        relative_part = file_path.replace("/project_root/", "")
                        project_path = os.path.join(project_root, relative_part).replace("\\", os.sep)
                        alternative_paths.append(project_path)
                    elif file_path.startswith("/storage/"):
                        relative_part = file_path.replace("/storage/", "")
                        project_path = os.path.join(project_root, "storage", relative_part).replace("\\", os.sep)
                        alternative_paths.append(project_path)
                except Exception as e:
                    logger.debug(f"프로젝트 루트 찾기 실패: {e}")
                
                # 상대 경로 시도
                logger.debug(f"대체 경로 시도 중... (총 {len(alternative_paths)}개)")
                for alt_path in alternative_paths:
                    logger.debug(f"  시도 중: {alt_path} (존재: {os.path.exists(alt_path)})")
                    if os.path.exists(alt_path):
                        final_file_path = alt_path
                        logger.warning(f"대체 경로 사용: {alt_path} (원본: {file_path})")
                        break
                
                # 모든 경로 시도 실패
                if final_file_path is None:
                    error_msg = f"File not found in any path. Tried:\n"
                    error_msg += f"  - Network: {network_file_path}\n"
                    error_msg += f"  - Original: {file_path}\n"
                    for alt_path in alternative_paths:
                        exists = os.path.exists(alt_path)
                        error_msg += f"  - Alternative: {alt_path} (exists: {exists})\n"
                    logger.error(error_msg)
                    raise FileNotFoundError(error_msg)
            
            # 최종 경로 사용
            file_path = final_file_path
            
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