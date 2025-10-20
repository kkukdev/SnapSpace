from fastapi import APIRouter

from app.api.api_v1 import items, utils
from app.core.config import settings

api_v1_router = APIRouter(prefix=settings.API_V1_STR)
api_v1_router.include_router(utils.router)
api_v1_router.include_router(items.router)
