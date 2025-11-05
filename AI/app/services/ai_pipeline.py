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
                logger.info(f"[네트워크 경로 감지] 로컬 마운트된 네트워크 경로: {src_input_path}")
        
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
            if (is_network_path or is_optimized_dir_network) and local_temp_dir:
                if not cleaned_local.exists():
                    raise FileNotFoundError(f"로컬 최적화 결과가 없습니다: {cleaned_local}")
                
                self._update_progress(48, "최종 결과를 네트워크로 복사 중...")
                cleaned_network = optimized_dir / f"{base_stem}_cleaned.obj"
                
                # 네트워크 디렉토리 생성 확인
                try:
                    optimized_dir.mkdir(parents=True, exist_ok=True)
                    logger.info(f"네트워크 디렉토리 생성 확인: {optimized_dir} (존재: {optimized_dir.exists()})")
                except Exception as e:
                    logger.error(f"네트워크 디렉토리 생성 실패: {optimized_dir}, 오류: {e}")
                    raise
                
                # 파일 복사 및 검증
                try:
                    shutil.copy2(cleaned_local, cleaned_network)
                    logger.info(f"최적화 결과 복사 완료: {cleaned_local} -> {cleaned_network}")
                    
                    # 복사 후 파일 존재 확인
                    if not cleaned_network.exists():
                        raise FileNotFoundError(f"파일 복사 후에도 존재하지 않습니다: {cleaned_network}")
                    
                    # 파일 크기 확인
                    local_size = cleaned_local.stat().st_size if cleaned_local.exists() else 0
                    network_size = cleaned_network.stat().st_size if cleaned_network.exists() else 0
                    logger.info(f"파일 크기 확인 - 로컬: {local_size} bytes, 네트워크: {network_size} bytes")
                    
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
                logger.info(f"[네트워크 경로 감지] 로컬 마운트된 네트워크 경로: {cleaned_input_str}")
        
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

        cmd = [
            sys.executable,  # 현재 Python 인터프리터 사용 (가상 환경 보장)
            str(self.polygon_path / "mesh_denoiser.py"),
            "-i", str(work_input_abs),
            "--output-dir", str(work_output_dir_abs)  # work_output_dir_abs는 이미 output 폴더를 포함
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
            # 기본 모드는 입력 파일과 같은 디렉토리에 저장하므로, 임시 디렉토리에서 찾기
            base_stem = work_input_abs.with_suffix("").name
            
            # 기본 모드는 --output-dir을 사용하지 않고 입력 파일과 같은 디렉토리에 저장
            # 네트워크 경로인 경우 로컬 결과를 네트워크로 복사
            if (is_network_path or is_final_dir_network) and local_temp_dir:
                # 기본 모드는 local_temp_dir에 직접 저장 (output 폴더가 아님)
                final_output_local = local_temp_dir / f"{base_stem}_denoised_taubin.obj"
                
                if not final_output_local.exists():
                    raise FileNotFoundError(f"로컬 노이즈 제거 결과가 없습니다: {final_output_local}")
                
                self._update_progress(78, "최종 결과를 네트워크로 복사 중...")
                # final_dir이 올바른 경로인지 확인하고 생성
                try:
                    final_dir.mkdir(parents=True, exist_ok=True)
                except Exception as e:
                    logger.warning(f"최종 디렉토리 생성 실패 (무시): {e}")
                
                final_output_network = final_dir / f"{base_stem}_denoised_taubin.obj"
                
                # 네트워크 디렉토리 생성 확인 (이미 run_full_pipeline에서 생성했지만 재확인)
                try:
                    final_dir.mkdir(parents=True, exist_ok=True)
                    logger.info(f"[Denoiser] 네트워크 디렉토리 생성 확인: {final_dir} (존재: {final_dir.exists()})")
                    # 디렉토리 접근 가능 여부 확인
                    if not final_dir.exists():
                        raise FileNotFoundError(f"디렉토리가 생성되지 않았습니다: {final_dir}")
                    # 디렉토리에 쓰기 권한 확인
                    test_file = final_dir / ".test_write"
                    try:
                        test_file.touch()
                        test_file.unlink()
                        logger.info(f"[Denoiser] 디렉토리 쓰기 권한 확인 완료: {final_dir}")
                    except Exception as write_e:
                        logger.warning(f"[Denoiser] 디렉토리 쓰기 권한 확인 실패: {final_dir}, 오류: {write_e}")
                except Exception as e:
                    logger.error(f"[Denoiser] 네트워크 디렉토리 생성 실패: {final_dir}, 오류: {e}")
                    raise
                
                # 파일 복사 및 검증
                try:
                    shutil.copy2(final_output_local, final_output_network)
                    logger.info(f"최종 결과 복사 완료: {final_output_local} -> {final_output_network}")
                    
                    # 복사 후 파일 존재 확인
                    if not final_output_network.exists():
                        raise FileNotFoundError(f"파일 복사 후에도 존재하지 않습니다: {final_output_network}")
                    
                    # 파일 크기 확인
                    local_size = final_output_local.stat().st_size if final_output_local.exists() else 0
                    network_size = final_output_network.stat().st_size if final_output_network.exists() else 0
                    logger.info(f"파일 크기 확인 - 로컬: {local_size} bytes, 네트워크: {network_size} bytes")
                    
                    if network_size == 0:
                        raise FileNotFoundError(f"네트워크 경로에 복사된 파일 크기가 0입니다: {final_output_network}")
                    
                except Exception as copy_e:
                    logger.error(f"파일 복사 실패: {copy_e}", exc_info=True)
                    # 복사 실패 시 예외를 재발생시켜 명확한 에러 표시
                    raise RuntimeError(f"네트워크 경로로 파일 복사 실패: {final_output_local} -> {final_output_network}, 오류: {copy_e}")
                
                # 로컬 임시 디렉토리 정리 (복사 성공 후에만)
                if final_output_network.exists() and final_output_network != final_output_local:
                    try:
                        shutil.rmtree(local_temp_dir)
                        logger.info(f"로컬 임시 디렉토리 정리 완료: {local_temp_dir}")
                    except Exception as e:
                        logger.warning(f"로컬 임시 디렉토리 정리 실패 (무시): {e}")
                
                final_output = final_output_network
            else:
                # 로컬 경로인 경우에도 final_dir 사용
                final_dir.mkdir(parents=True, exist_ok=True)
                # 기본 모드는 입력 파일과 같은 디렉토리에 저장
                final_output_local = work_input_abs.parent / f"{base_stem}_denoised_taubin.obj"
                final_output = final_dir / f"{base_stem}_denoised_taubin.obj"
                if final_output_local.exists() and final_output_local != final_output:
                    shutil.copy2(final_output_local, final_output)
                    logger.info(f"최종 결과 이동: {final_output_local} -> {final_output}")
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
    
    def run_full_pipeline(self, uploads_input_path: str, outputs_base_dir: str, group_id: str = "1", folder_name: str = None) -> str:
        """
        전체 AI 파이프라인 실행
        
        Args:
            uploads_input_path: 업로드된 원본 OBJ 경로
            outputs_base_dir: 저장 루트 (storage/outputs)
            group_id: 그룹 ID (기본값: "1")
            folder_name: 폴더명 (기본값: None, 파일명 기반으로 생성)
            
        Returns:
            최종 출력 파일 경로
        """
        self._update_progress(0, "AI 파이프라인 시작...")
        
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
            
            # 네트워크 경로 또는 절대 경로 처리
            outputs_root_str = str(outputs_base_dir)
            
            # 네트워크 경로인지 확인 (UNC 경로 또는 로컬 마운트된 네트워크 경로)
            import re
            is_unc_path = (outputs_root_str.startswith('\\\\') or outputs_root_str.startswith('//'))
            mounted_pattern = re.compile(r'^[A-Z]:\\([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}|[A-Za-z0-9_-]+)\\')
            is_mounted_network = bool(mounted_pattern.match(outputs_root_str))
            
            # 네트워크 경로인 경우 문자열 직접 조작 (Path 객체는 UNC 경로에서 parent 연산이 이상하게 동작할 수 있음)
            if is_unc_path or is_mounted_network:
                # UNC 경로 또는 로컬 마운트된 네트워크 경로 처리
                if outputs_root_str.endswith('\\') or outputs_root_str.endswith('/'):
                    outputs_root_str = outputs_root_str.rstrip('\\/')
                
                # storage 경로 추출 (outputs가 포함된 경우 제거)
                # 예: \\server\share\storage\outputs -> \\server\share\storage
                # 또는: C:\70.12.246.48\storage\outputs -> C:\70.12.246.48\storage
                # 또는: //70.12.246.48/storage/outputs -> //70.12.246.48/storage
                storage_base = outputs_root_str.rstrip('\\/')
                
                # outputs 제거 (끝에 있는 경우)
                if storage_base.lower().endswith('\\outputs'):
                    storage_base = storage_base[:-8]  # '\\outputs' 제거
                elif storage_base.lower().endswith('/outputs'):
                    storage_base = storage_base[:-8]  # '/outputs' 제거
                elif storage_base.lower().endswith('outputs'):
                    storage_base = storage_base[:-7]  # 'outputs' 제거
                
                # 경로 정규화 (끝에 백슬래시 제거)
                storage_base = storage_base.rstrip('\\/')
                
                # storage\storage 또는 storage/storage 중복 제거 (버그 방지)
                # 여러 번 반복될 수 있으므로 while 루프 사용
                while '\\storage\\storage' in storage_base:
                    storage_base = storage_base.replace('\\storage\\storage', '\\storage')
                while '/storage/storage' in storage_base:
                    storage_base = storage_base.replace('/storage/storage', '/storage')
                # 혼합된 경우도 처리
                while '\\storage/storage' in storage_base:
                    storage_base = storage_base.replace('\\storage/storage', '\\storage')
                while '/storage\\storage' in storage_base:
                    storage_base = storage_base.replace('/storage\\storage', '/storage')
                
                # 네트워크 경로 생성
                # 중간 산출물: storage\outputs\optimized\{group_id}\{folder_name}
                # 최종 산출물: storage\outputs\final\{group_id}\{folder_name}
                # 백슬래시 사용 (Windows 경로)
                sep = '\\' if '\\' in storage_base else '/'
                temp_dir_str = f"{storage_base}{sep}temp"
                optimized_dir_str = f"{storage_base}{sep}outputs{sep}optimized{sep}{group_id}{sep}{folder_name}"
                final_dir_str = f"{storage_base}{sep}outputs{sep}final{sep}{group_id}{sep}{folder_name}"
                
                # Path 객체 생성 (UNC 경로는 문자열로 직접 조작)
                # UNC 경로의 경우 Path 객체가 제대로 작동하지 않을 수 있으므로 문자열로 처리
                temp_dir = Path(temp_dir_str)
                optimized_dir = Path(optimized_dir_str)
                final_dir = Path(final_dir_str)
                
                # 디렉토리 생성 및 접근 확인
                try:
                    optimized_dir.mkdir(parents=True, exist_ok=True)
                    logger.info(f"[경로 생성] optimized_dir 생성: {optimized_dir_str} (존재: {optimized_dir.exists()})")
                except Exception as e:
                    logger.error(f"[경로 생성] optimized_dir 생성 실패: {optimized_dir_str}, 오류: {e}")
                    raise
                
                try:
                    final_dir.mkdir(parents=True, exist_ok=True)
                    logger.info(f"[경로 생성] final_dir 생성: {final_dir_str} (존재: {final_dir.exists()})")
                except Exception as e:
                    logger.error(f"[경로 생성] final_dir 생성 실패: {final_dir_str}, 오류: {e}")
                    raise
            else:
                # 일반 경로는 Path 객체 사용
                outputs_root = Path(outputs_base_dir)
                temp_dir = outputs_root.parent / "temp"
                optimized_dir = outputs_root / "optimized" / group_id / folder_name
                final_dir = outputs_root / "final" / group_id / folder_name
            
            logger.info(f"[AIPipeline] outputs_base_dir: {outputs_base_dir}")
            logger.info(f"[AIPipeline] temp_dir: {temp_dir} (문자열: {temp_dir_str if (is_unc_path or is_mounted_network) else str(temp_dir)})")
            logger.info(f"[AIPipeline] optimized_dir: {optimized_dir} (문자열: {optimized_dir_str if (is_unc_path or is_mounted_network) else str(optimized_dir)})")
            logger.info(f"[AIPipeline] final_dir: {final_dir} (문자열: {final_dir_str if (is_unc_path or is_mounted_network) else str(final_dir)})")

            # 1단계: 메쉬 최적화 → outputs/optimized
            optimized_path = self.run_mesh_optimizer(uploads_input_path, temp_dir, optimized_dir)

            # 2단계: 메쉬 노이즈 제거 → outputs/optimized (final_dir)
            final_path = self.run_mesh_denoiser(
                optimized_path,
                final_dir,
            )
            
            # 최종 경로 확인 및 검증
            final_path_str = str(final_path)
            logger.info(f"[AIPipeline] 최종 경로: {final_path_str}")
            
            # 파일 존재 확인
            if not final_path.exists():
                logger.error(f"[AIPipeline] 경고: 최종 파일이 존재하지 않습니다: {final_path_str}")
                # 파일이 없어도 경로는 반환 (에러 처리 상위에서)
            else:
                file_size = final_path.stat().st_size if final_path.exists() else 0
                logger.info(f"[AIPipeline] 최종 파일 확인 - 경로: {final_path_str}, 크기: {file_size} bytes, 존재: {final_path.exists()}")
            
            self._update_progress(100, f"AI 파이프라인 완료: {final_path_str}")
            return final_path_str
            
        except Exception as e:
            error_msg = f"AI 파이프라인 실행 중 오류: {str(e)}"
            logger.error(error_msg)
            raise RuntimeError(error_msg)
