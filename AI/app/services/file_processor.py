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
            
            # 비동기 파일 처리 시작 (group_id와 metadata 전달)
            task = asyncio.create_task(self._process_file_async(scan_id, file_path, group_id, metadata))
            self.processing_tasks[scan_id] = task
            
            return True
            
        except Exception as e:
            logger.error(f"Failed to start processing file {scan_id}: {e}")

            from app.services.websocket_manager import websocket_manager
            await websocket_manager.send_processing_error(scan_id, str(e))
            return False
    
    async def _process_file_async(self, scan_id: int, file_path: str, group_id: str, metadata: Dict):
        """비동기 파일 처리"""
        # group_id 처리 (빈 문자열이거나 None인 경우 기본값 1 사용)
        if not group_id or group_id.strip() == "":
            group_id = "1"
            logger.warning(f"group_id가 비어있어 기본값(1)을 사용합니다. scan_id={scan_id}")
        else:
            # group_id가 유효한지 확인 (정수로 변환 가능한지)
            try:
                int(group_id)  # 유효성 검사
            except (ValueError, TypeError):
                logger.warning(f"유효하지 않은 group_id({group_id})를 기본값(1)으로 변경합니다. scan_id={scan_id}")
                group_id = "1"
        
        # 원본 경로 저장 (폴더명 추출용 - 경로 변환 전)
        original_file_path_for_extraction = file_path
        logger.info(f"[파일 처리 시작] scan_id={scan_id}, 원본 경로={original_file_path_for_extraction}, group_id={group_id}")
        
        try:
            # 사용할 경로 결정
            final_file_path = None
            
            # 1. 원본 경로 확인 (가장 먼저 확인) - 로컬 절대 경로가 올 수 있음
            if os.path.exists(file_path):
                final_file_path = file_path
                logger.info(f"원본 경로 사용 (존재 확인): {file_path}")
            # 2. /project_root/storage/... 형식인 경우 로컬 경로로 변환 시도
            elif file_path.startswith("/project_root/"):
                # 프로젝트 루트 찾기
                try:
                    current_file_dir = os.path.dirname(os.path.abspath(__file__))  # AI/app/services
                    app_dir = os.path.dirname(current_file_dir)  # AI/app
                    ai_dir = os.path.dirname(app_dir)  # AI
                    project_root = os.path.dirname(ai_dir)  # 프로젝트 루트 (S13P31S102)
                    
                    relative_part = file_path.replace("/project_root/", "")
                    local_path = os.path.abspath(os.path.join(project_root, relative_part))
                    
                    if os.path.exists(local_path):
                        final_file_path = local_path
                        logger.info(f"로컬 경로로 변환 성공: {file_path} -> {local_path}")
                    else:
                        logger.warning(f"로컬 경로 변환 실패 (파일 없음): {local_path}")
                except Exception as e:
                    logger.warning(f"로컬 경로 변환 중 오류: {e}")
            # 3. 네트워크 경로가 설정되어 있으면 변환 시도
            elif settings.network_storage_base:
                network_file_path = convert_to_network_path(file_path, settings.network_storage_base)
                if network_file_path != file_path and os.path.exists(network_file_path):
                    final_file_path = network_file_path
                    logger.info(f"변환된 네트워크 경로 사용: {network_file_path}")
                else:
                    logger.warning(f"네트워크 경로 변환 실패: {file_path} -> {network_file_path}")
            else:
                logger.info(f"네트워크 경로 미설정, 원본 경로 사용: {file_path}")
            
            # 최종 경로가 없으면 대체 경로 시도
            if not final_file_path:
                # 프로젝트 루트 찾기 (AI 서비스 실행 위치 기준)
                # AI/app/services/file_processor.py -> AI/app/services -> AI/app -> AI -> 프로젝트 루트
                alternative_paths = []
                
                try:
                    current_file_dir = os.path.dirname(os.path.abspath(__file__))  # AI/app/services
                    app_dir = os.path.dirname(current_file_dir)  # AI/app
                    ai_dir = os.path.dirname(app_dir)  # AI
                    project_root = os.path.dirname(ai_dir)  # 프로젝트 루트 (S13P31S102)
                    
                    # /project_root/storage/... 형식인 경우
                    if file_path.startswith("/project_root/"):
                        relative_part = file_path.replace("/project_root/", "")
                        project_path = os.path.abspath(os.path.join(project_root, relative_part))
                        alternative_paths.append(project_path)
                        logger.debug(f"프로젝트 루트 기준 경로 추가: {project_path}")
                    
                    # /storage/... 형식인 경우
                    elif file_path.startswith("/storage/"):
                        relative_part = file_path.replace("/storage/", "")
                        project_path = os.path.abspath(os.path.join(project_root, "storage", relative_part))
                        alternative_paths.append(project_path)
                        logger.debug(f"프로젝트 루트/storage 기준 경로 추가: {project_path}")
                    
                    # 상대 경로 시도
                    if file_path.startswith("/project_root/"):
                        relative_part = file_path.replace("/project_root/", "")
                        alternative_paths.append(relative_part)
                        alternative_paths.append(f"../{relative_part}")
                    elif file_path.startswith("/storage/"):
                        relative_part = file_path.replace("/storage/", "")
                        alternative_paths.append(f"storage/{relative_part}")
                        alternative_paths.append(f"../storage/{relative_part}")
                        
                except Exception as e:
                    logger.debug(f"프로젝트 루트 찾기 실패: {e}")
                
                # 상대 경로 시도
                logger.info(f"대체 경로 시도 중... (총 {len(alternative_paths)}개)")
                for alt_path in alternative_paths:
                    exists = os.path.exists(alt_path)
                    logger.debug(f"  시도 중: {alt_path} (존재: {exists})")
                    if exists:
                        final_file_path = alt_path
                        logger.info(f"대체 경로 사용: {alt_path} (원본: {file_path})")
                        break
                
                # 모든 경로 시도 실패
                if final_file_path is None:
                    error_msg = f"File not found in any path. Tried:\n"
                    error_msg += f"  - Original: {file_path}\n"
                    if settings.network_storage_base:
                        try:
                            network_file_path = convert_to_network_path(file_path, settings.network_storage_base)
                            error_msg += f"  - Network: {network_file_path}\n"
                        except Exception:
                            pass
                    for alt_path in alternative_paths:
                        exists = os.path.exists(alt_path)
                        error_msg += f"  - Alternative: {alt_path} (exists: {exists})\n"
                    logger.error(error_msg)
                    raise FileNotFoundError(error_msg)
            
            # 최종 경로 사용
            file_path = final_file_path
            
            # 진행률 업데이트
            await self._update_progress(scan_id, 10)
            
            # folder_name 추출 (원본 경로와 최종 경로 모두 시도)
            # 경로 구조: .../storage/uploads/{group_id}/{folder_name}/파일명.obj
            folder_name = None
            
            # 두 경로 모두 시도 (원본 경로 우선)
            paths_to_try = [original_file_path_for_extraction, file_path]
            
            for current_path in paths_to_try:
                if folder_name:
                    break
                    
                try:
                    logger.info(f"[폴더명 추출] 시도 중: 경로={current_path}")
                    
                    # 경로를 정규화하지 않고 그대로 사용
                    path_obj = Path(current_path)
                    path_parts = list(path_obj.parts)
                    logger.info(f"[폴더명 추출] path_parts={path_parts}, 길이={len(path_parts)}")
                
                    # 여러 방법으로 folder_name 추출 시도
                    # 방법 1: 'uploads' 디렉토리 기준으로 추출
                    if 'uploads' in path_parts:
                        uploads_idx = path_parts.index('uploads')
                        logger.info(f"[폴더명 추출] 방법1: uploads 인덱스={uploads_idx}")
                        
                        # uploads 다음에 group_id (인덱스: uploads_idx + 1), 그 다음이 folder_name (인덱스: uploads_idx + 2), 마지막이 파일명
                        required_length = uploads_idx + 4
                        logger.info(f"[폴더명 추출] 방법1: 필요한 최소 길이={required_length}, 실제 길이={len(path_parts)}")
                        
                        if len(path_parts) >= required_length:
                            extracted_folder_name = path_parts[uploads_idx + 2]
                            logger.info(f"[폴더명 추출] 방법1: 추출 시도 - 인덱스 {uploads_idx + 2}의 값='{extracted_folder_name}'")
                            
                            # 추출된 값이 유효한지 확인
                            if extracted_folder_name and extracted_folder_name.strip():
                                # uploads/{group_id}/{folder_name}/파일명 구조에서 folder_name은 항상 폴더명
                                folder_name = extracted_folder_name
                                logger.info(f"[폴더명 추출] 방법1 성공: {folder_name}")
                                break  # 성공했으므로 다른 경로 시도 불필요
                            else:
                                logger.warning(f"[폴더명 추출] 방법1: 추출된 폴더명이 비어있음")
                        else:
                            logger.warning(f"[폴더명 추출] 방법1: 경로 길이 부족 - uploads 인덱스 {uploads_idx}, 전체 길이 {len(path_parts)}, 필요한 최소 길이 {required_length}")
                    
                    # 방법 2: 문자열 기반 추출 (uploads/{group_id}/{folder_name}/...)
                    if not folder_name:
                        path_str = str(current_path).replace('\\', '/')
                        logger.info(f"[폴더명 추출] 방법2: 문자열 기반 추출 시도 - 경로={path_str}")
                        
                        # /uploads/{group_id}/{folder_name}/ 패턴 찾기
                        import re
                        pattern = r'/uploads/\d+/([^/]+)/'
                        match = re.search(pattern, path_str)
                        if match:
                            extracted_folder_name = match.group(1)
                            if extracted_folder_name and extracted_folder_name.strip():
                                folder_name = extracted_folder_name
                                logger.info(f"[폴더명 추출] 방법2 성공: {folder_name}")
                                break  # 성공했으므로 다른 경로 시도 불필요
                            else:
                                logger.warning(f"[폴더명 추출] 방법2: 추출된 값이 비어있음")
                        else:
                            logger.warning(f"[폴더명 추출] 방법2: 정규식 패턴 매칭 실패")
                    
                    # 방법 3: 마지막에서 두 번째가 폴더명일 수 있음
                    if not folder_name and len(path_parts) >= 2:
                        fallback_folder_name = path_parts[-2]
                        logger.info(f"[폴더명 추출] 방법3: 마지막에서 두 번째 값='{fallback_folder_name}'")
                        
                        if fallback_folder_name and fallback_folder_name.strip():
                            # 경로 구조상 마지막에서 두 번째는 폴더명일 가능성이 높음
                            folder_name = fallback_folder_name
                            logger.info(f"[폴더명 추출] 방법3 성공: {folder_name}")
                            break  # 성공했으므로 다른 경로 시도 불필요
                        else:
                            logger.warning(f"[폴더명 추출] 방법3: 추출된 값이 비어있음")
                            
                except Exception as e:
                    logger.error(f"[폴더명 추출] 경로 '{current_path}' 처리 중 예외 발생: {e}", exc_info=True)
                    continue  # 다음 경로 시도
            
            # 전체 예외 처리 (위의 for 루프 밖)
            if not folder_name:
                try:
                    # 마지막 시도: 원본 경로에서 직접 추출
                    logger.warning(f"[폴더명 추출] 모든 방법 실패, 원본 경로에서 직접 추출 시도")
                except Exception as e:
                    logger.error(f"[폴더명 추출] 최종 시도 중 예외 발생: {e}", exc_info=True)
            
            # folder_name이 없거나 빈 문자열이면 파일명 기반으로 생성 (최후의 수단)
            if not folder_name or not folder_name.strip():
                file_stem = Path(file_path).stem
                folder_name = f"{datetime.now().strftime('%Y%m%d_%H%M%S')}_{file_stem}"
                logger.error(f"[폴더명 추출] 모든 방법 실패 - 자동 생성: {folder_name}")
                logger.error(f"[폴더명 추출] 원본 경로: {file_path}")
            else:
                logger.info(f"[폴더명 추출] 최종 사용할 폴더명: {folder_name}")
            
            # AI 파이프라인 실행 (group_id와 folder_name 전달)
            output_path = await self._run_ai_pipeline(scan_id, file_path, group_id, folder_name)
            
            # 진행률 업데이트
            await self._update_progress(scan_id, 90)
            
            # 처리 완료
            self.processing_status[scan_id]["status"] = "COMPLETED"
            self.processing_status[scan_id]["progress"] = 100
            self.processing_status[scan_id]["completed_at"] = datetime.now()
            self.processing_status[scan_id]["output_path"] = output_path
            
            from app.services.websocket_manager import websocket_manager
            # Backend에 완료 알림 (output_path를 포함하여 전송, BackEnd에서 retouched_file_path 업데이트)
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
    
    async def _run_ai_pipeline(self, scan_id: int, file_path: str, group_id: str, folder_name: str) -> str:
        """AI 파이프라인 실행"""
        loop = asyncio.get_event_loop()
        
        # ThreadPoolExecutor에서 실행
        output_path = await loop.run_in_executor(
            self.executor,
            self._run_ai_pipeline_sync,
            scan_id,
            file_path,
            group_id,
            folder_name
        )
        
        return output_path
    
    def _run_ai_pipeline_sync(self, scan_id: int, file_path: str, group_id: str, folder_name: str) -> str:
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
            # 저장소 기본 경로 사용 (로컬 또는 네트워크)
            storage_base = settings.storage_base_path
            outputs_dir = os.path.join(storage_base, settings.outputs_directory)
            logger.info(f"[파일 처리] storage_base: {storage_base}, outputs_dir: {outputs_dir}")
            ai_pipeline = AIPipeline(progress_callback=progress_callback)
            final_output_path = ai_pipeline.run_full_pipeline(file_path, outputs_dir, group_id, folder_name)
            
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