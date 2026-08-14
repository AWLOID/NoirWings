import aiohttp

from bot.config import settings


class NoirWingsAPIClient:
    def __init__(self):
        self._session: aiohttp.ClientSession | None = None

    async def _get_session(self) -> aiohttp.ClientSession:
        if self._session is None or self._session.closed:
            self._session = aiohttp.ClientSession(
                base_url=settings.api_url,
                headers={"X-API-Key": settings.api_key},
                timeout=aiohttp.ClientTimeout(total=180),
            )
        return self._session

    async def obfuscate(
        self,
        file_bytes: bytes,
        filename: str,
        profile: str = "hardened",
        seed: int | None = None,
        inner_string_encryption: bool = False,
    ) -> dict:
        """Send a file to the NoirWings API for obfuscation."""
        session = await self._get_session()

        data = aiohttp.FormData()
        data.add_field("file", file_bytes, filename=filename, content_type="text/x-lua")
        data.add_field("profile", profile)
        if seed is not None:
            data.add_field("seed", str(seed))
        if inner_string_encryption:
            data.add_field("inner_string_encryption", "true")

        async with session.post("/api/v1/obfuscate", data=data) as resp:
            if resp.status == 200:
                output_data = await resp.read()
                return {
                    "success": True,
                    "data": output_data,
                    "duration_ms": int(resp.headers.get("X-Duration-Ms", 0)),
                    "output_size": int(resp.headers.get("X-Output-Size", len(output_data))),
                }
            else:
                try:
                    error_body = await resp.json()
                    error = error_body.get("detail", f"HTTP {resp.status}")
                except Exception:
                    error = await resp.text()
                return {"success": False, "error": error}

    async def health(self) -> dict:
        """Check API health."""
        session = await self._get_session()
        async with session.get("/api/v1/health") as resp:
            return await resp.json()

    async def close(self):
        if self._session and not self._session.closed:
            await self._session.close()
