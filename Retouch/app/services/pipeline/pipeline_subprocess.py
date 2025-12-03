import os
import subprocess
import threading
from typing import Dict, List, Optional, Sequence


class SubprocessError(RuntimeError):
    """파이프라인 서브프로세스 실행 중 발생한 오류."""

    def __init__(self, message: str, returncode: int, output: Optional[str] = None):
        super().__init__(message)
        self.returncode = returncode
        self.output = output or ""


class SubprocessTimeoutError(RuntimeError):
    """파이프라인 서브프로세스가 타임아웃으로 종료된 경우."""

    def __init__(self, message: str):
        super().__init__(message)


def execute_subprocess(
    cmd: Sequence[str],
    *,
    cwd: Optional[str],
    logger,
    prefix: str,
    timeout_sec: int,
    env: Optional[Dict[str, str]] = None,
) -> List[str]:
    """
    파이프라인에서 사용하는 서브프로세스 실행 유틸리티.

    Args:
        cmd: 실행할 명령어 시퀀스
        cwd: 작업 디렉토리
        logger: 로깅에 사용할 logger 인스턴스
        prefix: 로그 프리픽스 (예: "[Optimizer]")
        timeout_sec: 타임아웃(초)
        env: 추가 환경 변수 (기본값: os.environ 기반)

    Returns:
        프로세스가 출력한 로그 라인 리스트

    Raises:
        SubprocessTimeoutError: 타임아웃 초과
        SubprocessError: 비정상 종료
    """
    printable_cmd = " ".join(cmd)
    logger.info(f"{prefix} cwd={cwd} cmd={printable_cmd}")

    merged_env = os.environ.copy()
    if env:
        merged_env.update(env)

    merged_env.setdefault("PYTHONUNBUFFERED", "1")
    merged_env.setdefault("PYTHONIOENCODING", "utf-8")

    process = subprocess.Popen(
        cmd,
        cwd=cwd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        bufsize=1,
        universal_newlines=True,
        env=merged_env,
    )

    output_lines: List[str] = []
    output_lock = threading.Lock()
    read_finished = threading.Event()

    def read_output() -> None:
        try:
            for line in iter(process.stdout.readline, ""):
                stripped = line.rstrip()
                if stripped:
                    logger.info(f"{prefix} {stripped}")
                    with output_lock:
                        output_lines.append(stripped)
            read_finished.set()
        except Exception as exc:  # pragma: no cover - 로깅만 수행
            logger.error(f"{prefix} stdout 읽기 오류: {exc}")
            read_finished.set()

    reader_thread = threading.Thread(target=read_output, daemon=True)
    reader_thread.start()

    return_code: Optional[int] = None
    timeout_reached = threading.Event()

    def wait_process() -> None:
        nonlocal return_code
        try:
            return_code = process.wait()
        except Exception as exc:  # pragma: no cover - 로깅만 수행
            logger.error(f"{prefix} 프로세스 대기 오류: {exc}")
            return_code = -1
        finally:
            timeout_reached.set()

    wait_thread = threading.Thread(target=wait_process, daemon=True)
    wait_thread.start()

    if not timeout_reached.wait(timeout=timeout_sec):
        logger.error(f"{prefix} 프로세스 타임아웃 ({timeout_sec}초 초과) - 강제 종료")
        try:
            process.kill()
            process.wait()
        except Exception as exc:  # pragma: no cover - 로깅만 수행
            logger.error(f"{prefix} 프로세스 종료 실패: {exc}")
        raise SubprocessTimeoutError(f"{prefix} 타임아웃: {timeout_sec}초 초과")

    wait_thread.join(timeout=5)
    read_finished.wait(timeout=5)

    logger.info(f"{prefix} 프로세스 종료 (returncode: {return_code})")

    if return_code is None:
        raise SubprocessError(f"{prefix} 프로세스 반환 코드 확인 실패", -1)

    if return_code < 0:
        raise SubprocessError(f"{prefix} 프로세스가 시그널(returncode {return_code})로 종료되었습니다.", return_code)

    if return_code != 0:
        with output_lock:
            output_text = "\n".join(output_lines)
        raise SubprocessError(f"{prefix} 프로세스 실패 (exit code: {return_code})", return_code, output_text)

    with output_lock:
        return list(output_lines)

