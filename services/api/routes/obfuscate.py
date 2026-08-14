import os

from fastapi import APIRouter, Depends, File, Form, UploadFile, HTTPException
from fastapi.responses import FileResponse

from api.auth import verify_api_key
from api.config import settings
from api.worker import run_obfuscation, cleanup_job

router = APIRouter(prefix="/api/v1")


@router.post("/obfuscate", dependencies=[Depends(verify_api_key)])
async def obfuscate(
    file: UploadFile = File(...),
    profile: str = Form("hardened"),
    seed: int | None = Form(None),
    inner_string_encryption: bool = Form(False),
):
    """Obfuscate a Lua file using NoirWings VM."""

    if profile not in ("balanced", "hardened", "maximum"):
        raise HTTPException(status_code=400, detail="Profile must be balanced, hardened, or maximum")

    content = await file.read()
    if len(content) > settings.max_input_size_kb * 1024:
        raise HTTPException(
            status_code=413,
            detail=f"File too large. Maximum is {settings.max_input_size_kb}KB",
        )

    if not file.filename or not file.filename.endswith(".lua"):
        raise HTTPException(status_code=400, detail="File must be a .lua file")

    result = await run_obfuscation(
        input_bytes=content,
        filename=file.filename,
        profile=profile,
        seed=seed,
        inner_string_encryption=inner_string_encryption,
    )

    if not result.success:
        raise HTTPException(status_code=500, detail=result.error)

    # Return the obfuscated file
    response = FileResponse(
        result.output_path,
        media_type="text/x-lua",
        filename=f"obf_{file.filename}",
        headers={
            "X-Duration-Ms": str(result.duration_ms),
            "X-Output-Size": str(os.path.getsize(result.output_path)),
        },
    )

    # Schedule cleanup after response (simplified — in production use BackgroundTasks)
    return response
