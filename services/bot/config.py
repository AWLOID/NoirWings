from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    bot_token: str
    api_url: str = "http://api:8000"
    api_key: str
    database_url: str = "postgresql+asyncpg://noirwings:noirwings@postgres:5432/noirwings"
    admin_ids: list[int] = []

    model_config = {"env_file": ".env", "env_file_encoding": "utf-8"}


settings = Settings()
