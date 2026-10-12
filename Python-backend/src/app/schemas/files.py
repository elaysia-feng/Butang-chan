"""上传文件与结果文件的外部契约。"""

from pydantic import BaseModel, Field


class UploadedFileResponse(BaseModel):
    """后续操作引用后端生成的文件标识，不提交客户端绝对路径。"""

    file_id: str
    filename: str
    size_bytes: int = Field(ge=0)
    media_type: str | None = None


class ArtifactResponse(BaseModel):
    """任务生成的新文件；客户端通过下载接口获取内容。"""

    artifact_id: str
    filename: str
    media_type: str | None = None
