import asyncio
import os
import tempfile
import time
import uuid

from api.config import settings


class ObfuscationResult:
    def __init__(self, success: bool, output_path: str | None = None, error: str | None = None, duration_ms: int = 0):
        self.success = success
        self.output_path = output_path
        self.error = error
        self.duration_ms = duration_ms


async def run_obfuscation(
    input_bytes: bytes,
    filename: str,
    profile: str = "hardened",
    seed: int | None = None,
    inner_string_encryption: bool = False,
) -> ObfuscationResult:
    """Run NoirWings.Vm.dll on the input Lua file and return the result."""

    job_id = uuid.uuid4().hex[:12]
    work_dir = os.path.join(settings.work_dir, f"job-{job_id}")
    os.makedirs(work_dir, exist_ok=True)

    input_path = os.path.join(work_dir, f"input_{filename}")
    output_path = os.path.join(work_dir, f"output_{filename}")

    try:
        # Write input file
        with open(input_path, "wb") as f:
            f.write(input_bytes)

        # Build command
        cmd = [
            settings.dotnet_path,
            settings.noirwings_dll,
            "--input", input_path,
            "--output", output_path,
            "--profile", profile,
            "--work-root", work_dir,
        ]

        if seed is not None:
            cmd.extend(["--seed", str(seed)])
        if inner_string_encryption:
            cmd.append("--inner-string-encryption")

        # Run the obfuscator
        start_time = time.monotonic()
        proc = await asyncio.create_subprocess_exec(
            *cmd,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
            cwd=work_dir,
        )

        try:
            stdout, stderr = await asyncio.wait_for(
                proc.communicate(), timeout=settings.obfuscation_timeout
            )
        except asyncio.TimeoutError:
            proc.kill()
            await proc.wait()
            return ObfuscationResult(
                success=False,
                error=f"Obfuscation timed out after {settings.obfuscation_timeout}s",
                duration_ms=int((time.monotonic() - start_time) * 1000),
            )

        duration_ms = int((time.monotonic() - start_time) * 1000)

        if proc.returncode != 0:
            error_text = stderr.decode("utf-8", errors="replace").strip()
            if not error_text:
                error_text = stdout.decode("utf-8", errors="replace").strip()
            return ObfuscationResult(
                success=False,
                error=error_text or f"Process exited with code {proc.returncode}",
                duration_ms=duration_ms,
            )

        if not os.path.exists(output_path):
            return ObfuscationResult(
                success=False,
                error="Obfuscation completed but no output file was produced",
                duration_ms=duration_ms,
            )

        return ObfuscationResult(
            success=True,
            output_path=output_path,
            duration_ms=duration_ms,
        )

    except Exception as e:
        return ObfuscationResult(success=False, error=str(e))


def cleanup_job(work_dir: str) -> None:
    """Remove job working directory."""
    import shutil
    try:
        if os.path.exists(work_dir) and "job-" in work_dir:
            shutil.rmtree(work_dir)
    except OSError:
        pass
