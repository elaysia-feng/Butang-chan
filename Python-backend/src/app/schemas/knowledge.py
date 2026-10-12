"""知识库管理及文件入库契约。"""

from pydantic import BaseModel, Field


class CreateKnowledgeBaseRequest(BaseModel):
    """新建知识库的名称与用途说明。"""

    name: str = Field(min_length=1, max_length=100)
    description: str = Field(default="", max_length=1000)


class KnowledgeBaseResponse(BaseModel):
    """供桌宠展示和选择的知识库信息。"""

    knowledge_base_id: str
    name: str
    description: str


class IngestRequest(BaseModel):
    """仅引用已上传文件；解析文件不代表已经入库。"""

    file_ids: list[str] = Field(min_length=1)
