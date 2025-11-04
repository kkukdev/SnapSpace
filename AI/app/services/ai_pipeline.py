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
        mesh_optimizer.py 실행 (네트워크 성능 최적화: 로컬 임시 디렉토리 사용)
        
        Args:
            src_input_path: 업로드된 원본 OBJ 경로 (uploads)
            temp_dir: 임시 디렉토리 (네트워크)
            optimized_dir: 최적화 결과 디렉토리 (네트워크)
            
        Returns:
            optimized_dir에 저장된 *_cleaned.obj 경로
        """
        self._update_progress(10, "메쉬 최적화 시작...")
        
        from app.config import settings
        import tempfile
        
        # 네트워크 경로인지 확인
        is_network_path = (src_input_path.startswith('\\\\') or src_input_path.startswith('//'))
        
        # 로컬 임시 디렉토리 생성 (네트워크 I/O 성능 향상)
        if is_network_path:
            if settings.local_temp_dir:
                local_temp_base = Path(settings.local_temp_dir)
            else:
                local_temp_base = Path(tempfile.gettempdir()) / "ai_pipeline"
            
            local_temp_base.mkdir(parents=True, exist_ok=True)
            local_temp_dir = Path(tempfile.mkdtemp(prefix="mesh_opt_", dir=str(local_temp_base)))
            local_optimized_dir = local_temp_dir / "optimized"
            local_optimized_dir.mkdir(parents=True, exist_ok=True)
            
            logger.info(f"[성능 최적화] 네트워크 경로 감지, 로컬 임시 디렉토리 사용: {local_temp_dir}")
            
            # 입력 파일을 로컬로 복사 (한 번만 네트워크 I/O)
            input_filename = Path(src_input_path).name
            local_input_path = local_temp_dir / input_filename
            self._update_progress(12, "입력 파일을 로컬로 복사 중...")
            shutil.copy2(src_input_path, local_input_path)
            logger.info(f"입력 파일 복사 완료: {src_input_path} -> {local_input_path}")
            
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
            str(self.polygon_path / "mesh_optimizer.py"),
            "--input", str(work_input_abs),
            "--temp-dir", str(work_temp_dir_abs),
            "--optimized-dir", str(work_optimized_dir_abs),
        ]

        try:
            logger.info(f"[optimizer] cwd={self.polygon_path} cmd={' '.join(cmd)}")
            
            # Python unbuffered 모드 및 UTF-8 인코딩 설정
            env = os.environ.copy()
            env['PYTHONUNBUFFERED'] = '1'
            env['PYTHONIOENCODING'] = 'utf-8'  # Windows에서 이모지 출력을 위한 UTF-8 인코딩
            
            # 실시간 출력을 위해 Popen 사용
            process = subprocess.Popen(
                cmd,
                cwd=str(self.polygon_path),
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,  # stderr를 stdout에 병합
                text=True,
                encoding='utf-8',  # UTF-8 인코딩 명시
                errors='replace',  # 인코딩 오류 시 대체 문자 사용
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

            # 최종 cleaned 경로는 work_optimized_dir 기준 (로컬 또는 네트워크)
            # work_input은 이미 절대 경로로 변환되었으므로 with_suffix 사용
            base_stem = work_input_abs.with_suffix("").name
            cleaned_local = work_optimized_dir_abs / f"{base_stem}_cleaned.obj"
            
            logger.info(f"[Optimizer] 최종 파일 경로 확인 (로컬): {cleaned_local}")
            
            # 네트워크 경로인 경우 로컬 결과를 네트워크로 복사
            if is_network_path and local_temp_dir:
                if not cleaned_local.exists():
                    raise FileNotFoundError(f"로컬 최적화 결과가 없습니다: {cleaned_local}")
                
                self._update_progress(48, "최종 결과를 네트워크로 복사 중...")
                cleaned_network = optimized_dir / f"{base_stem}_cleaned.obj"
                shutil.copy2(cleaned_local, cleaned_network)
                logger.info(f"최종 결과 복사 완료: {cleaned_local} -> {cleaned_network}")
                
                # 로컬 임시 디렉토리 정리
                try:
                    shutil.rmtree(local_temp_dir)
                    logger.info(f"로컬 임시 디렉토리 정리 완료: {local_temp_dir}")
                except Exception as e:
                    logger.warning(f"로컬 임시 디렉토리 정리 실패 (무시): {e}")
                
                cleaned = cleaned_network
            else:
                cleaned = cleaned_local
            
            if not cleaned.exists():
                raise FileNotFoundError(f"메쉬 최적화 결과가 없습니다: {cleaned}")
            
            self._update_progress(50, f"메쉬 최적화 완료: {cleaned}")
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
        mesh_denoiser.py 실행 (auto_flat 모드, 네트워크 성능 최적화: 로컬 임시 디렉토리 사용)
        
        Args:
            cleaned_polygon_path: polygon 단계 산출물 *_cleaned.obj 경로
            final_dir: 최종 산출물 디렉토리 (네트워크)
            
        Returns:
            final_dir에 저장된 *_auto_flat.obj 경로
        """
        self._update_progress(50, "메쉬 노이즈 제거 시작...")
        
        from app.config import settings
        import tempfile
        
        # 네트워크 경로인지 확인
        cleaned_input_str = str(cleaned_polygon_path)
        is_network_path = (cleaned_input_str.startswith('\\\\') or cleaned_input_str.startswith('//'))
        
        # 로컬 임시 디렉토리 생성 (네트워크 I/O 성능 향상)
        if is_network_path:
            if settings.local_temp_dir:
                local_temp_base = Path(settings.local_temp_dir)
            else:
                local_temp_base = Path(tempfile.gettempdir()) / "ai_pipeline"
            
            local_temp_base.mkdir(parents=True, exist_ok=True)
            local_temp_dir = Path(tempfile.mkdtemp(prefix="mesh_denoise_", dir=str(local_temp_base)))
            local_output_dir = local_temp_dir / "output"
            local_output_dir.mkdir(parents=True, exist_ok=True)
            
            logger.info(f"[성능 최적화] 네트워크 경로 감지, 로컬 임시 디렉토리 사용: {local_temp_dir}")
            
            # 입력 파일을 로컬로 복사 (한 번만 네트워크 I/O)
            input_filename = Path(cleaned_polygon_path).name
            local_input_path = local_temp_dir / input_filename
            self._update_progress(52, "입력 파일을 로컬로 복사 중...")
            shutil.copy2(cleaned_polygon_path, local_input_path)
            logger.info(f"입력 파일 복사 완료: {cleaned_polygon_path} -> {local_input_path}")
            
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
        final_dir.mkdir(parents=True, exist_ok=True)
        
        # 절대 경로로 변환
        work_input_abs = work_input.resolve() if hasattr(work_input, 'resolve') else Path(work_input).resolve()
        work_output_dir_abs = work_output_dir.resolve() if hasattr(work_output_dir, 'resolve') else Path(work_output_dir).resolve()

        cmd = [
            sys.executable,  # 현재 Python 인터프리터 사용 (가상 환경 보장)
            str(self.polygon_path / "mesh_denoiser.py"),
            "-i", str(work_input_abs),
            "--mode", "auto_flat",
            "--output-dir", str(work_output_dir_abs),
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
            
            # Python unbuffered 모드 및 UTF-8 인코딩 설정
            env = os.environ.copy()
            env['PYTHONUNBUFFERED'] = '1'
            env['PYTHONIOENCODING'] = 'utf-8'  # Windows에서 이모지 출력을 위한 UTF-8 인코딩
            
            # 실시간 출력을 위해 Popen 사용
            process = subprocess.Popen(
                cmd,
                cwd=str(self.polygon_path),
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,  # stderr를 stdout에 병합
                text=True,
                encoding='utf-8',  # UTF-8 인코딩 명시
                errors='replace',  # 인코딩 오류 시 대체 문자 사용
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

            # 최종 파일 경로 (base_stem은 원본 파일명에서 확장자 제거)
            base_stem = work_input_abs.with_suffix("").name
            final_output_local = work_output_dir_abs / f"{base_stem}_auto_flat.obj"
            
            # 네트워크 경로인 경우 로컬 결과를 네트워크로 복사
            if is_network_path and local_temp_dir:
                if not final_output_local.exists():
                    raise FileNotFoundError(f"로컬 노이즈 제거 결과가 없습니다: {final_output_local}")
                
                self._update_progress(78, "최종 결과를 네트워크로 복사 중...")
                final_output_network = final_dir / f"{base_stem}_auto_flat.obj"
                shutil.copy2(final_output_local, final_output_network)
                logger.info(f"최종 결과 복사 완료: {final_output_local} -> {final_output_network}")
                
                # 로컬 임시 디렉토리 정리
                try:
                    shutil.rmtree(local_temp_dir)
                    logger.info(f"로컬 임시 디렉토리 정리 완료: {local_temp_dir}")
                except Exception as e:
                    logger.warning(f"로컬 임시 디렉토리 정리 실패 (무시): {e}")
                
                final_output = final_output_network
            else:
                final_output = final_output_local
            
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
            # 네트워크 경로 또는 절대 경로 처리
            outputs_root_str = str(outputs_base_dir)
            
            # 네트워크 경로인 경우 문자열 직접 조작 (Path 객체는 UNC 경로에서 parent 연산이 이상하게 동작할 수 있음)
            if outputs_root_str.startswith('\\\\') or outputs_root_str.startswith('//'):
                # UNC 경로 처리: \\server\share\path\outputs -> \\server\share\path\temp
                # outputs 디렉토리 제거 후 temp, outputs 디렉토리 경로 생성
                if outputs_root_str.endswith('\\') or outputs_root_str.endswith('/'):
                    outputs_root_str = outputs_root_str.rstrip('\\/')
                
                # storage 경로 추출 (outputs가 포함된 경우 제거)
                # 예: \\server\share\storage\outputs -> \\server\share\storage
                if outputs_root_str.endswith('\\outputs') or outputs_root_str.endswith('/outputs'):
                    storage_base = outputs_root_str[:-8]  # '\\outputs' 또는 '/outputs' 제거
                elif outputs_root_str.endswith('outputs'):
                    storage_base = outputs_root_str[:-7]  # 'outputs' 제거
                else:
                    storage_base = outputs_root_str
                
                # 경로 정규화 (끝에 백슬래시 제거, 중복 storage 제거)
                storage_base = storage_base.rstrip('\\/')
                
                # storage\storage 중복 제거 (버그 방지)
                if '\\storage\\storage' in storage_base:
                    storage_base = storage_base.replace('\\storage\\storage', '\\storage')
                elif '/storage/storage' in storage_base:
                    storage_base = storage_base.replace('/storage/storage', '/storage')
                
                # 네트워크 경로 생성
                temp_dir = Path(f"{storage_base}\\temp")
                optimized_dir = Path(f"{storage_base}\\outputs\\optimized")
                final_dir = Path(f"{storage_base}\\outputs\\final")
            else:
                # 일반 경로는 Path 객체 사용
                outputs_root = Path(outputs_base_dir)
                temp_dir = outputs_root.parent / "temp"
                optimized_dir = outputs_root / "optimized"
                final_dir = outputs_root / "final"
            
            logger.info(f"[AIPipeline] outputs_base_dir: {outputs_base_dir}")
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
