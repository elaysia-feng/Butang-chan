"""文档解析和生成新文档的业务入口。"""

from app.schemas.documents import ParseDocumentRequest, ProcessDocumentRequest
from app.schemas.tasks import TaskAcceptedResponse


def parse_document(request: ParseDocumentRequest) -> TaskAcceptedResponse:
    """预留解析任务提交入口。

    Args:
        request: 已上传文件标识及 OCR 选项。

    Returns:
        实现后返回已保存并调度的任务标识。

    Raises:
        NotImplementedError: 当前不会创建任务。
    """
    # TODO(to be confirmed): 按实际格式选择解析器或 OCR，保存任务并产出文本、结构及页码信息。
    raise NotImplementedError("文档解析尚未实现")


def process_document(request: ProcessDocumentRequest) -> TaskAcceptedResponse:
    """预留文档处理任务入口。

    Args:
        request: 已上传文件标识、处理类型及水印文本选项。

    Returns:
        实现后返回生成新文档的任务标识。

    Raises:
        NotImplementedError: 当前不会创建任务或改写文件。
    """
    # TODO(to be confirmed): 按文档格式实现去水印，保留原件并生成独立结果文件；不支持的格式明确失败。
    raise NotImplementedError("文档处理尚未实现")
