"""
AI 파이프라인 통합 서비스
- mesh_optimizer.py 래핑
- mesh_denoiser.py 래핑
- 진행률 콜백 지원
"""

import os
import sys
import subprocess
import logging
from typing import Callable, Optional
from pathlib import Path

# AI 파이프라인 모듈 경로 추가
AI_PIPELINE_PATH = Path(__file__).parent.parent.parent / "ai_pipeline"
sys.path.insert(0, str(AI_PIPELINE_PATH))

logger = logging.getLogger(__name__)


class AIPipeline:
    """AI 파이프라인 통합 클래스"""
    
    def __init__(self, progress_callback: Optional[Callable[[int, str], None]] = None):
        """
        Args:
            progress_callback: 진행률 콜백 함수 (progress: int, message: str) -> None
        """
        self.progress_callback = progress_callback
        self.polygon_path = AI_PIPELINE_PATH / "Polygon"
    
    def _update_progress(self, progress: int, message: str):
        """진행률 업데이트"""
        if self.progress_callback:
            self.progress_callback(progress, message)
        logger.info(f"[AI Pipeline] {progress}% - {message}")
    
    def run_mesh_optimizer(self, input_path: str, output_dir: str) -> str:
        """
        mesh_optimizer.py 실행
        
        Args:
            input_path: 입력 OBJ 파일 경로
            output_dir: 출력 디렉토리
            
        Returns:
            출력 파일 경로
        """
        self._update_progress(10, "메쉬 최적화 시작...")
        
        # mesh_optimizer.py 실행
        cmd = [
            "python", 
            str(self.polygon_path / "mesh_optimizer.py"),
            "--input", input_path
        ]
        
        try:
            result = subprocess.run(
                cmd,
                cwd=str(self.polygon_path),
                capture_output=True,
                text=True,
                check=True
            )
            
            # 출력 파일 경로 계산 (mesh_optimizer는 {input}_cleaned.obj로 저장)
            base_name = os.path.splitext(os.path.basename(input_path))[0]
            output_file = os.path.join(output_dir, f"{base_name}_cleaned.obj")
            
            # 임시 파일을 최종 출력 디렉토리로 이동
            temp_output = input_path.replace(".obj", "_cleaned.obj")
            if os.path.exists(temp_output):
                os.rename(temp_output, output_file)
                self._update_progress(30, f"메쉬 최적화 완료: {output_file}")
                return output_file
            else:
                raise FileNotFoundError(f"메쉬 최적화 결과 파일을 찾을 수 없습니다: {temp_output}")
                
        except subprocess.CalledProcessError as e:
            error_msg = f"메쉬 최적화 실패: {e.stderr}"
            logger.error(error_msg)
            raise RuntimeError(error_msg)
    
    def run_mesh_denoiser(self, input_path: str, output_dir: str) -> str:
        """
        mesh_denoiser.py 실행 (auto_flat 모드)
        
        Args:
            input_path: 입력 OBJ 파일 경로
            output_dir: 출력 디렉토리
            
        Returns:
            출력 파일 경로
        """
        self._update_progress(50, "메쉬 노이즈 제거 시작...")
        
        # mesh_denoiser.py 실행 (auto_flat 모드)
        cmd = [
            "python",
            str(self.polygon_path / "mesh_denoiser.py"),
            "-i", input_path,
            "--mode", "auto_flat",
            "--proj-dist", "0.008",
            "--floor-ratio", "0.5",
            "--wall-ratio", "2.0",
            "--smooth-floor", "6",
            "--smooth-wall", "24",
            "--wall-ortho-dot", "0.15",
            "--max-walls", "6",
            "--ransac-iters", "4000",
            "--preclean"
            # --visualize 제거 (서버 환경에서 시각화 불가)
        ]
        
        try:
            result = subprocess.run(
                cmd,
                cwd=str(self.polygon_path),
                capture_output=True,
                text=True,
                check=True
            )
            
            # 출력 파일 경로 계산 (mesh_denoiser는 {input}_auto_flat.obj로 저장)
            base_name = os.path.splitext(os.path.basename(input_path))[0]
            output_file = os.path.join(output_dir, f"{base_name}_auto_flat.obj")
            
            # 임시 파일을 최종 출력 디렉토리로 이동
            temp_output = input_path.replace(".obj", "_auto_flat.obj")
            if os.path.exists(temp_output):
                os.rename(temp_output, output_file)
                self._update_progress(80, f"메쉬 노이즈 제거 완료: {output_file}")
                return output_file
            else:
                raise FileNotFoundError(f"메쉬 노이즈 제거 결과 파일을 찾을 수 없습니다: {temp_output}")
                
        except subprocess.CalledProcessError as e:
            error_msg = f"메쉬 노이즈 제거 실패: {e.stderr}"
            logger.error(error_msg)
            raise RuntimeError(error_msg)
    
    def run_full_pipeline(self, input_path: str, output_dir: str) -> str:
        """
        전체 AI 파이프라인 실행
        
        Args:
            input_path: 입력 OBJ 파일 경로
            output_dir: 출력 디렉토리
            
        Returns:
            최종 출력 파일 경로
        """
        self._update_progress(0, "AI 파이프라인 시작...")
        
        try:
            # 1단계: 메쉬 최적화
            optimized_path = self.run_mesh_optimizer(input_path, output_dir)
            
            # 2단계: 메쉬 노이즈 제거
            final_path = self.run_mesh_denoiser(optimized_path, output_dir)
            
            self._update_progress(100, f"AI 파이프라인 완료: {final_path}")
            return final_path
            
        except Exception as e:
            error_msg = f"AI 파이프라인 실행 중 오류: {str(e)}"
            logger.error(error_msg)
            raise RuntimeError(error_msg)
