"""检索增强问答的业务入口。"""

from app.schemas.chat import ChatRequest, ChatResponse


def answer_question(request: ChatRequest) -> ChatResponse:
    """预留知识库问答入口。

    Args:
        request: 问题、检索知识库范围及可选会话标识。

    Returns:
        实现后返回答案、会话标识及来源引用。

    Raises:
        NotImplementedError: 当前仅提供骨架，不调用模型。
    """
    # TODO(to be confirmed): 校验知识库范围，检索片段，组装上下文并调用模型，返回答案与真实来源。
    # TODO(to be confirmed): 需要多轮问答时持久化会话；需要流式响应时单独定义 SSE 契约。
    raise NotImplementedError("知识库问答尚未实现")
