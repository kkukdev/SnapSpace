from fastapi import APIRouter, File, UploadFile
from app.schemas.base import BaseResponse
from app.services.upload_service import upload_service
from app.config import settings

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
                            "file_path": "BackEnd/uploads/20241027_143025_model.ply"
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
async def upload_file(file: UploadFile = File(...)):
    """파일 업로드"""
    # 서비스 레이어에서 파일 업로드 처리
    upload_result = await upload_service.upload_file(file)
    
    return BaseResponse(
        message="파일 업로드가 완료되었습니다",
        data=upload_result
    )