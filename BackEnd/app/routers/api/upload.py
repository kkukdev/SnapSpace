from fastapi import APIRouter, File, UploadFile
from app.schemas.base import BaseResponse
from app.services.upload_service import upload_service

router = APIRouter()


@router.get("/", response_model=BaseResponse)
async def root():
    """업로드 라우터의 상태를 확인하기 위한 API"""
    return BaseResponse(message="upload router is alive")


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