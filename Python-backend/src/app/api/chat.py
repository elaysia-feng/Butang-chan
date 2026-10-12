"""知识库问答 HTTP 边界。"""

from fastapi import APIRouter

from app.schemas.chat import ChatRequest, ChatResponse
from app.services import chat

router = APIRouter(prefix="/chat", tags=["问答"])


@router.post("", response_model=ChatResponse)
def answer_question(request: ChatRequest) -> ChatResponse:
    return chat.answer_question(request)
