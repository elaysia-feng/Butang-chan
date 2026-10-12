"""长耗时处理任务的查询契约。"""

from typing import Literal

from pydantic import BaseModel, Field

from app.schemas.files import ArtifactResponse


class TaskAcceptedResponse(BaseModel):
    """仅在真实创建并保存任务之后才能返回此响应。"""

    task_id: str
    status: Literal["queued", "running"]


class TaskResponse(BaseModel):
    """查询任务进度、失败原因及可下载结果。"""

    task_id: str
    operation: Literal["ingest", "parse", "remove_watermark"]
    status: Literal["queued", "running", "succeeded", "failed", "cancelled"]
    progress: int = Field(ge=0, le=100, description="百分比，0 到 100")
    error: str | None = None
    result_text: str | None = None
    artifacts: list[ArtifactResponse] = Field(default_factory=list)
