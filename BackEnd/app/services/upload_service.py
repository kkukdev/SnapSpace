import os
import aiofiles
import logging
import zipfile
import shutil
from datetime import datetime
from pathlib import Path
from typing import Dict, Any, Optional, AsyncGenerator, List
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
        try:
            upload_path.mkdir(parents=True, exist_ok=True)
        except Exception as e:
            raise HTTPException(
                status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                detail=f"업로드 디렉토리를 생성할 수 없습니다: {str(e)}"
            )
        return upload_path

    def find_obj_files(self, directory: Path) -> List[Path]:
        """디렉토리 내 모든 .obj 파일을 재귀적으로 찾기"""
        obj_files = []
        try:
            for root, dirs, files in os.walk(directory):
                for file in files:
                    if file.lower().endswith('.obj'):
                        obj_files.append(Path(root) / file)
        except Exception as e:
            logger.error(f"obj 파일 검색 중 오류 발생: {str(e)}")
        return obj_files

    async def extract_zip_file(self, zip_path: Path, extract_to: Path) -> Path:
        """zip 파일 압축 해제 (비동기로 실행하여 이벤트 룹 블로킹 방지)"""
        def _extract():
            """동기 압축 해제 함수"""
            # 압축 해제할 디렉토리 생성
            extract_to.mkdir(parents=True, exist_ok=True)
            
            # zip 파일 압축 해제
            with zipfile.ZipFile(zip_path, 'r') as zip_ref:
                zip_ref.extractall(extract_to)
            
            return extract_to
        
        try:
            # 별도 스레드에서 실행하여 이벤트 룹 블로킹 방지
            return await asyncio.to_thread(_extract)
        except zipfile.BadZipFile:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail="유효하지 않은 zip 파일입니다."
            )
        except Exception as e:
            raise HTTPException(
                status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                detail=f"zip 파일 압축 해제 중 오류가 발생했습니다: {str(e)}"
            )

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
            
            # 파일이 실제로 저장되었는지 확인
            if not file_path.exists():
                raise HTTPException(
                    status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                    detail="파일이 저장되지 않았습니다."
                )
            
            return total_size
            
        except HTTPException:
            raise
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
            
            # 파일 포인터를 처음으로 리셋 (이전에 읽혔을 수 있음)
            await file.seek(0)
            
            # 파일 확장자 확인
            file_extension = Path(file.filename).suffix.lower()
            
            # zip 파일인 경우 별도 처리
            if file_extension == '.zip':
                return await self._upload_zip_file(file, upload_dir)
            
            # 기존 로직: 일반 파일 업로드
            # 고유한 파일명 생성
            filename = self.generate_filename(file.filename)
            file_path = upload_dir / filename
            
            # 파일 포인터를 다시 처음으로 리셋
            await file.seek(0)
            
            # 최적화된 파일 저장
            total_size = await self.save_file_optimized(file, file_path)
            
            # 파일이 실제로 존재하는지 최종 확인
            if not file_path.exists():
                raise HTTPException(
                    status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                    detail="파일이 저장되지 않았습니다. 저장 경로를 확인하세요."
                )
            
            # 파일 크기 확인
            actual_file_size = file_path.stat().st_size
            
            # 파일 존재 및 접근 가능 여부 최종 확인
            file_exists = file_path.exists()
            file_readable = file_path.is_file() and os.access(file_path, os.R_OK)
            
            upload_result = {
                "original_filename": file.filename,
                "saved_filename": filename,
                "file_size": total_size,
                "actual_file_size": actual_file_size,
                "file_path": str(file_path.absolute()),
                "file_exists": file_exists,
                "file_readable": file_readable,
                "upload_success": file_exists and file_readable and actual_file_size > 0
            }
            
            # 파일이 제대로 저장되지 않은 경우
            if not upload_result["upload_success"]:
                raise HTTPException(
                    status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                    detail=f"파일이 제대로 저장되지 않았습니다. 존재: {file_exists}, 읽기 가능: {file_readable}, 크기: {actual_file_size} bytes"
                )
            
            # 웹소켓으로 업로드된 파일 정보 전송
            try:
                file_info = [{
                    "scan_id": "0",
                    "file_path": str(file_path.absolute()),
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

    async def _upload_zip_file(self, file: UploadFile, upload_dir: Path) -> Dict[str, Any]:
        """zip 파일 업로드 및 압축 해제 처리"""
        # 고유한 파일명 생성 (zip 파일용)
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        name, _ = os.path.splitext(file.filename)
        zip_filename = f"{timestamp}_{name}.zip"
        zip_path = upload_dir / zip_filename
        
        # zip 파일 저장
        total_size = await self.save_file_optimized(file, zip_path)
        
        # zip 파일 존재 확인
        if not zip_path.exists():
            raise HTTPException(
                status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                detail="zip 파일이 저장되지 않았습니다."
            )
        
        # 압축 해제할 디렉토리 생성
        extract_dir_name = f"{timestamp}_{name}"
        extract_dir = upload_dir / extract_dir_name
        
        try:
            # zip 파일 압축 해제
            await self.extract_zip_file(zip_path, extract_dir)
            
            # 압축 해제된 폴더 내 .obj 파일 찾기
            obj_files = self.find_obj_files(extract_dir)
            
            if not obj_files:
                logger.warning(f"압축 해제된 폴더에 .obj 파일이 없습니다: {extract_dir}")
                # obj 파일이 없어도 성공으로 처리하되, 빈 리스트 전송
                file_info = []
            else:
                # 찾은 모든 .obj 파일 정보를 웹소켓으로 전송
                file_info = []
                for obj_file in obj_files:
                    file_info.append({
                        "scan_id": "0",
                        "file_path": str(obj_file.absolute()),
                        "group_id": "",  # 업로드 시점에는 아직 group_id가 없을 수 있음
                        "metadata": {
                            "original_filename": file.filename,
                            "extracted_from_zip": zip_filename,
                            "obj_file": obj_file.name,
                            "relative_path": str(obj_file.relative_to(extract_dir)),
                            "file_size": obj_file.stat().st_size if obj_file.exists() else 0,
                            "timestamp": datetime.now().isoformat()
                        }
                    })
            
            # 웹소켓으로 obj 파일 목록 전송
            try:
                if file_info:
                    await websocket_manager.send_file_list(file_info)
            except Exception as e:
                # 웹소켓 전송 실패는 업로드 자체를 실패시키지 않음
                logger.warning(f"Failed to send zip file upload notification via websocket: {e}")
            
            # 결과 반환
            return {
                "original_filename": file.filename,
                "saved_filename": zip_filename,
                "file_size": total_size,
                "actual_file_size": zip_path.stat().st_size,
                "file_path": str(zip_path.absolute()),
                "extract_dir": str(extract_dir.absolute()),
                "obj_files_count": len(obj_files),
                "obj_files": [str(obj.absolute()) for obj in obj_files],
                "upload_success": True,
                "is_zip": True
            }
            
        except Exception as e:
            # 실패 시 zip 파일 및 압축 해제 디렉토리 정리
            if zip_path.exists():
                try:
                    zip_path.unlink()
                except:
                    pass
            if extract_dir.exists():
                try:
                    shutil.rmtree(extract_dir)
                except:
                    pass
            raise HTTPException(
                status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                detail=f"zip 파일 처리 중 오류가 발생했습니다: {str(e)}"
            )

    # 기존 메서드 호환성을 위해 별칭 제공
    async def save_file_in_chunks(self, file: UploadFile, file_path: Path) -> int:
        """기존 코드 호환성을 위한 별칭 (내부적으로 최적화된 버전 사용)"""
        return await self.save_file_optimized(file, file_path)


# 싱글톤 인스턴스
upload_service = UploadService()
