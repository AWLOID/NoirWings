from pydantic_settings import BaseSettings
from pydantic import field_validator


class Settings(BaseSettings):
    bot_token: str
    api_url: str = "http://api:8000"
    api_key: str
    database_url: str = "postgresql+asyncpg://noirwings:noirwings@postgres:5432/noirwings"
    admin_ids: list[int] = []

    @field_validator("database_url", mode="before")
    @classmethod
    def fix_db_scheme(cls, v: str) -> str:
        """Railway gives postgresql://, asyncpg needs postgresql+asyncpg://"""
        if v and v.startswith("postgresql://"):
            v = v.replace("postgresql://", "postgresql+asyncpg://", 1)
        elif v and v.startswith("postgres://"):
            v = v.replace("postgres://", "postgresql+asyncpg://", 1)
        return v

    model_config = {"env_file": ".env", "env_file_encoding": "utf-8"}


settings = Settings()
