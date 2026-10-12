"""独立文档解析及处理 HTTP 边界。"""

from fastapi import APIRouter

from app.schemas.documents import ParseDocumentRequest, ProcessDocumentRequest
from app.schemas.tasks import TaskAcceptedResponse
from app.services import documents

router = APIRouter(prefix="/documents", tags=["文档"])


@router.post("/parse", response_model=TaskAcceptedResponse, status_code=202)
def parse_document(request: ParseDocumentRequest) -> TaskAcceptedResponse:
    return documents.parse_document(request)


@router.post("/process", response_model=TaskAcceptedResponse, status_code=202)
def process_document(request: ProcessDocumentRequest) -> TaskAcceptedResponse:
    return documents.process_document(request)
