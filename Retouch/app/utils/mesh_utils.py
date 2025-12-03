"""
메쉬 파일 통계 계산 유틸리티
- OBJ 파일의 face 수, vertex 수 계산
- 파일 크기 계산
"""
import os
import logging
from pathlib import Path
from typing import Optional, Tuple

logger = logging.getLogger(__name__)


def get_mesh_stats(file_path: Path) -> Optional[Tuple[int, int, int]]:
    """
    OBJ 파일의 통계 정보를 계산합니다.
    
    Args:
        file_path: OBJ 파일 경로
        
    Returns:
        (vertex_count, face_count, file_size_bytes) 튜플 또는 None (실패 시)
    """
    try:
        if not file_path.exists():
            logger.warning(f"파일이 존재하지 않습니다: {file_path}")
            return None
        
        file_size = file_path.stat().st_size
        
        # Open3D를 사용하여 메쉬 로드 시도
        try:
            import open3d as o3d
            mesh = o3d.io.read_triangle_mesh(str(file_path), enable_post_processing=False)
            
            if mesh.is_empty():
                # Open3D로 읽기 실패 시 수동 파싱 시도
                return _parse_obj_manually(file_path, file_size)
            
            vertex_count = len(mesh.vertices)
            face_count = len(mesh.triangles)
            
            return (vertex_count, face_count, file_size)
        except ImportError:
            # Open3D가 없으면 수동 파싱
            logger.debug("Open3D를 사용할 수 없어 수동 파싱을 시도합니다.")
            return _parse_obj_manually(file_path, file_size)
        except Exception as e:
            logger.warning(f"Open3D로 메쉬 로드 실패, 수동 파싱 시도: {e}")
            return _parse_obj_manually(file_path, file_size)
            
    except Exception as e:
        logger.error(f"메쉬 통계 계산 실패: {file_path}, 오류: {e}")
        return None


def _parse_obj_manually(file_path: Path, file_size: int) -> Optional[Tuple[int, int, int]]:
    """
    OBJ 파일을 수동으로 파싱하여 vertex와 face 수를 계산합니다.
    
    Args:
        file_path: OBJ 파일 경로
        file_size: 파일 크기 (bytes)
        
    Returns:
        (vertex_count, face_count, file_size_bytes) 튜플 또는 None
    """
    try:
        vertex_count = 0
        face_count = 0
        
        with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith('#'):
                    continue
                
                parts = line.split()
                if not parts:
                    continue
                
                if parts[0] == 'v':
                    # vertex
                    vertex_count += 1
                elif parts[0] == 'f':
                    # face
                    face_count += 1
        
        return (vertex_count, face_count, file_size)
    except Exception as e:
        logger.error(f"OBJ 파일 수동 파싱 실패: {file_path}, 오류: {e}")
        return None


def format_file_size(size_bytes: int) -> str:
    """
    파일 크기를 읽기 쉬운 형식으로 변환합니다.
    
    Args:
        size_bytes: 바이트 단위 크기
        
    Returns:
        포맷된 문자열 (예: "1.5 MB", "500 KB")
    """
    if size_bytes < 1024:
        return f"{size_bytes} B"
    elif size_bytes < 1024 * 1024:
        return f"{size_bytes / 1024:.2f} KB"
    elif size_bytes < 1024 * 1024 * 1024:
        return f"{size_bytes / (1024 * 1024):.2f} MB"
    else:
        return f"{size_bytes / (1024 * 1024 * 1024):.2f} GB"

