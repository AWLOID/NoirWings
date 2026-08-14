import os

from fastapi import FastAPI

from api.config import settings
from api.routes.obfuscate import router as obfuscate_router
from api.routes.health import router as health_router

app = FastAPI(
    title="NoirWings API",
    description="HTTP API for NoirWings Lua VM obfuscator",
    version="1.0.0",
)

app.include_router(obfuscate_router)
app.include_router(health_router)


@app.on_event("startup")
async def startup():
    os.makedirs(settings.work_dir, exist_ok=True)


if __name__ == "__main__":
    import uvicorn
    port = int(os.environ.get("PORT", 8000))
    uvicorn.run(app, host="0.0.0.0", port=port)
