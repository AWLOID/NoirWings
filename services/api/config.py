from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    api_key: str
    database_url: str = "postgresql+asyncpg://noirwings:noirwings@postgres:5432/noirwings"
    noirwings_dll: str = "/opt/noirwings/NoirWings.Vm.dll"
    dotnet_path: str = "dotnet"
    work_dir: str = "/tmp/noirwings"
    max_input_size_kb: int = 512
    obfuscation_timeout: int = 120

    model_config = {"env_file": ".env", "env_file_encoding": "utf-8"}


settings = Settings()
