from fastapi import APIRouter, File, UploadFile, Form, Depends, HTTPException
from typing import Optional
from sqlalchemy.orm import Session
import logging
from app.schemas.base import BaseResponse
from app.schemas.group import GroupCreate
from app.services.upload_service import upload_service
from app.services.group_service import group_service
from app.utils.dependencies import get_db
from app.config import settings

logger = logging.getLogger(__name__)

router = APIRouter()


@router.get(
    "/",
    response_model=BaseResponse,
    summary="업로드 디렉토리 정보 조회",
    description="파일이 저장되는 디렉토리 경로 정보를 반환합니다.",
    responses={
        200: {
            "description": "업로드 디렉토리 정보 조회 성공",
            "content": {
                "application/json": {
                    "example": {
                        "message": "업로드 디렉토리 정보",
                        "success": True,
                        "data": {
                            "upload_dir": "C:storage\\uploads",
                            "max_file_size": 262144000,
                            "max_file_size_mb": 250,
                            "allowed_extensions": [".ply", ".obj", ".stl", ".3ds", ".dae", ".x3d", ".fbx", ".glb", ".gltf"]
                        }
                    }
                }
            }
        }
    }
)
async def root():
    """업로드 디렉토리 정보 및 저장된 파일 목록을 반환하는 API"""
    from pathlib import Path
    from datetime import datetime
    
    upload_dir = Path(settings.UPLOAD_DIR)
    
    # 절대 경로 얻기 (resolve를 사용하여 심볼릭 링크도 해석)
    upload_dir_absolute = upload_dir.resolve()
    
    # 저장된 파일 목록 조회
    uploaded_files = []
    if upload_dir.exists() and upload_dir.is_dir():
        for file_path in upload_dir.iterdir():
            if file_path.is_file():
                stat = file_path.stat()
                file_absolute = file_path.resolve()
                try:
                    relative_path = str(file_path.relative_to(upload_dir_absolute))
                except ValueError:
                    relative_path = None
                
                uploaded_files.append({
                    "filename": file_path.name,
                    "size": stat.st_size,
                    "size_mb": round(stat.st_size / (1024 * 1024), 2),
                    "created_at": datetime.fromtimestamp(stat.st_ctime).isoformat(),
                    "modified_at": datetime.fromtimestamp(stat.st_mtime).isoformat(),
                    "absolute_path": str(file_absolute),
                    "relative_path": relative_path
                })
    
    # 파일 생성일자 순으로 정렬 (최신 순)
    uploaded_files.sort(key=lambda x: x["created_at"], reverse=True)
    
    return BaseResponse(
        message="업로드 디렉토리 정보",
        data={
            "upload_dir": str(upload_dir_absolute),
            "upload_dir_exists": upload_dir.exists(),
            "max_file_size": settings.MAX_FILE_SIZE,
            "max_file_size_mb": settings.MAX_FILE_SIZE // (1024 * 1024),
            "allowed_extensions": settings.ALLOWED_EXTENSIONS,
            "uploaded_files_count": len(uploaded_files),
            "uploaded_files": uploaded_files
        }
    )


@router.post(
    "/",
    response_model=BaseResponse,
    summary="파일 업로드",
    description="150-200MB 크기의 3D 모델 파일을 안전하게 업로드합니다.",
    responses={
        200: {
            "description": "파일 업로드 성공",
            "content": {
                "application/json": {
                    "example": {
                        "message": "파일 업로드가 완료되었습니다",
                        "success": True,
                        "data": {
                            "original_filename": "model.ply",
                            "saved_filename": "20241027_143025_model.ply",
                            "file_size": 157286400,
                            "original_file_path": "BackEnd/uploads/20241027_143025_model.ply"
                        }
                    }
                }
            }
        },
        400: {"description": "잘못된 파일 형식"},
        413: {"description": "파일 크기 초과"},
        500: {"description": "서버 오류"}
    }
)
async def upload_file(
    file: UploadFile = File(...),
    group_name: Optional[str] = Form(None),
    db: Session = Depends(get_db)
):
    """파일 업로드"""
    group_id = "1"  # 기본값
    group_name_to_use = None
    
    # group_name이 있으면 그룹을 찾거나 생성
    if group_name and group_name.strip():
        group_name_clean = group_name.strip()
        group_name_to_use = group_name_clean
        logger.info(f"그룹 검색 시작: group_name='{group_name_clean}'")
        
        try:
            # name 컬럼으로 직접 검색
            found_group = group_service.get_group_by_name(db=db, name=group_name_clean)
            
            if found_group:
                # 기존 그룹이 있으면 해당 group_id 사용
                group_id = str(found_group.group_id)
                logger.info(f"기존 그룹 발견: group_id={group_id}, group_name='{group_name_clean}'")
            else:
                # 그룹이 없으면 새로 생성
                logger.info(f"그룹을 찾지 못함. 새 그룹 생성: group_name='{group_name_clean}'")
                new_group = group_service.create_group(
                    db=db,
                    group_data=GroupCreate(
                        name=group_name_clean,
                        meta_data={}  # 빈 메타데이터로 생성
                    )
                )
                if new_group.success and new_group.data:
                    group_id = str(new_group.data.group_id)
                    logger.info(f"새 그룹 생성 완료: group_id={group_id}, group_name='{group_name_clean}'")
                else:
                    logger.warning(f"그룹 생성 실패: {new_group}")
        except HTTPException as e:
            # 그룹 이름 중복 등의 HTTP 예외는 로그만 남기고 기본값 사용
            logger.warning(f"그룹 생성 중 HTTP 예외: {e.detail}")
        except Exception as e:
            # 그룹 처리 실패 시 기본값 1 사용 (업로드는 계속 진행)
            logger.error(f"그룹 처리 중 오류 발생: {str(e)}", exc_info=True)
    else:
        # group_name이 없으면 기본 그룹(1)의 이름을 DB에서 조회
        try:
            default_group = group_service.get_group(db=db, group_id=1)
            if default_group.success and default_group.data:
                group_name_to_use = default_group.data.name
                logger.info(f"기본 그룹 이름 조회: group_name='{group_name_to_use}'")
        except Exception as e:
            # 기본 그룹 조회 실패 시 None 유지 (default 디렉토리 사용)
            logger.warning(f"기본 그룹 이름 조회 실패: {str(e)}")
    
    # 서비스 레이어에서 파일 업로드 처리
    upload_result = await upload_service.upload_file(file, group_id=group_id, group_name=group_name_to_use)
    
    # 응답에 group_id와 group_name 추가
    try:
        upload_result["group_id"] = int(group_id)
    except (ValueError, TypeError):
        upload_result["group_id"] = None
    
    if group_name and group_name.strip():
        upload_result["group_name"] = group_name.strip()
    
    return BaseResponse(
        message="파일 업로드가 완료되었습니다",
        data=upload_result
    )