"""
AI 파이프라인 통합 서비스
- mesh_optimizer.py 래핑
- mesh_denoiser.py 래핑
- 진행률 콜백 지원
- 디렉토리 정책:
  - 중간 산출물: storage/outputs/polygon
  - 최종 산출물: storage/outputs/final
"""

import os
import sys
import shutil
import logging
import time
from typing import Callable, Optional
from pathlib import Path

# Open3D headless 모드 설정
os.environ["OPEN3D_HEADLESS"] = "1"
os.environ["PYOPENGL_PLATFORM"] = "egl"

# AI 파이프라인 모듈 경로 추가
AI_PIPELINE_PATH = Path(__file__).parent.parent.parent / "ai_pipeline"
sys.path.insert(0, str(AI_PIPELINE_PATH))

from app.services.pipeline import (
    SubprocessError,
    SubprocessTimeoutError,
    execute_subprocess,
    ensure_utf8_copy,
    sanitize_material_assets,
    resolve_pipeline_paths,
    sanitize_filename,
)
from app.utils.mesh_utils import get_mesh_stats, format_file_size

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
        self.pipeline_configs = {
            "space": {
                "optimizer": self.polygon_path / "Space" / "space_optimizer.py",
                "denoiser": self.polygon_path / "Space" / "space_denoiser.py",
                "denoiser_suffix": "_auto_flat.obj",
                "denoiser_extra_args": [],
            },
            "object": {
                "optimizer": self.polygon_path / "Object" / "object_optimizer.py",
                "denoiser": self.polygon_path / "Object" / "object_denoiser.py",
                "denoiser_suffix": "_auto_flat.obj",
                "denoiser_extra_args": ["--mode", "auto_flat"],
            },
        }

    def _get_pipeline_config(self, model_type: str) -> dict:
        """파이프라인 타입에 따른 스크립트/출력 설정 반환"""
        key = (model_type or "space").strip().lower()
        if key not in self.pipeline_configs:
            raise ValueError(f"지원하지 않는 model_type 입니다: {model_type}")
        return self.pipeline_configs[key]
    
    def _update_progress(self, progress: int, message: str):
        """진행률 업데이트"""
        if self.progress_callback:
            self.progress_callback(progress, message)
        logger.info(f"[AI Pipeline] {progress}% - {message}")
    
    def run_mesh_optimizer(self, src_input_path: str, temp_dir: Path, optimized_dir: Path, model_type: str = "space") -> Path:
        """
        mesh_optimizer.py 실행 (네트워크 성능 최적화: 로컬 임시 디렉토리 사용)
        
        Args:
            src_input_path: 업로드된 원본 OBJ 경로 (uploads)
            temp_dir: 임시 디렉토리 (네트워크)
            optimized_dir: 최적화 결과 디렉토리 (네트워크)
            model_type: 파이프라인 타입 (space/object)
            
        Returns:
            optimized_dir에 저장된 *_cleaned.obj 경로
        """
        self._update_progress(10, "메쉬 최적화 시작...")
        pipeline_config = self._get_pipeline_config(model_type)
        
        from app.config import settings
        import tempfile
        
        # 보정 전 성능 지표 수집
        start_time = time.time()
        input_stats = get_mesh_stats(Path(src_input_path))
        if input_stats:
            input_vertices, input_faces, input_size = input_stats
            logger.info(f"[Optimizer] 보정 전 - Vertices: {input_vertices:,}, Faces: {input_faces:,}, 크기: {format_file_size(input_size)}")
        else:
            input_size = Path(src_input_path).stat().st_size if Path(src_input_path).exists() else 0
            logger.info(f"[Optimizer] 보정 전 - 크기: {format_file_size(input_size)} (통계 계산 실패)")
        
        # 네트워크 경로인지 확인 (UNC 경로 또는 로컬 마운트된 네트워크 경로)
        optimized_dir_str = str(optimized_dir)
        
        # UNC 경로 체크
        is_unc_path = (src_input_path.startswith('\\\\') or src_input_path.startswith('//'))
        # 로컬 마운트된 네트워크 경로 체크 (C:\IP주소\... 또는 C:\hostname\... 패턴)
        is_mounted_network = False
        if not is_unc_path and src_input_path:
            import re
            mounted_pattern = re.compile(r'^[A-Z]:\\([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}|[A-Za-z0-9_-]+)\\')
            if mounted_pattern.match(src_input_path):
                is_mounted_network = True
        
        is_network_path = is_unc_path or is_mounted_network
        
        # optimized_dir도 네트워크 경로인지 확인
        is_optimized_dir_network = False
        if optimized_dir_str.startswith('\\\\') or optimized_dir_str.startswith('//'):
            is_optimized_dir_network = True
        elif optimized_dir_str:
            import re
            mounted_pattern = re.compile(r'^[A-Z]:\\([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}|[A-Za-z0-9_-]+)\\')
            if mounted_pattern.match(optimized_dir_str):
                is_optimized_dir_network = True
        
        # 네트워크 경로인 경우 로컬 임시 디렉토리 생성 (네트워크 I/O 성능 향상)
        if is_network_path or is_optimized_dir_network:
            if settings.local_temp_dir:
                local_temp_base = Path(settings.local_temp_dir)
            else:
                local_temp_base = Path(tempfile.gettempdir()) / "ai_pipeline"
            
            local_temp_base.mkdir(parents=True, exist_ok=True)
            local_temp_dir = Path(tempfile.mkdtemp(prefix="mesh_opt_", dir=str(local_temp_base)))
            local_optimized_dir = local_temp_dir / "optimized"
            local_optimized_dir.mkdir(parents=True, exist_ok=True)
            
            
            # 입력 파일을 로컬로 복사 (한 번만 네트워크 I/O)
            input_filename = Path(src_input_path).name
            local_input_path = local_temp_dir / input_filename
            self._update_progress(12, "입력 파일을 로컬로 복사 중...")
            shutil.copy2(src_input_path, local_input_path)
            
            work_input = local_input_path
            work_temp_dir = local_temp_dir
            work_optimized_dir = local_optimized_dir
        else:
            # 로컬 경로는 그대로 사용
            if src_input_path.startswith('\\\\') or src_input_path.startswith('//'):
                work_input = Path(src_input_path)
            else:
                work_input = Path(src_input_path).resolve()
            work_temp_dir = Path(temp_dir)
            work_optimized_dir = Path(optimized_dir)
            local_temp_dir = None

        # 네트워크 디렉토리도 생성 (최종 결과 복사용)
        optimized_dir.mkdir(parents=True, exist_ok=True)

        # 절대 경로로 변환 (subprocess의 cwd와 관계없이 동작하도록)
        work_input_abs = work_input.resolve() if hasattr(work_input, 'resolve') else Path(work_input).resolve()
        work_temp_dir_abs = work_temp_dir.resolve() if hasattr(work_temp_dir, 'resolve') else Path(work_temp_dir).resolve()
        work_optimized_dir_abs = work_optimized_dir.resolve() if hasattr(work_optimized_dir, 'resolve') else Path(work_optimized_dir).resolve()
        
        cmd = [
            sys.executable,  # 현재 Python 인터프리터 사용 (가상 환경 보장)
            str(pipeline_config["optimizer"]),
            "--input", str(work_input_abs),
            "--temp-dir", str(work_temp_dir_abs),
            "--optimized-dir", str(work_optimized_dir_abs),
        ]

        try:
            execute_subprocess(
                cmd,
                cwd=str(self.polygon_path),
                logger=logger,
                prefix="[Optimizer]",
                timeout_sec=1800,
            )
        except SubprocessTimeoutError:
            logger.error("[Optimizer] 타임아웃 (30분 초과)")
            raise RuntimeError("메쉬 최적화 타임아웃: 30분 초과")
        except SubprocessError as error:
            if error.returncode < 0:
                logger.error(f"[Optimizer] 시그널 종료 감지 (returncode: {error.returncode})")
                raise RuntimeError("메쉬 최적화 중 크래시 발생 (SIGSEGV 가능성). 파일 손상 또는 메모리 부족일 수 있습니다")
            if error.output:
                logger.error(f"[Optimizer] 프로세스 출력:\n{error.output}")
            raise RuntimeError(f"메쉬 최적화 실패: exit code {error.returncode}")
        except Exception as exc:
            error_str = str(exc)
            if "SIGSEGV" in error_str or "died with" in error_str or "Signal" in error_str:
                logger.error("[Optimizer] 세그멘테이션 폴트 발생 (SIGSEGV)")
                logger.error("[Optimizer] 보통 손상된 OBJ, 메모리 부족, PyMeshLab/Open3D 문제로 발생합니다")
                raise RuntimeError("메쉬 최적화 중 크래시 발생 (SIGSEGV). 파일이 손상되었거나 메모리 부족일 수 있습니다")
            logger.error(f"[Optimizer] 예상치 못한 오류: {exc}", exc_info=True)
            raise RuntimeError(f"메쉬 최적화 실패: {exc}")

        try:
            # 최종 cleaned 경로는 work_optimized_dir 기준 (로컬 또는 네트워크)
            # work_input은 이미 절대 경로로 변환되었으므로 with_suffix 사용
            base_stem = work_input_abs.with_suffix("").name
            expected_filename = f"{base_stem}_cleaned.obj"
            safe_base = sanitize_filename(base_stem)
            sanitized_filename = f"{safe_base}_cleaned.obj"

            candidates = [
                work_optimized_dir_abs / expected_filename,
                work_optimized_dir_abs / sanitized_filename,
            ]
            candidates.extend(
                sorted(
                    work_optimized_dir_abs.glob("*_cleaned.obj"),
                    key=lambda p: p.stat().st_mtime,
                    reverse=True,
                )
            )

            cleaned_local = None
            for candidate in candidates:
                if candidate.exists():
                    cleaned_local = candidate
                    break

            if cleaned_local is None:
                raise FileNotFoundError(f"*_cleaned.obj 결과를 찾지 못했습니다 (기대: {work_optimized_dir_abs / expected_filename})")

            cleaned_filename = cleaned_local.name

            # 필요하면 원래 파일명으로 추가 복사 시도 (실패해도 치명적이지 않음)
            if cleaned_filename != expected_filename:
                target_original = work_optimized_dir_abs / expected_filename
                if not target_original.exists():
                    try:
                        shutil.copy2(cleaned_local, target_original)
                    except Exception as copy_error:
                        logger.warning(f"[Optimizer] 원래 파일명 복사 실패 (무시): {copy_error}")

            # 네트워크 경로인 경우 로컬 결과를 네트워크로 복사
            if (is_network_path or is_optimized_dir_network) and local_temp_dir:
                if not cleaned_local.exists():
                    fallback_candidates = [
                        work_optimized_dir_abs / sanitized_filename,
                        work_optimized_dir_abs / expected_filename,
                    ]
                    fallback_candidates.extend(
                        sorted(
                            work_optimized_dir_abs.glob("*_cleaned.obj"),
                            key=lambda p: p.stat().st_mtime,
                            reverse=True,
                        )
                    )
                    recovered = None
                    for candidate in fallback_candidates:
                        if candidate.exists():
                            recovered = candidate
                            break
                    if recovered is not None:
                        logger.warning(f"[Optimizer] 예상 경로에 파일이 없어 대체 경로를 사용합니다: {recovered}")
                        cleaned_local = recovered
                        cleaned_filename = cleaned_local.name
                    else:
                        raise FileNotFoundError(f"로컬 최적화 결과가 없습니다: {cleaned_local}")

                self._update_progress(48, "최종 결과를 네트워크로 복사 중...")
                cleaned_network = optimized_dir / cleaned_filename
                
                # 네트워크 디렉토리 생성 확인
                try:
                    optimized_dir.mkdir(parents=True, exist_ok=True)
                except Exception as e:
                    logger.error(f"네트워크 디렉토리 생성 실패: {optimized_dir}, 오류: {e}")
                    raise
                
                # 파일 복사 및 검증
                try:
                    shutil.copy2(cleaned_local, cleaned_network)
                    # 복사 후 파일 존재 확인
                    if not cleaned_network.exists():
                        raise FileNotFoundError(f"파일 복사 후에도 존재하지 않습니다: {cleaned_network}")
                    
                    # 파일 크기 확인
                    network_size = cleaned_network.stat().st_size if cleaned_network.exists() else 0
                    if network_size == 0:
                        raise FileNotFoundError(f"네트워크 경로에 복사된 파일 크기가 0입니다: {cleaned_network}")
                        
                except Exception as copy_e:
                    logger.error(f"파일 복사 실패: {copy_e}", exc_info=True)
                    # 복사 실패 시 예외를 재발생시켜 명확한 에러 표시
                    raise RuntimeError(f"네트워크 경로로 파일 복사 실패: {cleaned_local} -> {cleaned_network}, 오류: {copy_e}")
                
                # 로컬 임시 디렉토리 정리 (복사 성공 후에만)
                if cleaned_network.exists() and cleaned_network != cleaned_local:
                    try:
                        shutil.rmtree(local_temp_dir)
                    except Exception as e:
                        logger.warning(f"[Optimizer] 로컬 임시 디렉토리 정리 실패 (무시): {e}")
                
                cleaned = cleaned_network
            else:
                cleaned = cleaned_local
            
            
            if not cleaned.exists():
                raise FileNotFoundError(f"메쉬 최적화 결과가 없습니다: {cleaned}")
            
            # 보정 후 성능 지표 수집 및 비교
            elapsed_time = time.time() - start_time
            output_stats = get_mesh_stats(cleaned)
            
            if output_stats:
                output_vertices, output_faces, output_size = output_stats
                logger.info(f"[Optimizer] 보정 후 - Vertices: {output_vertices:,}, Faces: {output_faces:,}, 크기: {format_file_size(output_size)}")
                
                if input_stats:
                    input_vertices, input_faces, input_size = input_stats
                    vertex_change = output_vertices - input_vertices
                    face_change = output_faces - input_faces
                    size_change = output_size - input_size
                    size_change_pct = (size_change / input_size * 100) if input_size > 0 else 0
                    
                    logger.info(f"[Optimizer] 성능 비교 - 처리시간: {elapsed_time:.2f}초, "
                              f"Vertices 변화: {vertex_change:+,} ({output_vertices/input_vertices*100 if input_vertices > 0 else 0:.1f}%), "
                              f"Faces 변화: {face_change:+,} ({output_faces/input_faces*100 if input_faces > 0 else 0:.1f}%), "
                              f"크기 변화: {size_change:+,} bytes ({size_change_pct:+.1f}%)")
                else:
                    logger.info(f"[Optimizer] 성능 비교 - 처리시간: {elapsed_time:.2f}초, 크기: {format_file_size(output_size)}")
            else:
                output_size = cleaned.stat().st_size if cleaned.exists() else 0
                logger.info(f"[Optimizer] 보정 후 - 크기: {format_file_size(output_size)} (통계 계산 실패)")
                logger.info(f"[Optimizer] 성능 비교 - 처리시간: {elapsed_time:.2f}초")
            
            self._update_progress(50, f"메쉬 최적화 완료: {cleaned}")
            return cleaned
        except Exception as exc:
            logger.error(f"[Optimizer] 결과 처리 중 오류: {exc}", exc_info=True)
            raise
    
    def run_mesh_denoiser(self, cleaned_polygon_path: Path, final_dir: Path, model_type: str = "space") -> Path:
        """
        mesh_denoiser.py 실행 (auto_flat 모드, 네트워크 성능 최적화: 로컬 임시 디렉토리 사용)
        
        Args:
            cleaned_polygon_path: polygon 단계 산출물 *_cleaned.obj 경로
            final_dir: 최종 산출물 디렉토리 (네트워크)
            model_type: 파이프라인 타입 (space/object)
            
        Returns:
            final_dir에 저장된 결과 파일 경로
        """
        self._update_progress(50, "메쉬 노이즈 제거 시작...")
        pipeline_config = self._get_pipeline_config(model_type)
        expected_suffix = pipeline_config["denoiser_suffix"]
        
        from app.config import settings
        import tempfile
        
        # 보정 전 성능 지표 수집
        start_time = time.time()
        input_stats = get_mesh_stats(cleaned_polygon_path)
        if input_stats:
            input_vertices, input_faces, input_size = input_stats
            logger.info(f"[Denoiser] 보정 전 - Vertices: {input_vertices:,}, Faces: {input_faces:,}, 크기: {format_file_size(input_size)}")
        else:
            input_size = cleaned_polygon_path.stat().st_size if cleaned_polygon_path.exists() else 0
            logger.info(f"[Denoiser] 보정 전 - 크기: {format_file_size(input_size)} (통계 계산 실패)")
        
        # 네트워크 경로인지 확인 (UNC 경로 또는 로컬 마운트된 네트워크 경로)
        cleaned_input_str = str(cleaned_polygon_path)
        final_dir_str = str(final_dir)
        
        # UNC 경로 체크
        is_unc_path = (cleaned_input_str.startswith('\\\\') or cleaned_input_str.startswith('//'))
        # 로컬 마운트된 네트워크 경로 체크 (C:\IP주소\... 또는 C:\hostname\... 패턴)
        is_mounted_network = False
        if not is_unc_path and cleaned_input_str:
            # IP 주소 패턴 (C:\192.168.1.1\...) 또는 호스트명 패턴 체크
            import re
            mounted_pattern = re.compile(r'^[A-Z]:\\([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}|[A-Za-z0-9_-]+)\\')
            if mounted_pattern.match(cleaned_input_str):
                is_mounted_network = True
        
        is_network_path = is_unc_path or is_mounted_network
        
        # final_dir도 네트워크 경로인지 확인
        is_final_dir_network = False
        if final_dir_str.startswith('\\\\') or final_dir_str.startswith('//'):
            is_final_dir_network = True
        elif final_dir_str:
            mounted_pattern = re.compile(r'^[A-Z]:\\([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}|[A-Za-z0-9_-]+)\\')
            if mounted_pattern.match(final_dir_str):
                is_final_dir_network = True
        
        # 네트워크 경로인 경우 로컬 임시 디렉토리 생성 (네트워크 I/O 성능 향상)
        if is_network_path or is_final_dir_network:
            if settings.local_temp_dir:
                local_temp_base = Path(settings.local_temp_dir)
            else:
                local_temp_base = Path(tempfile.gettempdir()) / "ai_pipeline"
            
            local_temp_base.mkdir(parents=True, exist_ok=True)
            local_temp_dir = Path(tempfile.mkdtemp(prefix="mesh_denoise_", dir=str(local_temp_base)))
            local_output_dir = local_temp_dir / "output"
            local_output_dir.mkdir(parents=True, exist_ok=True)
            
            
            # 입력 파일을 로컬로 복사 (한 번만 네트워크 I/O)
            input_filename = Path(cleaned_polygon_path).name
            local_input_path = local_temp_dir / input_filename
            self._update_progress(52, "입력 파일을 로컬로 복사 중...")
            shutil.copy2(cleaned_polygon_path, local_input_path)
            
            work_input = local_input_path
            work_output_dir = local_output_dir
        else:
            # 로컬 경로는 그대로 사용
            if cleaned_input_str.startswith('\\\\') or cleaned_input_str.startswith('//'):
                work_input = cleaned_polygon_path
            else:
                work_input = cleaned_polygon_path.resolve()
            work_output_dir = Path(final_dir)
            local_temp_dir = None
        
        # 네트워크 디렉토리도 생성 (최종 결과 복사용)
        # final_dir이 네트워크 경로인 경우에만 생성
        if is_final_dir_network or is_network_path:
            try:
                final_dir.mkdir(parents=True, exist_ok=True)
                logger.info(f"[Denoiser] 최종 저장 디렉토리 생성: {final_dir}")
            except Exception as e:
                logger.warning(f"[Denoiser] 최종 저장 디렉토리 생성 실패 (무시): {e}")
        
        # 절대 경로로 변환
        work_input_abs = work_input.resolve() if hasattr(work_input, 'resolve') else Path(work_input).resolve()
        work_output_dir_abs = work_output_dir.resolve() if hasattr(work_output_dir, 'resolve') else Path(work_output_dir).resolve()

        model_type_key = (model_type or "space").strip().lower()
        cmd = [
            sys.executable,
            str(pipeline_config["denoiser"]),
        ]

        if model_type_key == "object":
            cmd.extend([
                "--input", str(work_input_abs),
                "--output-dir", str(work_output_dir_abs),
            ])
            extra_args = pipeline_config.get("denoiser_extra_args") or []
            cmd.extend(str(arg) for arg in extra_args)
        else:
            cmd.extend([
                "-i", str(work_input_abs),
                "--output-dir", str(work_output_dir_abs),
            ])
            extra_args = pipeline_config.get("denoiser_extra_args") or []
            cmd.extend(str(arg) for arg in extra_args)

        try:
            execute_subprocess(
                cmd,
                cwd=str(self.polygon_path),
                logger=logger,
                prefix="[Denoiser]",
                timeout_sec=1800,
            )
        except SubprocessTimeoutError:
            logger.error("[Denoiser] 타임아웃 (30분 초과)")
            raise RuntimeError("메쉬 노이즈 제거 타임아웃: 30분 초과")
        except SubprocessError as error:
            if error.returncode < 0:
                logger.error(f"[Denoiser] 시그널 종료 감지 (returncode: {error.returncode})")
                raise RuntimeError("메쉬 노이즈 제거 중 크래시 발생 (SIGSEGV 가능성). 파일 손상 또는 메모리 부족일 수 있습니다")
            if error.output:
                logger.error(f"[Denoiser] 프로세스 출력:\n{error.output}")
            raise RuntimeError(f"메쉬 노이즈 제거 실패: exit code {error.returncode}")
        except Exception as exc:
            error_str = str(exc)
            if "SIGSEGV" in error_str or "died with" in error_str or "Signal" in error_str:
                logger.error("[Denoiser] 세그멘테이션 폴트 발생 (SIGSEGV)")
                logger.error("[Denoiser] 손상된 OBJ, 메모리 부족, PyMeshLab/Open3D 문제 가능성")
                raise RuntimeError("메쉬 노이즈 제거 중 크래시 발생 (SIGSEGV). 파일이 손상되었거나 메모리 부족일 수 있습니다")
            logger.error(f"[Denoiser] 예상치 못한 오류: {exc}", exc_info=True)
            raise RuntimeError(f"메쉬 노이즈 제거 실패: {exc}")

        try:
            # 최종 파일 경로 (base_stem은 원본 파일명에서 확장자 제거)
            # 기본 모드는 입력 파일과 같은 디렉토리에 저장하므로, 임시 디렉토리에서 찾기
            base_stem = work_input_abs.with_suffix("").name
            expected_filename = f"{base_stem}{expected_suffix}"
            safe_base = sanitize_filename(base_stem)
            sanitized_filename = f"{safe_base}{expected_suffix}"
            final_dir.mkdir(parents=True, exist_ok=True)

            candidates = [
                work_output_dir_abs / expected_filename,
                work_output_dir_abs / sanitized_filename,
            ]
            candidates.extend(
                sorted(
                    work_output_dir_abs.glob(f"*{expected_suffix}"),
                    key=lambda p: p.stat().st_mtime,
                    reverse=True,
                )
            )

            final_output_local = None
            for candidate in candidates:
                if candidate.exists():
                    final_output_local = candidate
                    break

            if final_output_local is None:
                raise FileNotFoundError(f"메쉬 노이즈 제거 결과를 찾지 못했습니다 (기대: {work_output_dir_abs / expected_filename})")

            output_filename = final_output_local.name

            if output_filename != expected_filename:
                target_original = work_output_dir_abs / expected_filename
                if not target_original.exists():
                    try:
                        shutil.copy2(final_output_local, target_original)
                    except Exception as copy_error:
                        logger.warning(f"[Denoiser] 원래 파일명 복사 실패 (무시): {copy_error}")

            if (is_network_path or is_final_dir_network) and local_temp_dir:
                if not final_output_local.exists():
                    fallback_candidates = [
                        work_output_dir_abs / sanitized_filename,
                        work_output_dir_abs / expected_filename,
                    ]
                    fallback_candidates.extend(
                        sorted(
                            work_output_dir_abs.glob(f"*{expected_suffix}"),
                            key=lambda p: p.stat().st_mtime,
                            reverse=True,
                        )
                    )
                    recovered = None
                    for candidate in fallback_candidates:
                        if candidate.exists():
                            recovered = candidate
                            break
                    if recovered is not None:
                        logger.warning(f"[Denoiser] 예상 경로에 파일이 없어 대체 경로를 사용합니다: {recovered}")
                        final_output_local = recovered
                        output_filename = final_output_local.name
                    else:
                        raise FileNotFoundError(f"로컬 노이즈 제거 결과가 없습니다: {final_output_local}")

                self._update_progress(78, "최종 결과를 네트워크로 복사 중...")

                # 네트워크 디렉토리 접근성 점검
                try:
                    final_dir.mkdir(parents=True, exist_ok=True)
                    if not final_dir.exists():
                        raise FileNotFoundError(f"디렉토리가 생성되지 않았습니다: {final_dir}")
                except Exception as e:
                    logger.error(f"[Denoiser] 네트워크 디렉토리 생성 실패: {final_dir}, 오류: {e}")
                    raise

                final_output_network = final_dir / output_filename

                try:
                    shutil.copy2(final_output_local, final_output_network)

                    if not final_output_network.exists():
                        raise FileNotFoundError(f"파일 복사 후에도 존재하지 않습니다: {final_output_network}")

                    # 파일 크기 확인
                    network_size = final_output_network.stat().st_size if final_output_network.exists() else 0
                    if network_size == 0:
                        raise FileNotFoundError(f"네트워크 경로에 복사된 파일 크기가 0입니다: {final_output_network}")

                except Exception as copy_e:
                    logger.error(f"파일 복사 실패: {copy_e}", exc_info=True)
                    raise RuntimeError(f"네트워크 경로로 파일 복사 실패: {final_output_local} -> {final_output_network}, 오류: {copy_e}")

                if final_output_network.exists() and final_output_network != final_output_local:
                    try:
                        shutil.rmtree(local_temp_dir)
                    except Exception as e:
                        logger.warning(f"[Denoiser] 로컬 임시 디렉토리 정리 실패 (무시): {e}")

                final_output = final_output_network
            else:
                final_output = final_dir / output_filename
                source_candidate = work_output_dir_abs / output_filename
                if not final_output.exists() and source_candidate.exists() and source_candidate != final_output:
                    shutil.copy2(source_candidate, final_output)


            if not final_output.exists():
                raise FileNotFoundError(f"메쉬 노이즈 제거 결과가 없습니다: {final_output}")

            # 보정 후 성능 지표 수집 및 비교
            elapsed_time = time.time() - start_time
            output_stats = get_mesh_stats(final_output)
            
            if output_stats:
                output_vertices, output_faces, output_size = output_stats
                logger.info(f"[Denoiser] 보정 후 - Vertices: {output_vertices:,}, Faces: {output_faces:,}, 크기: {format_file_size(output_size)}")
                
                if input_stats:
                    input_vertices, input_faces, input_size = input_stats
                    vertex_change = output_vertices - input_vertices
                    face_change = output_faces - input_faces
                    size_change = output_size - input_size
                    size_change_pct = (size_change / input_size * 100) if input_size > 0 else 0
                    
                    logger.info(f"[Denoiser] 성능 비교 - 처리시간: {elapsed_time:.2f}초, "
                              f"Vertices 변화: {vertex_change:+,} ({output_vertices/input_vertices*100 if input_vertices > 0 else 0:.1f}%), "
                              f"Faces 변화: {face_change:+,} ({output_faces/input_faces*100 if input_faces > 0 else 0:.1f}%), "
                              f"크기 변화: {size_change:+,} bytes ({size_change_pct:+.1f}%)")
                else:
                    logger.info(f"[Denoiser] 성능 비교 - 처리시간: {elapsed_time:.2f}초, 크기: {format_file_size(output_size)}")
            else:
                output_size = final_output.stat().st_size if final_output.exists() else 0
                logger.info(f"[Denoiser] 보정 후 - 크기: {format_file_size(output_size)} (통계 계산 실패)")
                logger.info(f"[Denoiser] 성능 비교 - 처리시간: {elapsed_time:.2f}초")

            self._update_progress(80, f"메쉬 노이즈 제거 완료: {final_output}")
            return final_output
        except Exception as exc:
            logger.error(f"[Denoiser] 결과 처리 중 오류: {exc}", exc_info=True)
            raise
    
    def run_texture_pipeline(
        self,
        hi_input_path: Path,
        lo_input_path: Path,
        texture_dir: Path,
        texture_output_path: Path,
    ) -> Path:
        """
        texture_pipeline.py 실행 (하이/로우 모델 기반 텍스처 보정)
        
        Args:
            hi_input_path: 원본 고해상도 OBJ/GLB 경로 (--hi)
            lo_input_path: 메쉬 보정 완료 OBJ/GLB 경로 (--lo)
            texture_dir: 텍스처 파이프라인 산출물 디렉토리 (--outdir)
            texture_output_path: 텍스처 파이프라인 최종 결과 GLB 경로 (--out)
        
        Returns:
            texture_output_path에 저장된 결과 파일 경로
        """
        self._update_progress(82, "텍스처 파이프라인 준비 중...")
        from app.config import settings
        
        pipeline_script = AI_PIPELINE_PATH / "Texture" / "texture_pipeline.py"
        if not pipeline_script.exists():
            raise FileNotFoundError(f"texture_pipeline.py를 찾을 수 없습니다: {pipeline_script}")
        
        blender_exec = settings.blender_executable
        if not blender_exec:
            raise RuntimeError("BLENDER_EXECUTABLE 설정이 필요합니다 (ai.env 확인)")
        blender_exec = blender_exec.strip().strip('"')
        
        local_temp_base = Path(settings.local_temp_dir) if settings.local_temp_dir else None
        if local_temp_base:
            local_temp_base = local_temp_base.resolve()
        else:
            import tempfile
            local_temp_base = Path(tempfile.gettempdir()) / "ai_texture_pipeline"
        local_temp_base.mkdir(parents=True, exist_ok=True)
        import tempfile
        local_temp_dir = Path(tempfile.mkdtemp(prefix="texture_", dir=str(local_temp_base)))
        local_out_dir = local_temp_dir / "outputs"
        local_out_dir.mkdir(parents=True, exist_ok=True)
        local_sanitized_dir = local_temp_dir / "sanitized_inputs"
        local_sanitized_dir.mkdir(parents=True, exist_ok=True)
        # texture_dir 생성 비활성화 (현재 필요 없음)
        # texture_dir.mkdir(parents=True, exist_ok=True)
        # texture_output_path.parent.mkdir(parents=True, exist_ok=True)
        
        hi_path = hi_input_path if isinstance(hi_input_path, Path) else Path(hi_input_path)
        lo_path = lo_input_path if isinstance(lo_input_path, Path) else Path(lo_input_path)
        
        original_hi_path = Path(hi_path)
        local_hi_candidate = local_sanitized_dir / original_hi_path.name
        try:
            shutil.copy2(original_hi_path, local_hi_candidate)
            hi_path = local_hi_candidate
        except Exception as copy_exc:
            logger.warning(f"[Texture] 하이 메쉬 복사 실패 (원본 사용): {copy_exc}")
            hi_path = original_hi_path

        try:
            hi_path = ensure_utf8_copy(
                hi_path,
                local_sanitized_dir,
                logger=logger,
                prefix="[Texture]",
            )
        except Exception as exc:
            logger.warning(f"[Texture] 하이 메쉬 UTF-8 변환 실패 (원본 사용): {exc}")
            hi_path = local_hi_candidate if local_hi_candidate.exists() else original_hi_path

        try:
            hi_path = sanitize_material_assets(
                hi_path,
                original_hi_path.parent,
                local_sanitized_dir,
                logger=logger,
                prefix="[Texture]",
            )
        except Exception as exc:
            logger.warning(f"[Texture] MTL/텍스처 정리 실패 (원본 사용): {exc}")
        
        hi_str = str(hi_path.resolve())
        lo_str = str(lo_path)
        local_out_dir_str = str(local_out_dir.resolve())
        local_out_path = local_out_dir / texture_output_path.name
        cmd = [
            sys.executable,
            str(pipeline_script),
            "--hi",
            hi_str,
            "--lo",
            lo_str,
            "--out",
            str(local_out_path.resolve()),
            "--blender",
            blender_exec,
            "--outdir",
            local_out_dir_str,
        ]
        
        self._update_progress(84, "텍스처 파이프라인 실행 중...")

        keep_temp_artifacts = bool(getattr(settings, "keep_texture_temp_artifacts", False))

        try:
            execute_subprocess(
                cmd,
                cwd=str(AI_PIPELINE_PATH / "Texture"),
                logger=logger,
                prefix="[Texture]",
                timeout_sec=3600,
            )
        except SubprocessTimeoutError:
            logger.error("[Texture] 타임아웃 (60분 초과)")
            raise RuntimeError("텍스처 파이프라인 타임아웃: 60분 초과")
        except SubprocessError as error:
            if error.returncode < 0:
                raise RuntimeError(f"텍스처 파이프라인 실행 중 시그널 종료 발생 (returncode: {error.returncode})")
            if error.output:
                logger.error(f"[Texture] 프로세스 출력:\n{error.output}")
            raise RuntimeError(f"텍스처 파이프라인 실패: exit code {error.returncode}")
        except Exception as exc:
            logger.error(f"[Texture] 예상치 못한 오류: {exc}", exc_info=True)
            raise RuntimeError(f"텍스처 파이프라인 실패: {exc}")

        if not local_out_path.exists():
            raise FileNotFoundError(f"텍스처 파이프라인 결과를 찾지 못했습니다: {local_out_path}")
        
        try:
            shutil.copy2(local_out_path, texture_output_path)
            logger.info(f"[Texture] 최종 결과 복사: {local_out_path} -> {texture_output_path}")
        except Exception as copy_exc:
            logger.error(f"[Texture] 결과 복사 실패: {copy_exc}", exc_info=True)
            raise RuntimeError(f"텍스처 결과 복사 실패: {copy_exc}")
        finally:
            if keep_temp_artifacts:
                logger.info(f"[Texture] 설정에 따라 로컬 산출물 보존: {local_temp_dir}")
            else:
                try:
                    shutil.rmtree(local_temp_dir, ignore_errors=True)
                except Exception as cleanup_exc:
                    logger.warning(f"[Texture] 로컬 임시 디렉토리 정리 실패: {cleanup_exc}")

        self._update_progress(96, f"텍스처 파이프라인 완료: {texture_output_path}")
        return texture_output_path
    
    def run_full_pipeline(
        self,
        uploads_input_path: str,
        outputs_base_dir: str,
        group_id: str = "1",
        folder_name: str = None,
        model_type: str = "space",
    ) -> str:
        """
        전체 AI 파이프라인 실행
        
        Args:
            uploads_input_path: 업로드된 원본 OBJ 경로
            outputs_base_dir: 저장 루트 (storage/outputs)
            group_id: 그룹 ID (기본값: "1")
            folder_name: 폴더명 (기본값: None, 파일명 기반으로 생성)
            model_type: 파이프라인 타입 (space 또는 object)
            
        Returns:
            최종 출력 파일 경로
        """
        self._update_progress(0, "AI 파이프라인 시작...")
        config = self._get_pipeline_config(model_type)
        logger.info(f"[AIPipeline] model_type={model_type} 파이프라인 실행 (optimizer: {config['optimizer'].name}, denoiser: {config['denoiser'].name})")

        try:
            # group_id 처리 (빈 문자열이거나 None인 경우 기본값 1 사용)
            if not group_id or group_id.strip() == "":
                group_id = "1"
                logger.warning(f"group_id가 비어있어 기본값(1)을 사용합니다.")
            else:
                # group_id가 유효한지 확인 (정수로 변환 가능한지)
                try:
                    int(group_id)  # 유효성 검사
                except (ValueError, TypeError):
                    logger.warning(f"유효하지 않은 group_id({group_id})를 기본값(1)으로 변경합니다.")
                    group_id = "1"
            
            # folder_name이 없으면 파일명 기반으로 생성
            if not folder_name:
                from datetime import datetime
                file_stem = Path(uploads_input_path).stem
                folder_name = f"{datetime.now().strftime('%Y%m%d_%H%M%S')}_{file_stem}"
            
            # uploads_input_path에서 group name 추출 (group_id 대신 사용)
            # 경로 구조: .../uploads/{group_name}/{folder_name}/file.obj
            group_name = group_id  # 기본값은 group_id 사용
            try:
                # 경로를 정규화하고 분리
                path_str = str(uploads_input_path).replace('\\', '/')
                # uploads/ 다음에 오는 디렉토리명이 group name
                if '/uploads/' in path_str:
                    parts = path_str.split('/uploads/')
                    if len(parts) > 1:
                        remaining = parts[1]
                        # 다음 슬래시까지가 group name
                        next_slash = remaining.find('/')
                        if next_slash > 0:
                            extracted_group_name = remaining[:next_slash]
                            if extracted_group_name and extracted_group_name.strip():
                                group_name = extracted_group_name.strip()
                                logger.info(f"[AIPipeline] 경로에서 group name 추출: {group_name} (원본 group_id: {group_id})")
            except Exception as e:
                logger.warning(f"[AIPipeline] group name 추출 실패, group_id 사용: {e}")
            
            context = resolve_pipeline_paths(
                uploads_input_path=uploads_input_path,
                outputs_base_dir=outputs_base_dir,
                group_name=group_name,
                folder_name=folder_name,
            )
            directories = context.directories
            folder_name = context.folder_name
            uploads_path = context.uploads_path
            temp_dir = directories.temp_dir
            optimized_dir = directories.optimized_dir
            final_dir = directories.final_dir
            # texture_dir 생성 비활성화 (현재 필요 없음)
            # texture_dir = directories.texture_dir

            temp_dir_display = directories.temp_dir_str or str(temp_dir)
            optimized_dir_display = directories.optimized_dir_str or str(optimized_dir)
            final_dir_display = directories.final_dir_str or str(final_dir)
            # texture_dir_display = directories.texture_dir_str or str(texture_dir)

            logger.info(f"[AIPipeline] outputs_base_dir: {outputs_base_dir}")
            logger.info(f"[AIPipeline] temp_dir: {temp_dir} (문자열: {temp_dir_display})")
            logger.info(f"[AIPipeline] optimized_dir: {optimized_dir} (문자열: {optimized_dir_display})")
            logger.info(f"[AIPipeline] final_dir: {final_dir} (문자열: {final_dir_display})")
            # logger.info(f"[AIPipeline] texture_dir: {texture_dir} (문자열: {texture_dir_display})")

            model_type_key = (model_type or "space").strip().lower()

            if model_type_key == "object":
                logger.info("[AIPipeline] object 파이프라인: denoiser → optimizer 순서로 실행")
                denoised_path = self.run_mesh_denoiser(
                    uploads_input_path,
                    optimized_dir,
                    model_type=model_type,
                )
                self._update_progress(65, f"object denoiser 완료: {denoised_path}")

                final_path = self.run_mesh_optimizer(
                    str(denoised_path),
                    temp_dir,
                    final_dir,
                    model_type=model_type,
                )
                final_obj_path = Path(final_path)
            else:
                # 1단계: 메쉬 최적화 → outputs/optimized
                optimized_path = self.run_mesh_optimizer(uploads_input_path, temp_dir, optimized_dir, model_type=model_type)

                # 2단계: 메쉬 노이즈 제거 → outputs/final
                final_path = self.run_mesh_denoiser(
                    optimized_path,
                    final_dir,
                    model_type=model_type,
                )
                final_obj_path = Path(final_path)
            
            if not final_obj_path.exists():
                raise FileNotFoundError(f"메쉬 파이프라인 최종 OBJ를 찾지 못했습니다: {final_obj_path}")
            
            # texture 파이프라인 실행 비활성화 (현재 필요 없음)
            # safe_folder_name = sanitize_filename(folder_name or final_obj_path.stem)
            # texture_output_filename = f"{safe_folder_name}_final.glb"
            # texture_output_path = texture_dir / texture_output_filename
            # 
            # hi_input_path = uploads_path
            # texture_final_path = self.run_texture_pipeline(
            #     hi_input_path=hi_input_path,
            #     lo_input_path=final_obj_path,
            #     texture_dir=texture_dir,
            #     texture_output_path=texture_output_path,
            # )
            # 
            # # 최종 경로 확인 및 검증
            # final_path_str = str(texture_final_path)
            # logger.info(f"[AIPipeline] 텍스처 최종 경로: {final_path_str}")
            # logger.info(f"[AIPipeline] 메쉬 보정 최종 OBJ: {final_obj_path}")
            # 
            # # 파일 존재 확인
            # if not texture_final_path.exists():
            #     logger.error(f"[AIPipeline] 경고: 텍스처 최종 파일이 존재하지 않습니다: {final_path_str}")
            #     # 파일이 없어도 경로는 반환 (에러 처리 상위에서)
            # else:
            #     file_size = texture_final_path.stat().st_size if texture_final_path.exists() else 0
            #     logger.info(f"[AIPipeline] 텍스처 최종 파일 확인 - 경로: {final_path_str}, 크기: {file_size} bytes, 존재: {texture_final_path.exists()}")
            
            if final_obj_path.exists():
                obj_size = final_obj_path.stat().st_size
                logger.info(f"[AIPipeline] 메쉬 보정 OBJ 확인 - 경로: {final_obj_path}, 크기: {obj_size} bytes")
            
            # 최종 경로는 메쉬 보정 OBJ 경로로 반환
            final_path_str = str(final_obj_path)
            self._update_progress(100, f"AI 파이프라인 완료 (메쉬 보정 OBJ 생성): {final_obj_path}")
            return final_path_str
            
        except Exception as e:
            error_msg = f"AI 파이프라인 실행 중 오류: {str(e)}"
            logger.error(error_msg)
            raise RuntimeError(error_msg)
