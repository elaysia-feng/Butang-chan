"""知识库管理与入库 HTTP 边界。"""

from fastapi import APIRouter

from app.schemas.knowledge import CreateKnowledgeBaseRequest, IngestRequest, KnowledgeBaseResponse
from app.schemas.tasks import TaskAcceptedResponse
from app.services import knowledge

router = APIRouter(prefix="/knowledge-bases", tags=["知识库"])


@router.post("", response_model=KnowledgeBaseResponse)
def create_knowledge_base(request: CreateKnowledgeBaseRequest) -> KnowledgeBaseResponse:
    return knowledge.create_knowledge_base(request)


@router.get("", response_model=list[KnowledgeBaseResponse])
def list_knowledge_bases() -> list[KnowledgeBaseResponse]:
    return knowledge.list_knowledge_bases()


@router.post("/{knowledge_base_id}/ingestions", response_model=TaskAcceptedResponse, status_code=202)
def ingest_files(knowledge_base_id: str, request: IngestRequest) -> TaskAcceptedResponse:
    return knowledge.ingest_files(knowledge_base_id, request)
