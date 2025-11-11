import os
import aiofiles
import logging
import zipfile
import shutil
import re
from datetime import datetime
from pathlib import Path
from typing import Dict, Any, Optional, AsyncGenerator, List
from fastapi import UploadFile, HTTPException, status
import asyncio

from app.config import settings, get_current_datetime
from app.services.base import BaseService
from app.services.websocket_manager import websocket_manager
from app.services.scan_service import scan_service
from app.services.group_service import group_service
from app.schemas.scan import ScanCreate, ScanStatus
from app.schemas.group import GroupCreate
from app.database import SessionLocal

logger = logging.getLogger(__name__)


class UploadService(BaseService):
    """최적화된 파일 업로드 서비스 클래스"""

    def __init__(self):
        super().__init__()

    async def _create_scan_for_upload(
        self,
        group_id: Optional[str],
        original_file_path: str,
        original_filename: str,
        file_size: int,
        model_type: Optional[str] = None
    ) -> Optional[Dict[str, Any]]:
        """업로드된 파일에 대한 스캔 정보 생성"""
        try:
            # group_id를 정수로 변환 (없으면 기본값 1)
            try:
                group_id_int = int(group_id) if group_id else 1
            except (ValueError, TypeError):
                group_id_int = 1
            
            # model_type 기본값 설정 (없으면 "space")
            model_type_value = model_type if model_type else "space"
            
            # DB 세션 생성
            db = SessionLocal()
            try:
                # 스캔 생성 데이터 준비
                scan_data = ScanCreate(
                    group_id=group_id_int,
                    meta_data={
                        "original_filename": original_filename,
                        "file_size": file_size,
                        "model_type": model_type_value,
                        "uploaded_at": get_current_datetime().isoformat()
                    },
                    status=ScanStatus.UPLOADED,
                    original_file_path=original_file_path,
                    retouched_file_path=None,
                    memos=None
                )
                
                # 스캔 생성
                scan_response = scan_service.create_scan(db=db, scan_data=scan_data)
                
                if scan_response.success and scan_response.data:
                    logger.info(f"스캔 생성 성공: scan_id={scan_response.data.scan_id}, file_path={original_file_path}")
                    return {
                        "scan_id": scan_response.data.scan_id,
                        "scan_created": True
                    }
                else:
                    logger.warning(f"스캔 생성 실패: {scan_response.message}")
                    return None
            except HTTPException as e:
                # HTTPException (예: 그룹이 없는 경우) 처리
                logger.error(f"스캔 생성 중 HTTP 오류 발생: {e.detail} (status_code: {e.status_code})", exc_info=True)
                db.rollback()
                # 그룹이 없는 경우 기본 그룹(1)으로 재시도
                if e.status_code == status.HTTP_404_NOT_FOUND and "그룹" in str(e.detail):
                    logger.warning(f"그룹 {group_id_int}을 찾을 수 없어 기본 그룹(1)으로 재시도")
                    try:
                        scan_data.group_id = 1
                        scan_response = scan_service.create_scan(db=db, scan_data=scan_data)
                        if scan_response.success and scan_response.data:
                            logger.info(f"기본 그룹(1)으로 스캔 생성 성공: scan_id={scan_response.data.scan_id}")
                            return {
                                "scan_id": scan_response.data.scan_id,
                                "scan_created": True
                            }
                    except HTTPException as retry_e:
                        # 기본 그룹(1)도 없으면 새로운 그룹 생성
                        if retry_e.status_code == status.HTTP_404_NOT_FOUND and "그룹" in str(retry_e.detail):
                            logger.warning(f"기본 그룹(1)도 없어 새로운 그룹(name='empty') 생성 시도")
                            try:
                                # 새로운 그룹 생성 (name="empty")
                                # 이름 중복 가능성 고려: 먼저 기존 그룹 확인
                                existing_group = group_service.get_group_by_name(db, name="empty")
                                if existing_group:
                                    # 이미 "empty" 그룹이 있으면 그 그룹 사용
                                    new_group_id = existing_group.group_id
                                    logger.info(f"기존 'empty' 그룹 발견: group_id={new_group_id}")
                                else:
                                    # 새로운 그룹 생성
                                    group_data = GroupCreate(
                                        name="empty",
                                        meta_data={"description": "자동 생성된 기본 그룹", "auto_created": True}
                                    )
                                    group_response = group_service.create_group(db=db, group_data=group_data)
                                    if group_response.success and group_response.data:
                                        new_group_id = group_response.data.group_id
                                        logger.info(f"새로운 그룹 생성 성공: group_id={new_group_id}, name='empty'")
                                    else:
                                        logger.error(f"그룹 생성 실패: {group_response.message}")
                                        return None
                                
                                # 생성된 그룹으로 스캔 생성 재시도
                                scan_data.group_id = new_group_id
                                scan_response = scan_service.create_scan(db=db, scan_data=scan_data)
                                if scan_response.success and scan_response.data:
                                    logger.info(f"새로운 그룹(group_id={new_group_id})으로 스캔 생성 성공: scan_id={scan_response.data.scan_id}")
                                    return {
                                        "scan_id": scan_response.data.scan_id,
                                        "scan_created": True
                                    }
                                else:
                                    logger.error(f"새로운 그룹으로 스캔 생성 실패: {scan_response.message}")
                                    return None
                            except Exception as create_e:
                                logger.error(f"새로운 그룹 생성 및 스캔 생성 중 오류 발생: {str(create_e)}", exc_info=True)
                                return None
                        else:
                            logger.error(f"기본 그룹으로 재시도 중 오류 발생: {str(retry_e)}", exc_info=True)
                    except Exception as retry_e:
                        logger.error(f"기본 그룹으로 재시도 중 예상치 못한 오류 발생: {str(retry_e)}", exc_info=True)
                return None
            except Exception as e:
                logger.error(f"스캔 생성 중 오류 발생: {str(e)}", exc_info=True)
                db.rollback()
                return None
            finally:
                db.close()
        except Exception as e:
            logger.error(f"스캔 생성 처리 중 오류 발생: {str(e)}", exc_info=True)
            return None

    async def validate_file_extension(self, filename: str) -> None:
        """파일 확장자 검증"""
        file_extension = Path(filename).suffix.lower()
        if file_extension not in settings.ALLOWED_EXTENSIONS:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail=f"지원하지 않는 파일 형식입니다. 허용된 확장자: {', '.join(settings.ALLOWED_EXTENSIONS)}"
            )

    async def ensure_group_upload_directory(self, group_name: Optional[str] = None) -> Path:
        """그룹별 업로드 디렉토리 생성 및 경로 반환 (group_name 사용)"""
        base_upload_path = Path(settings.UPLOAD_DIR)
        
        if group_name is None or group_name == "":
            # group_name이 없으면 default 디렉토리 사용
            upload_path = base_upload_path / str("default")
        else:
            # storage/uploads/{group_name} 구조
            upload_path = base_upload_path / str(group_name)
        
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

    def find_memo_file(self, directory: Path) -> Optional[Path]:
        """
        디렉토리 내 메모 파일 찾기 (단일 파일)
        
        Returns:
            Optional[Path]: 찾은 메모 파일 경로, 없으면 None
        """
        try:
            for root, dirs, files in os.walk(directory):
                for file in files:
                    file_ext = Path(file).suffix.lower()
                    # 텍스트 파일만 확인
                    if file_ext in [".txt"]:
                        file_path = Path(root) / file
                        # 파일명에 메모 관련 키워드가 포함되어 있는지 확인
                        file_stem = Path(file).stem.lower()
                        memo_keywords = ["memo", "note", "note_memo", "memo_note"]
                        if any(keyword in file_stem for keyword in memo_keywords):
                            return file_path
        except Exception as e:
            logger.error(f"메모 파일 검색 중 오류 발생: {str(e)}")
        
        return None

    async def read_text_memo_file(self, memo_path: Path) -> Optional[str]:
        """텍스트 메모 파일 읽기 (비동기)"""
        try:
            async with aiofiles.open(memo_path, 'r', encoding='utf-8') as f:
                content = await f.read()
                return content.strip() if content else None
        except UnicodeDecodeError:
            # UTF-8로 읽기 실패 시 다른 인코딩 시도
            try:
                async with aiofiles.open(memo_path, 'r', encoding='cp949') as f:
                    content = await f.read()
                    return content.strip() if content else None
            except Exception as e:
                logger.error(f"텍스트 메모 파일 읽기 실패 (인코딩 오류): {str(e)}")
                return None
        except Exception as e:
            logger.error(f"텍스트 메모 파일 읽기 중 오류 발생: {str(e)}")
            return None

    def parse_memo_content(self, content: str) -> Dict[str, str]:
        """
        메모 파일 내용을 파싱하여 key-value 쌍으로 변환
        
        형식:
        [key1]
        value1
        
        [key2]
        value2
        
        같은 key가 여러 번 나오면 value를 연결
        
        Returns:
            Dict[str, str]: key-value 쌍 딕셔너리
        """
        parsed_data = {}
        
        # 대괄호 패턴 찾기: [...]
        pattern = r'\[([^\]]+)\]'
        matches = list(re.finditer(pattern, content))
        
        if not matches:
            # 대괄호가 없으면 전체 내용을 빈 key로 저장
            if content.strip():
                parsed_data[""] = content.strip()
            return parsed_data
        
        # 각 대괄호 구간 처리
        for i, match in enumerate(matches):
            key = match.group(1).strip()
            
            # 현재 대괄호의 끝 위치
            start_pos = match.end()
            
            # 다음 대괄호의 시작 위치 (마지막이면 파일 끝)
            if i + 1 < len(matches):
                end_pos = matches[i + 1].start()
            else:
                end_pos = len(content)
            
            # value 추출 (대괄호 다음부터 다음 대괄호 전까지)
            value = content[start_pos:end_pos].strip()
            
            # 같은 key가 이미 있으면 기존 value 뒤에 추가
            if key in parsed_data:
                # 기존 value와 새 value를 연결 (줄바꿈으로 구분)
                parsed_data[key] = parsed_data[key] + "\n\n" + value
            else:
                parsed_data[key] = value
        
        return parsed_data

    async def process_text_memo_file(self, memo_path: Path) -> List[Dict[str, Any]]:
        """텍스트 메모 파일을 읽고 파싱하여 memos 형식으로 변환"""
        memo_content = await self.read_text_memo_file(memo_path)
        if not memo_content:
            return []
        
        # 메모 내용 파싱
        parsed_data = self.parse_memo_content(memo_content)
        
        # 파싱된 데이터를 리스트로 변환 (각 key-value 쌍을 하나의 메모로)
        memos = []
        for key, value in parsed_data.items():
            memos.append({
                "type": "text",
                "key": key,
                "content": value,
                "source": memo_path.name,
                "file_path": str(memo_path.absolute()),
                "file_size": memo_path.stat().st_size if memo_path.exists() else 0
            })
        
        return memos

    async def process_all_memo_files(self, directory: Path) -> List[Dict[str, Any]]:
        """
        디렉토리 내 메모 파일을 찾아서 처리하고 memos 형식으로 변환 (단일 파일)
        
        Returns:
            List[Dict[str, Any]]: 처리된 메모 파일의 리스트
        """
        memo_file = self.find_memo_file(directory)
        
        if memo_file:
            try:
                memos = await self.process_text_memo_file(memo_file)
                if memos:
                    logger.info(f"메모 파일 처리 완료: {memo_file}, {len(memos)}개의 메모 항목")
                    return memos
            except Exception as e:
                logger.warning(f"메모 파일 처리 실패: {memo_file}, 오류: {str(e)}")
        else:
            logger.debug(f"메모 파일을 찾을 수 없습니다: {directory}")
        
        return []

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
        timestamp = get_current_datetime().strftime("%Y%m%d_%H%M%S")
        name, _ = os.path.splitext(original_filename)
        return f"{timestamp}_{name}"

    def generate_filename(self, original_filename: str) -> str:
        """고유한 파일명 생성 (설정된 시간대 사용)"""
        timestamp = get_current_datetime().strftime("%Y%m%d_%H%M%S")
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
        group_id: Optional[str] = None,
        group_name: Optional[str] = None
    ) -> AsyncGenerator[Dict[str, Any], None]:
        """진행률 콜백과 함께 파일 업로드"""
        
        # 파일 검증
        if not file.filename:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail="파일명이 없습니다"
            )
        
        await self.validate_file_extension(file.filename)
        upload_dir = await self.ensure_group_upload_directory(group_name)
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
                "original_file_path": str(file_path),
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

    async def upload_file(self, file: UploadFile, group_id: Optional[str] = None, group_name: Optional[str] = None, model_type: Optional[str] = None) -> Dict[str, Any]:
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
            group_upload_dir = await self.ensure_group_upload_directory(group_name)
            
            # 파일 포인터를 처음으로 리셋 (이전에 읽혔을 수 있음)
            await file.seek(0)
            
            # 파일 확장자 확인
            file_extension = Path(file.filename).suffix.lower()
            
            # zip 파일인 경우 별도 처리
            if file_extension == '.zip':
                return await self._upload_zip_file(file, group_upload_dir, group_id, group_name, model_type)
            
            # 일반 파일 업로드: storage/uploads/{group_name}/{날짜_파일명}/파일명 형태
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
                "original_file_path": str(file_path.absolute()),
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
            
            # obj 파일인지 확인 (단일 파일 업로드의 경우)
            file_extension = Path(file.filename).suffix.lower()
            is_obj_file = file_extension == '.obj'
            
            # obj 파일이 아니면 스캔 생성하지 않고 웹소켓 전송도 하지 않음
            if not is_obj_file:
                logger.info(f"[파일 검증] obj 파일이 아니므로 스캔 생성 및 AI 처리 스킵: {file.filename} (확장자: {file_extension})")
                upload_result["skipped"] = True
                upload_result["skip_reason"] = f"obj 파일이 아닙니다 (확장자: {file_extension})"
                return upload_result
            
            # obj 파일인 경우에만 스캔 정보 DB에 생성
            scan_info = await self._create_scan_for_upload(
                group_id=group_id,
                original_file_path=str(file_path.absolute()),
                original_filename=file.filename,
                file_size=total_size,
                model_type=model_type
            )
            
            # 스캔 정보를 업로드 결과에 추가
            if scan_info:
                upload_result["scan_id"] = scan_info.get("scan_id")
                scan_id_for_websocket = scan_info.get("scan_id", 0)
                
                # 메모 파일 처리 (스캔 생성 후) - 텍스트 및 음성 메모 모두 처리
                if scan_id_for_websocket != 0:
                    try:
                        memos = await self.process_all_memo_files(upload_folder)
                        if memos:
                            # DB 세션 생성
                            db = SessionLocal()
                            try:
                                from app.schemas.scan import ScanUpdate
                                scan_update = ScanUpdate(memos=memos)
                                update_response = scan_service.update_scan(
                                    db=db, 
                                    scan_id=scan_id_for_websocket, 
                                    scan_data=scan_update
                                )
                                if update_response.success:
                                    logger.info(f"메모 파일 {len(memos)}개가 스캔(scan_id={scan_id_for_websocket})에 저장되었습니다.")
                                    upload_result["memos"] = memos
                                    upload_result["memos_count"] = len(memos)
                                else:
                                    logger.warning(f"메모 파일 저장 실패: {update_response.message}")
                            except Exception as e:
                                logger.error(f"메모 파일 저장 중 오류 발생: {str(e)}", exc_info=True)
                            finally:
                                db.close()
                    except Exception as e:
                        # 메모 파일 처리 실패는 업로드 자체를 실패시키지 않음
                        logger.warning(f"메모 파일 처리 중 오류 발생 (업로드는 성공): {str(e)}")
            else:
                scan_id_for_websocket = 0
                logger.warning(f"스캔 생성 실패로 인해 scan_id=0으로 처리됨. group_id={group_id}")
            
            # group_id 처리 (기본값 "1" 사용)
            group_id_str = group_id if group_id else "1"
            
            # 웹소켓으로 업로드된 파일 정보 전송 (obj 파일인 경우에만)
            # scan_id가 0이면 스캔이 생성되지 않았으므로 전송하지 않음
            if scan_id_for_websocket == 0:
                logger.warning(f"scan_id가 0이므로 웹소켓 전송을 스킵합니다. group_id={group_id}, file={file.filename}")
            else:
                try:
                    file_info = [{
                        "scan_id": scan_id_for_websocket,  # 정수로 전송
                        "original_file_path": str(file_path.absolute()),
                        "group_id": group_id_str,
                        "group_name": group_name,  # group_name 추가
                        "model_type": model_type,  # model_type 추가
                        "metadata": {
                            "original_filename": file.filename,
                            "saved_filename": filename,
                            "file_size": total_size,
                            "timestamp": get_current_datetime().isoformat()
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

    async def _upload_zip_file(self, file: UploadFile, group_upload_dir: Path, group_id: Optional[str] = None, group_name: Optional[str] = None, model_type: Optional[str] = None) -> Dict[str, Any]:
        """zip 파일 업로드 및 압축 해제 처리"""
        # 업로드 폴더 생성: storage/uploads/{group_name}/{날짜_파일명}
        folder_name = self.generate_folder_name(file.filename)
        upload_folder = group_upload_dir / folder_name
        upload_folder.mkdir(parents=True, exist_ok=True)
        
        # 임시 zip 파일 저장 경로 (업로드 폴더 내부)
        temp_zip_filename = f"temp_{get_current_datetime().strftime('%Y%m%d_%H%M%S')}.zip"
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
            
            # .obj 파일 찾기
            obj_files = self.find_obj_files(upload_folder)
            
            # obj 파일이 없으면 스캔 생성하지 않고 웹소켓 전송도 하지 않음
            if not obj_files:
                logger.info(f"[파일 검증] zip 파일 압축 해제 후 obj 파일이 없으므로 스캔 생성 및 AI 처리 스킵: {file.filename} (전체 파일 수: {len(all_files)})")
                result = {
                    "original_filename": file.filename,
                    "file_size": total_size,
                    "folder_path": str(upload_folder.absolute()),
                    "files_count": len(all_files),
                    "obj_files_count": 0,
                    "files": [str(f.absolute()) for f in all_files],
                    "obj_files": [],
                    "upload_success": True,
                    "is_zip": True,
                    "group_id": group_id,
                    "skipped": True,
                    "skip_reason": "압축 해제된 파일 중 obj 파일이 없습니다"
                }
                return result
            
            # obj 파일이 있는 경우에만 스캔 정보 DB에 생성 (zip 파일 전체를 하나의 스캔으로 처리)
            # 대표 파일 경로로 스캔 생성 (첫 번째 파일 또는 폴더 경로)
            representative_file_path = str(upload_folder.absolute())
            scan_info = await self._create_scan_for_upload(
                group_id=group_id,
                original_file_path=representative_file_path,
                original_filename=file.filename,
                file_size=total_size,
                model_type=model_type
            )
            
            # 결과 딕셔너리 초기화 (메모 파일 처리에서 사용하기 위해 미리 생성)
            result = {
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
            
            # 스캔 ID 설정 (스캔 생성 실패 시 0 사용)
            if scan_info:
                scan_id_for_websocket = scan_info.get("scan_id", 0)
                result["scan_id"] = scan_id_for_websocket
                
                # 메모 파일 처리 (스캔 생성 후) - 텍스트 및 음성 메모 모두 처리
                if scan_id_for_websocket != 0:
                    try:
                        memos = await self.process_all_memo_files(upload_folder)
                        if memos:
                            # DB 세션 생성
                            db = SessionLocal()
                            try:
                                from app.schemas.scan import ScanUpdate
                                scan_update = ScanUpdate(memos=memos)
                                update_response = scan_service.update_scan(
                                    db=db, 
                                    scan_id=scan_id_for_websocket, 
                                    scan_data=scan_update
                                )
                                if update_response.success:
                                    logger.info(f"메모 파일 {len(memos)}개가 스캔(scan_id={scan_id_for_websocket})에 저장되었습니다.")
                                    result["memos"] = memos
                                    result["memos_count"] = len(memos)
                                else:
                                    logger.warning(f"메모 파일 저장 실패: {update_response.message}")
                            except Exception as e:
                                logger.error(f"메모 파일 저장 중 오류 발생: {str(e)}", exc_info=True)
                            finally:
                                db.close()
                    except Exception as e:
                        # 메모 파일 처리 실패는 업로드 자체를 실패시키지 않음
                        logger.warning(f"메모 파일 처리 중 오류 발생 (업로드는 성공): {str(e)}")
            else:
                scan_id_for_websocket = 0
                logger.warning(f"스캔 생성 실패로 인해 scan_id=0으로 처리됨. group_id={group_id}")
            
            # group_id 처리 (기본값 "1" 사용)
            group_id_str = group_id if group_id else "1"
            
            # 웹소켓으로 파일 정보 전송 (obj 파일만 전송, scan_id가 0이 아닌 경우에만)
            file_info = []
            if scan_id_for_websocket != 0:
                for file_path in obj_files:  # obj 파일만 전송
                    try:
                        file_size = file_path.stat().st_size if file_path.exists() else 0
                        relative_path = str(file_path.relative_to(upload_folder))
                    except Exception:
                        file_size = 0
                        relative_path = file_path.name
                    
                    file_info.append({
                        "scan_id": scan_id_for_websocket,  # 정수로 전송
                        "original_file_path": str(file_path.absolute()),
                        "group_id": group_id_str,
                        "group_name": group_name,  # group_name 추가
                        "model_type": model_type,  # model_type 추가
                        "metadata": {
                            "original_filename": file.filename,
                            "extracted_from_zip": True,
                            "obj_file": file_path.name,
                            "relative_path": relative_path,
                            "file_size": file_size,
                            "timestamp": get_current_datetime().isoformat()
                        }
                    })
                
                try:
                    if file_info:
                        await websocket_manager.send_file_list(file_info)
                except Exception as e:
                    logger.warning(f"Failed to send zip file upload notification via websocket: {e}")
            else:
                logger.warning(f"scan_id가 0이므로 웹소켓 전송을 스킵합니다. group_id={group_id}, zip_file={file.filename}")
            
            return result
            
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
