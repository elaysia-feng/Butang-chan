"""知识库问答输入及来源引用。"""

from pydantic import BaseModel, Field


class ChatRequest(BaseModel):
    """指定知识库范围的问题；空会话标识表示发起新会话。"""

    question: str = Field(min_length=1, max_length=10000)
    knowledge_base_ids: list[str] = Field(min_length=1)
    conversation_id: str | None = None


class Citation(BaseModel):
    """回答的原始文件引用；页码从 1 开始，未知时省略。"""

    file_id: str
    filename: str
    excerpt: str
    page: int | None = Field(default=None, ge=1)


class ChatResponse(BaseModel):
    """问答结果及后续多轮对话可复用的会话标识。"""

    answer: str
    conversation_id: str
    citations: list[Citation] = Field(default_factory=list)
