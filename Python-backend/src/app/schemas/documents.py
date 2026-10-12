"""独立解析与生成新文档的操作契约。"""

from typing import Literal

from pydantic import BaseModel, Field


class ParseDocumentRequest(BaseModel):
    """独立解析已上传文件，不自动入库。"""

    file_id: str = Field(min_length=1)
    enable_ocr: bool = False


class ProcessDocumentRequest(BaseModel):
    """首个预留操作为去水印；输出新文件，原始上传文件保留。"""

    file_id: str = Field(min_length=1)
    operation: Literal["remove_watermark"]
    watermark_text: str | None = Field(default=None, max_length=1000)
