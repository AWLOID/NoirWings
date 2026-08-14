from pydantic_settings import BaseSettings
from pydantic import field_validator


class Settings(BaseSettings):
    api_key: str = ""
    database_url: str = "postgresql+asyncpg://noirwings:noirwings@postgres:5432/noirwings"
    noirwings_dll: str = "/opt/noirwings/NoirWings.Vm.dll"
    dotnet_path: str = "dotnet"
    work_dir: str = "/tmp/noirwings"
    max_input_size_kb: int = 512
    obfuscation_timeout: int = 120
    port: int = 8000

    @field_validator("database_url", mode="before")
    @classmethod
    def fix_db_scheme(cls, v: str) -> str:
        """Railway gives postgresql://, asyncpg needs postgresql+asyncpg://"""
        if v and v.startswith("postgresql://"):
            v = v.replace("postgresql://", "postgresql+asyncpg://", 1)
        elif v and v.startswith("postgres://"):
            v = v.replace("postgres://", "postgresql+asyncpg://", 1)
        return v

    model_config = {"env_file": ".env", "env_file_encoding": "utf-8", "extra": "ignore"}


settings = Settings()
