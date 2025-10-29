import os
import aiofiles
import logging
from datetime import datetime
from pathlib import Path
from typing import Dict, Any, Optional, AsyncGenerator
from fastapi import UploadFile, HTTPException, status
import asyncio

from app.config import settings
from app.services.base import BaseService
from app.services.websocket_manager import websocket_manager

logger = logging.getLogger(__name__)


class UploadService(BaseService):
    """최적화된 파일 업로드 서비스 클래스"""

    def __init__(self):
        super().__init__()

    async def validate_file_extension(self, filename: str) -> None:
        """파일 확장자 검증"""
        file_extension = Path(filename).suffix.lower()
        if file_extension not in settings.ALLOWED_EXTENSIONS:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail=f"지원하지 않는 파일 형식입니다. 허용된 확장자: {', '.join(settings.ALLOWED_EXTENSIONS)}"
            )

    async def ensure_upload_directory(self) -> Path:
        """업로드 디렉토리 생성 및 경로 반환"""
        upload_path = Path(settings.UPLOAD_DIR)
        upload_path.mkdir(exist_ok=True)
        return upload_path

    def generate_filename(self, original_filename: str) -> str:
        """고유한 파일명 생성"""
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        name, extension = os.path.splitext(original_filename)
        return f"{timestamp}_{name}{extension}"

    async def save_file_optimized(self, file: UploadFile, file_path: Path) -> int:
        """최적화된 버퍼링 기반 파일 저장"""
        total_size = 0
        chunk_size = 64 * 1024  # 64KB 청크 (기존 8KB에서 개선)
        buffer_size = 1024 * 1024  # 1MB 버퍼
        
        try:
            async with aiofiles.open(file_path, 'wb') as f:
                buffer = bytearray()
                
                while chunk := await file.read(chunk_size):
                    total_size += len(chunk)
                    
                    # 파일 크기 제한 검증
                    if total_size > settings.MAX_FILE_SIZE:
                        # 파일 삭제
                        if file_path.exists():
                            file_path.unlink()
                        raise HTTPException(
                            status_code=status.HTTP_413_REQUEST_ENTITY_TOO_LARGE,
                            detail=f"파일이 너무 큽니다. 최대 크기: {settings.MAX_FILE_SIZE // (1024*1024)}MB"
                        )
                    
                    buffer.extend(chunk)
                    
                    # 버퍼가 가득 찼을 때만 디스크에 쓰기 (I/O 최적화)
                    if len(buffer) >= buffer_size:
                        await f.write(buffer)
                        buffer.clear()
                        
                        # CPU 점유율 조절을 위한 미세한 지연
                        if total_size % (5 * 1024 * 1024) == 0:  # 5MB마다
                            await asyncio.sleep(0.001)  # 1ms 지연
                
                # 남은 데이터 쓰기
                if buffer:
                    await f.write(buffer)
            
            return total_size
            
        except Exception as e:
            # 실패 시 파일 삭제
            if file_path.exists():
                try:
                    file_path.unlink()
                except:
                    pass  # 삭제 실패는 무시
            raise e

    async def upload_file_with_progress(
        self, 
        file: UploadFile,
        progress_callback: Optional[callable] = None
    ) -> AsyncGenerator[Dict[str, Any], None]:
        """진행률 콜백과 함께 파일 업로드"""
        
        # 파일 검증
        if not file.filename:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail="파일명이 없습니다"
            )
        
        await self.validate_file_extension(file.filename)
        upload_dir = await self.ensure_upload_directory()
        filename = self.generate_filename(file.filename)
        file_path = upload_dir / filename
        
        total_size = 0
        chunk_size = 128 * 1024  # 128KB 청크
        
        try:
            async with aiofiles.open(file_path, 'wb') as f:
                while chunk := await file.read(chunk_size):
                    total_size += len(chunk)
                    
                    # 크기 검증
                    if total_size > settings.MAX_FILE_SIZE:
                        if file_path.exists():
                            file_path.unlink()
                        raise HTTPException(
                            status_code=status.HTTP_413_REQUEST_ENTITY_TOO_LARGE,
                            detail=f"파일이 너무 큽니다."
                        )
                    
                    # 청크 쓰기
                    await f.write(chunk)
                    
                    # 진행률 콜백
                    if progress_callback:
                        progress = {
                            "uploaded_bytes": total_size,
                            "percentage": min(100, (total_size / settings.MAX_FILE_SIZE) * 100),
                            "status": "uploading"
                        }
                        yield progress
            
            # 업로드 완료
            result = {
                "original_filename": file.filename,
                "saved_filename": filename,
                "file_size": total_size,
                "file_path": str(file_path),
                "status": "completed"
            }
            yield result
            
        except Exception as e:
            # 실패 시 파일 삭제
            if file_path.exists():
                try:
                    file_path.unlink()
                except:
                    pass
            raise

    async def upload_file(self, file: UploadFile) -> Dict[str, Any]:
        """파일 업로드 처리 (최적화된 버전)"""
        try:
            # 파일명 검증
            if not file.filename:
                raise HTTPException(
                    status_code=status.HTTP_400_BAD_REQUEST,
                    detail="파일명이 없습니다"
                )
            
            # 파일 확장자 검증
            await self.validate_file_extension(file.filename)
            
            # 업로드 디렉토리 준비
            upload_dir = await self.ensure_upload_directory()
            
            # 고유한 파일명 생성
            filename = self.generate_filename(file.filename)
            file_path = upload_dir / filename
            
            # 최적화된 파일 저장
            total_size = await self.save_file_optimized(file, file_path)
            
            upload_result = {
                "original_filename": file.filename,
                "saved_filename": filename,
                "file_size": total_size,
                "file_path": str(file_path)
            }
            
            # 웹소켓으로 업로드된 파일 정보 전송
            try:
                file_info = [{
                    "file_path": str(file_path),
                    "group_id": "",  # 업로드 시점에는 아직 group_id가 없을 수 있음
                    "metadata": {
                        "original_filename": file.filename,
                        "saved_filename": filename,
                        "file_size": total_size,
                        "timestamp": datetime.now().isoformat()
                    }
                }]
                await websocket_manager.send_file_list(file_info)
            except Exception as e:
                # 웹소켓 전송 실패는 업로드 자체를 실패시키지 않음
                logger.warning(f"Failed to send file upload notification via websocket: {e}")
            
            return upload_result
            
        except HTTPException:
            # FastAPI HTTP 예외는 그대로 전파
            raise
        except Exception as e:
            # 기타 모든 예외 처리
            raise HTTPException(
                status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                detail=f"파일 업로드 중 오류가 발생했습니다: {str(e)}"
            )

    # 기존 메서드 호환성을 위해 별칭 제공
    async def save_file_in_chunks(self, file: UploadFile, file_path: Path) -> int:
        """기존 코드 호환성을 위한 별칭 (내부적으로 최적화된 버전 사용)"""
        return await self.save_file_optimized(file, file_path)


# 싱글톤 인스턴스
upload_service = UploadService()
