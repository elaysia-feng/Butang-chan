"""本机后端配置，不在导入时连接外部服务。"""

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """从环境变量读取配置；默认只监听本机。"""

    model_config = SettingsConfigDict(env_prefix="BUTANG_", extra="ignore")

    host: str = "127.0.0.1"
    port: int = Field(default=8765, ge=1, le=65535)
