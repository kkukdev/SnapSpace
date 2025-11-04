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

    async def ensure_group_upload_directory(self, group_id: Optional[str] = None) -> Path:
        """그룹별 업로드 디렉토리 생성 및 경로 반환"""
        base_upload_path = Path(settings.UPLOAD_DIR)
        
        if group_id is None or group_id == "":
            # group_id가 없으면 1번 디렉토리 사용
            upload_path = base_upload_path / str(1)
        else:
            # storage/uploads/{group_id} 구조
            upload_path = base_upload_path / str(group_id)
        
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

    def find_all_files(self, directory: Path) -> List[Path]:
        """디렉토리 내 모든 파일을 재귀적으로 찾기"""
        all_files = []
        try:
            for root, dirs, files in os.walk(directory):
                for file in files:
                    all_files.append(Path(root) / file)
        except Exception as e:
            logger.error(f"파일 검색 중 오류 발생: {str(e)}")
        return all_files

    async def extract_zip_file(self, zip_path: Path, extract_to: Path) -> Path:
        """zip 파일 압축 해제 (내부 폴더가 있으면 그 내부의 파일들만 저장)"""
        def _extract():
            """동기 압축 해제 함수"""
            try:
                # 임시 디렉토리에 먼저 압축 해제
                temp_dir = extract_to.parent / f"{extract_to.name}_temp"
                temp_dir.mkdir(parents=True, exist_ok=True)
                
                # zip 파일 압축 해제
                with zipfile.ZipFile(zip_path, 'r') as zip_ref:
                    zip_ref.extractall(temp_dir)
                
                # 압축 해제된 내용 확인
                extracted_items = list(temp_dir.iterdir())
                
                # 모든 파일을 찾기 (재귀적으로)
                all_files = self.find_all_files(temp_dir)
                
                # extract_to 디렉토리 생성
                if extract_to.exists():
                    shutil.rmtree(extract_to)
                extract_to.mkdir(parents=True, exist_ok=True)
                
                # 모든 파일을 extract_to 디렉토리로 복사 (폴더 구조 제거, 파일만 저장)
                for file_path in all_files:
                    # temp_dir을 기준으로 한 상대 경로
                    try:
                        relative_path = file_path.relative_to(temp_dir)
                        # 파일명만 사용 (폴더 구조 무시)
                        dest_file = extract_to / file_path.name
                        # 파일명 중복 방지
                        counter = 1
                        original_dest = dest_file
                        while dest_file.exists():
                            stem = original_dest.stem
                            suffix = original_dest.suffix
                            dest_file = extract_to / f"{stem}_{counter}{suffix}"
                            counter += 1
                        shutil.copy2(str(file_path), str(dest_file))
                    except Exception as e:
                        logger.warning(f"파일 복사 중 오류 발생: {file_path}, {str(e)}")
                        continue
                
                # 임시 디렉토리 삭제
                shutil.rmtree(temp_dir)
                
                return extract_to
            except Exception as e:
                # 내부 함수에서 발생한 예외를 상위로 전파
                raise
        
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

    def generate_folder_name(self, original_filename: str) -> str:
        """업로드용 폴더명 생성 (날짜_파일명, 확장자 제거)"""
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        name, _ = os.path.splitext(original_filename)
        return f"{timestamp}_{name}"

    def generate_filename(self, original_filename: str) -> str:
        """고유한 파일명 생성 (시스템 로컬 타임존 사용)"""
        # 시스템의 로컬 타임존 시간을 직접 사용
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
        progress_callback: Optional[callable] = None,
        group_id: Optional[str] = None
    ) -> AsyncGenerator[Dict[str, Any], None]:
        """진행률 콜백과 함께 파일 업로드"""
        
        # 파일 검증
        if not file.filename:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail="파일명이 없습니다"
            )
        
        await self.validate_file_extension(file.filename)
        upload_dir = await self.ensure_group_upload_directory(group_id)
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

    async def upload_file(self, file: UploadFile, group_id: Optional[str] = None) -> Dict[str, Any]:
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
            
            # 그룹별 업로드 디렉토리 준비
            group_upload_dir = await self.ensure_group_upload_directory(group_id)
            
            # 파일 포인터를 처음으로 리셋 (이전에 읽혔을 수 있음)
            await file.seek(0)
            
            # 파일 확장자 확인
            file_extension = Path(file.filename).suffix.lower()
            
            # zip 파일인 경우 별도 처리
            if file_extension == '.zip':
                return await self._upload_zip_file(file, group_upload_dir, group_id)
            
            # 일반 파일 업로드: storage/uploads/{group_id}/{날짜_파일명}/파일명 형태
            folder_name = self.generate_folder_name(file.filename)
            upload_folder = group_upload_dir / folder_name
            upload_folder.mkdir(parents=True, exist_ok=True)
            
            # 원본 파일명 사용 (타임스탬프는 폴더명에 포함)
            filename = file.filename
            file_path = upload_folder / filename
            
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
                "folder_path": str(upload_folder.absolute()),
                "file_exists": file_exists,
                "file_readable": file_readable,
                "upload_success": file_exists and file_readable and actual_file_size > 0,
                "group_id": group_id
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
                    "group_id": str(group_id) if group_id else "",
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

    async def _upload_zip_file(self, file: UploadFile, group_upload_dir: Path, group_id: Optional[str] = None) -> Dict[str, Any]:
        """zip 파일 업로드 및 압축 해제 처리"""
        # 업로드 폴더 생성: storage/uploads/{group_id}/{날짜_파일명}
        folder_name = self.generate_folder_name(file.filename)
        upload_folder = group_upload_dir / folder_name
        upload_folder.mkdir(parents=True, exist_ok=True)
        
        # 임시 zip 파일 저장 경로 (업로드 폴더 내부)
        temp_zip_filename = f"temp_{datetime.now().strftime('%Y%m%d_%H%M%S')}.zip"
        temp_zip_path = upload_folder / temp_zip_filename
        
        # 파일 포인터를 처음으로 리셋
        try:
            await file.seek(0)
        except Exception:
            pass
        
        # 임시 zip 파일 저장
        total_size = await self.save_file_optimized(file, temp_zip_path)
        
        # zip 파일 존재 확인
        if not temp_zip_path.exists():
            raise HTTPException(
                status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                detail="zip 파일이 저장되지 않았습니다."
            )
        
        try:
            # zip 파일 압축 해제 (내용물만 upload_folder에 저장)
            await self.extract_zip_file(temp_zip_path, upload_folder)
            
            # 압축 해제 후 임시 zip 파일 삭제
            if temp_zip_path.exists():
                temp_zip_path.unlink()
            
            # 업로드 폴더 내의 모든 파일 찾기
            all_files = self.find_all_files(upload_folder)
            
            # .obj 파일도 찾기 (호환성 유지)
            obj_files = self.find_obj_files(upload_folder)
            
            # 웹소켓으로 파일 정보 전송
            file_info = []
            for file_path in all_files:
                try:
                    file_size = file_path.stat().st_size if file_path.exists() else 0
                    relative_path = str(file_path.relative_to(upload_folder))
                except Exception:
                    file_size = 0
                    relative_path = file_path.name
                
                file_info.append({
                    "scan_id": "0",
                    "file_path": str(file_path.absolute()),
                    "group_id": str(group_id) if group_id else "",
                    "metadata": {
                        "original_filename": file.filename,
                        "extracted_from_zip": True,
                        "obj_file": file_path.name if file_path.suffix.lower() == '.obj' else None,
                        "relative_path": relative_path,
                        "file_size": file_size,
                        "timestamp": datetime.now().isoformat()
                    }
                })
            
            try:
                if file_info:
                    await websocket_manager.send_file_list(file_info)
            except Exception as e:
                logger.warning(f"Failed to send zip file upload notification via websocket: {e}")
            
            # 결과 반환
            return {
                "original_filename": file.filename,
                "file_size": total_size,
                "folder_path": str(upload_folder.absolute()),
                "files_count": len(all_files),
                "obj_files_count": len(obj_files),
                "files": [str(f.absolute()) for f in all_files],
                "obj_files": [str(obj.absolute()) for obj in obj_files],
                "upload_success": True,
                "is_zip": True,
                "group_id": group_id
            }
            
        except HTTPException:
            # HTTPException은 그대로 전파하되, 정리 작업 수행
            if temp_zip_path.exists():
                try:
                    temp_zip_path.unlink()
                except:
                    pass
            if upload_folder.exists():
                try:
                    shutil.rmtree(upload_folder)
                except:
                    pass
            raise
        except Exception as e:
            # 실패 시 zip 파일 및 폴더 정리
            if temp_zip_path.exists():
                try:
                    temp_zip_path.unlink()
                except:
                    pass
            if upload_folder.exists():
                try:
                    shutil.rmtree(upload_folder)
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
