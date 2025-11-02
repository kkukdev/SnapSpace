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
import subprocess
import logging
import threading
from typing import Callable, Optional
from pathlib import Path

# Open3D headless 모드 설정
os.environ["OPEN3D_HEADLESS"] = "1"
os.environ["PYOPENGL_PLATFORM"] = "egl"

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
    
    def run_mesh_optimizer(self, src_input_path: str, temp_dir: Path, optimized_dir: Path) -> Path:
        """
        mesh_optimizer.py 실행
        
        Args:
            src_input_path: 업로드된 원본 OBJ 경로 (uploads)
            polygon_dir: 중간 산출물 디렉토리 (outputs/polygon)
            
        Returns:
            polygon_dir에 저장된 *_cleaned.obj 경로
        """
        self._update_progress(10, "메쉬 최적화 시작...")

        temp_dir.mkdir(parents=True, exist_ok=True)
        optimized_dir.mkdir(parents=True, exist_ok=True)

        src_input = Path(src_input_path).resolve()
        work_input = src_input  # processing 미사용: 업로드 파일 경로 그대로

        # 절대 경로 보장 (resolve()는 현재 디렉토리를 기준으로 하므로 사용하지 않음)
        if os.path.isabs(str(temp_dir)):
            temp_dir_abs = Path(temp_dir)
        else:
            temp_dir_abs = Path("/project_root") / str(temp_dir).lstrip("/")
        
        if os.path.isabs(str(optimized_dir)):
            optimized_dir_abs = Path(optimized_dir)
        else:
            optimized_dir_abs = Path("/project_root") / str(optimized_dir).lstrip("/")

        cmd = [
            "python",
            str(self.polygon_path / "mesh_optimizer.py"),
            "--input", str(work_input),
            "--temp-dir", str(temp_dir_abs),
            "--optimized-dir", str(optimized_dir_abs),
        ]

        try:
            logger.info(f"[optimizer] cwd={self.polygon_path} cmd={' '.join(cmd)}")
            
            # Python unbuffered 모드로 실행하기 위해 환경 변수 설정
            env = os.environ.copy()
            env['PYTHONUNBUFFERED'] = '1'
            
            # 실시간 출력을 위해 Popen 사용
            process = subprocess.Popen(
                cmd,
                cwd=str(self.polygon_path),
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,  # stderr를 stdout에 병합
                text=True,
                bufsize=1,  # 라인 버퍼링
                universal_newlines=True,
                env=env  # 환경 변수 전달
            )
            
            # 실시간으로 stdout 라인별 로깅 (타임아웃 지원)
            output_lines = []
            output_lock = threading.Lock()
            read_finished = threading.Event()
            
            def read_output():
                """stdout 읽기 스레드"""
                try:
                    for line in iter(process.stdout.readline, ''):
                        line = line.rstrip()
                        if line:
                            logger.info(f"[Optimizer] {line}")
                            with output_lock:
                                output_lines.append(line)
                    read_finished.set()
                except Exception as e:
                    logger.error(f"[Optimizer] stdout 읽기 오류: {e}")
                    read_finished.set()
            
            # stdout 읽기 스레드 시작
            reader_thread = threading.Thread(target=read_output, daemon=True)
            reader_thread.start()
            
            # 프로세스 종료 대기 (타임아웃 30분)
            return_code = None
            timeout_reached = threading.Event()
            
            def wait_with_timeout():
                """프로세스 종료 대기 (타임아웃 지원)"""
                nonlocal return_code
                try:
                    return_code = process.wait()
                except Exception as e:
                    logger.error(f"[Optimizer] 프로세스 대기 오류: {e}")
                    return_code = -1
                finally:
                    timeout_reached.set()
            
            # 타임아웃 스레드 시작
            wait_thread = threading.Thread(target=wait_with_timeout, daemon=True)
            wait_thread.start()
            
            # 타임아웃 체크 (30분 = 1800초)
            if not timeout_reached.wait(timeout=1800):
                logger.error(f"[Optimizer] 프로세스 타임아웃 (30분 초과) - 강제 종료")
                try:
                    process.kill()  # SIGKILL
                    process.wait()
                    return_code = -9  # SIGKILL 시그널 코드
                except Exception as e:
                    logger.error(f"[Optimizer] 프로세스 종료 실패: {e}")
                    return_code = -1
            else:
                # 정상 종료 대기 (최대 5초)
                wait_thread.join(timeout=5)
            
            # stdout 읽기 완료 대기 (최대 5초)
            read_finished.wait(timeout=5)
            
            # returncode 로깅
            logger.info(f"[Optimizer] 프로세스 종료 (returncode: {return_code})")
            
            # SIGSEGV 감지 (음수 returncode는 시그널로 종료됨을 의미)
            if return_code < 0:
                error_msg = f"프로세스가 시그널로 종료되었습니다 (returncode: {return_code}, SIGSEGV일 가능성)"
                logger.error(f"[Optimizer] {error_msg}")
                raise RuntimeError(f"메쉬 최적화 중 크래시 발생 (SIGSEGV). 파일이 손상되었거나 메모리 부족일 수 있습니다")
            
            # 일반적인 에러 코드
            if return_code != 0:
                output_text = '\n'.join(output_lines)
                error_msg = f"프로세스 실패 (exit code: {return_code})"
                logger.error(f"[Optimizer] {error_msg}")
                raise subprocess.CalledProcessError(return_code, cmd, output_text)

            # 최종 cleaned 경로는 optimized_dir_abs 기준
            base_stem = work_input.with_suffix("").name
            cleaned = optimized_dir_abs / f"{base_stem}_cleaned.obj"
            
            logger.info(f"[Optimizer] 최종 파일 경로 확인: {cleaned}")
            logger.info(f"[Optimizer] optimized_dir 존재 여부: {optimized_dir_abs.exists()}")
            
            if not cleaned.exists():
                # 디버깅: optimized_dir의 모든 파일 목록 출력
                if optimized_dir_abs.exists():
                    all_files = list(optimized_dir_abs.glob("*"))
                    logger.error(f"[Optimizer] 파일 생성 실패!")
                    logger.error(f"[Optimizer] 기대 경로: {cleaned}")
                    logger.error(f"[Optimizer] optimized_dir ({optimized_dir_abs}) 전체 파일 목록:")
                    for f in all_files:
                        logger.error(f"[Optimizer]   - {f.name} ({'파일' if f.is_file() else '디렉토리'})")
                else:
                    logger.error(f"[Optimizer] optimized_dir 자체가 존재하지 않습니다: {optimized_dir_abs}")
                
                raise FileNotFoundError(f"메쉬 최적화 결과가 없습니다: {cleaned}")
            self._update_progress(30, f"메쉬 최적화 완료: {cleaned}")
            return cleaned

        except subprocess.CalledProcessError as e:
            logger.error(f"[Optimizer] 프로세스 오류 (exit code: {e.returncode})")
            if hasattr(e, 'stdout') and e.stdout:
                logger.error(f"[Optimizer] stdout: {e.stdout}")
            if hasattr(e, 'stderr') and e.stderr:
                logger.error(f"[Optimizer] stderr: {e.stderr}")
            raise RuntimeError(f"메쉬 최적화 실패: {e}")
        except subprocess.TimeoutExpired as e:
            logger.error(f"[Optimizer] 타임아웃 (30분 초과)")
            raise RuntimeError("메쉬 최적화 타임아웃: 30분 초과")
        except Exception as e:
            # SIGSEGV 등 모든 예외 처리
            error_str = str(e)
            if "SIGSEGV" in error_str or "died with" in error_str or "Signal" in error_str:
                logger.error(f"[Optimizer] 세그멘테이션 폴트 발생 (SIGSEGV)")
                logger.error(f"[Optimizer] 이는 보통 손상된 OBJ 파일, 메모리 부족, 또는 PyMeshLab/Open3D 라이브러리 문제로 발생합니다")
                raise RuntimeError(f"메쉬 최적화 중 크래시 발생 (SIGSEGV). 파일이 손상되었거나 메모리 부족일 수 있습니다")
            logger.error(f"[Optimizer] 예상치 못한 오류: {e}", exc_info=True)
            raise RuntimeError(f"메쉬 최적화 실패: {e}")
    
    def run_mesh_denoiser(self, cleaned_polygon_path: Path, final_dir: Path) -> Path:
        """
        mesh_denoiser.py 실행 (auto_flat 모드)
        
        Args:
            cleaned_polygon_path: polygon 단계 산출물 *_cleaned.obj 경로
            final_dir: 최종 산출물 디렉토리 (outputs/final)
            
        Returns:
            final_dir에 저장된 *_auto_flat.obj 경로
        """
        self._update_progress(50, "메쉬 노이즈 제거 시작...")

        final_dir.mkdir(parents=True, exist_ok=True)

        work_input = cleaned_polygon_path.resolve()  # processing 미사용

        cmd = [
            "python",
            str(self.polygon_path / "mesh_denoiser.py"),
            "-i", str(work_input),
            "--mode", "auto_flat",
            "--output-dir", str(final_dir.resolve()),
            "--proj-dist", "0.008",
            "--floor-ratio", "0.5",
            "--wall-ratio", "2.0",
            "--smooth-floor", "6",
            "--smooth-wall", "24",
            "--wall-ortho-dot", "0.15",
            "--max-walls", "6",
            "--ransac-iters", "4000",
            "--preclean"
        ]

        try:
            logger.info(f"[denoiser] cwd={self.polygon_path} cmd={' '.join(cmd)}")
            
            # Python unbuffered 모드로 실행하기 위해 환경 변수 설정
            env = os.environ.copy()
            env['PYTHONUNBUFFERED'] = '1'
            
            # 실시간 출력을 위해 Popen 사용
            process = subprocess.Popen(
                cmd,
                cwd=str(self.polygon_path),
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,  # stderr를 stdout에 병합
                text=True,
                bufsize=1,  # 라인 버퍼링
                universal_newlines=True,
                env=env  # 환경 변수 전달
            )
            
            # 실시간으로 stdout 라인별 로깅 (타임아웃 지원)
            output_lines = []
            output_lock = threading.Lock()
            read_finished = threading.Event()
            
            def read_output():
                """stdout 읽기 스레드"""
                try:
                    for line in iter(process.stdout.readline, ''):
                        line = line.rstrip()
                        if line:
                            logger.info(f"[Denoiser] {line}")
                            with output_lock:
                                output_lines.append(line)
                    read_finished.set()
                except Exception as e:
                    logger.error(f"[Denoiser] stdout 읽기 오류: {e}")
                    read_finished.set()
            
            # stdout 읽기 스레드 시작
            reader_thread = threading.Thread(target=read_output, daemon=True)
            reader_thread.start()
            
            # 프로세스 종료 대기 (타임아웃 30분)
            return_code = None
            timeout_reached = threading.Event()
            
            def wait_with_timeout():
                """프로세스 종료 대기 (타임아웃 지원)"""
                nonlocal return_code
                try:
                    return_code = process.wait()
                except Exception as e:
                    logger.error(f"[Denoiser] 프로세스 대기 오류: {e}")
                    return_code = -1
                finally:
                    timeout_reached.set()
            
            # 타임아웃 스레드 시작
            wait_thread = threading.Thread(target=wait_with_timeout, daemon=True)
            wait_thread.start()
            
            # 타임아웃 체크 (30분 = 1800초)
            if not timeout_reached.wait(timeout=1800):
                logger.error(f"[Denoiser] 프로세스 타임아웃 (30분 초과) - 강제 종료")
                try:
                    process.kill()  # SIGKILL
                    process.wait()
                    return_code = -9  # SIGKILL 시그널 코드
                except Exception as e:
                    logger.error(f"[Denoiser] 프로세스 종료 실패: {e}")
                    return_code = -1
            else:
                # 정상 종료 대기 (최대 5초)
                wait_thread.join(timeout=5)
            
            # stdout 읽기 완료 대기 (최대 5초)
            read_finished.wait(timeout=5)
            
            # returncode 로깅
            logger.info(f"[Denoiser] 프로세스 종료 (returncode: {return_code})")
            
            # SIGSEGV 감지 (음수 returncode는 시그널로 종료됨을 의미)
            if return_code < 0:
                error_msg = f"프로세스가 시그널로 종료되었습니다 (returncode: {return_code}, SIGSEGV일 가능성)"
                logger.error(f"[Denoiser] {error_msg}")
                raise RuntimeError(f"메쉬 노이즈 제거 중 크래시 발생 (SIGSEGV). 파일이 손상되었거나 메모리 부족일 수 있습니다")
            
            # 일반적인 에러 코드
            if return_code != 0:
                output_text = '\n'.join(output_lines)
                error_msg = f"프로세스 실패 (exit code: {return_code})"
                logger.error(f"[Denoiser] {error_msg}")
                raise subprocess.CalledProcessError(return_code, cmd, output_text)

            base_stem = work_input.with_suffix("").name
            final_output = final_dir / f"{base_stem}_auto_flat.obj"
            if not final_output.exists():
                raise FileNotFoundError(f"메쉬 노이즈 제거 결과가 없습니다: {final_output}")
            self._update_progress(80, f"메쉬 노이즈 제거 완료: {final_output}")
            return final_output

        except subprocess.CalledProcessError as e:
            logger.error(f"[Denoiser] 프로세스 오류 (exit code: {e.returncode})")
            if hasattr(e, 'stdout') and e.stdout:
                logger.error(f"[Denoiser] stdout: {e.stdout}")
            if hasattr(e, 'stderr') and e.stderr:
                logger.error(f"[Denoiser] stderr: {e.stderr}")
            raise RuntimeError(f"메쉬 노이즈 제거 실패: {e}")
        except subprocess.TimeoutExpired as e:
            logger.error(f"[Denoiser] 타임아웃 (30분 초과)")
            raise RuntimeError("메쉬 노이즈 제거 타임아웃: 30분 초과")
        except Exception as e:
            # SIGSEGV 등 모든 예외 처리
            error_str = str(e)
            if "SIGSEGV" in error_str or "died with" in error_str or "Signal" in error_str:
                logger.error(f"[Denoiser] 세그멘테이션 폴트 발생 (SIGSEGV)")
                logger.error(f"[Denoiser] 이는 보통 손상된 OBJ 파일, 메모리 부족, 또는 PyMeshLab/Open3D 라이브러리 문제로 발생합니다")
                raise RuntimeError(f"메쉬 노이즈 제거 중 크래시 발생 (SIGSEGV). 파일이 손상되었거나 메모리 부족일 수 있습니다")
            logger.error(f"[Denoiser] 예상치 못한 오류: {e}", exc_info=True)
            raise RuntimeError(f"메쉬 노이즈 제거 실패: {e}")
    
    def run_full_pipeline(self, uploads_input_path: str, outputs_base_dir: str) -> str:
        """
        전체 AI 파이프라인 실행
        
        Args:
            uploads_input_path: 업로드된 원본 OBJ 경로
            outputs_base_dir: 저장 루트 (storage/outputs)
            
        Returns:
            최종 출력 파일 경로
        """
        self._update_progress(0, "AI 파이프라인 시작...")
        
        try:
            # 절대 경로 보장 (resolve()는 현재 디렉토리를 기준으로 하므로 사용하지 않음)
            if os.path.isabs(outputs_base_dir):
                outputs_root = Path(outputs_base_dir)
            else:
                # 상대 경로인 경우 /project_root 기준으로 변환
                outputs_root = Path("/project_root") / outputs_base_dir.lstrip("/")
            
            temp_dir = outputs_root.parent / "temp"
            optimized_dir = outputs_root / "optimized"
            final_dir = outputs_root / "final"
            
            logger.info(f"[AIPipeline] outputs_root: {outputs_root}")
            logger.info(f"[AIPipeline] temp_dir: {temp_dir}")
            logger.info(f"[AIPipeline] optimized_dir: {optimized_dir}")
            logger.info(f"[AIPipeline] final_dir: {final_dir}")

            # 1단계: 메쉬 최적화 → outputs/polygon
            optimized_path = self.run_mesh_optimizer(uploads_input_path, temp_dir, optimized_dir)

            # 2단계: 메쉬 노이즈 제거 → outputs/final
            final_path = self.run_mesh_denoiser(
                optimized_path,
                final_dir,
            )
            
            self._update_progress(100, f"AI 파이프라인 완료: {final_path}")
            return str(final_path)
            
        except Exception as e:
            error_msg = f"AI 파이프라인 실행 중 오류: {str(e)}"
            logger.error(error_msg)
            raise RuntimeError(error_msg)
