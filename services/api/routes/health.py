import os
import shutil

from fastapi import APIRouter, Depends

from api.auth import verify_api_key
from api.config import settings

router = APIRouter()


@router.get("/health")
async def health_root():
    """Root health check for Railway."""
    return {"status": "ok"}


@router.get("/api/v1/health")
async def health():
    """Check if the API and NoirWings runtime are available."""
    dll_exists = os.path.exists(settings.noirwings_dll)
    dotnet_available = shutil.which(settings.dotnet_path) is not None

    return {
        "status": "ok" if (dll_exists and dotnet_available) else "degraded",
        "noirwings_dll": dll_exists,
        "dotnet_available": dotnet_available,
    }


@router.get("/api/v1/stats", dependencies=[Depends(verify_api_key)])
async def stats():
    """Return basic API statistics."""
    work_dir = settings.work_dir
    active_jobs = 0
    if os.path.exists(work_dir):
        active_jobs = len([
            d for d in os.listdir(work_dir)
            if d.startswith("job-") and os.path.isdir(os.path.join(work_dir, d))
        ])

    return {
        "active_jobs": active_jobs,
        "max_input_size_kb": settings.max_input_size_kb,
        "timeout_seconds": settings.obfuscation_timeout,
    }
