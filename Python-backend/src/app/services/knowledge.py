"""知识库管理与入库业务入口。"""

from app.schemas.knowledge import CreateKnowledgeBaseRequest, IngestRequest, KnowledgeBaseResponse
from app.schemas.tasks import TaskAcceptedResponse


def create_knowledge_base(request: CreateKnowledgeBaseRequest) -> KnowledgeBaseResponse:
    """预留知识库创建入口。

    Args:
        request: 新知识库的名称与说明。

    Returns:
        实现后返回已保存的知识库标识与元数据。

    Raises:
        NotImplementedError: 当前不会写入存储。
    """
    # TODO(to be confirmed): 保存知识库元数据并返回知识库标识。
    raise NotImplementedError("知识库创建尚未实现")


def list_knowledge_bases() -> list[KnowledgeBaseResponse]:
    """预留知识库列表入口。

    Returns:
        实现后返回已保存的知识库列表。

    Raises:
        NotImplementedError: 当前尚未接入存储。
    """
    # TODO(to be confirmed): 从选定的持久化存储查询知识库列表。
    raise NotImplementedError("知识库列表尚未实现")


def ingest_files(knowledge_base_id: str, request: IngestRequest) -> TaskAcceptedResponse:
    """预留入库任务提交入口。

    Args:
        knowledge_base_id: 目标知识库标识。
        request: 已上传的文件标识列表。

    Returns:
        实现后返回已保存并调度的入库任务标识。

    Raises:
        NotImplementedError: 当前不会创建任务或写入索引。
    """
    # TODO(to be confirmed): 校验库与文件，持久化任务，再调度解析、分块、向量化及索引写入。
    raise NotImplementedError("知识库入库尚未实现")
