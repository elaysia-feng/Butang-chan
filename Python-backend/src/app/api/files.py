"""桌宠文件上传及结果下载 HTTP 边界。"""

from fastapi import APIRouter, UploadFile
from fastapi.responses import FileResponse

from app.schemas.files import UploadedFileResponse
from app.services import files

router = APIRouter(prefix="/files", tags=["文件"])


@router.post("", response_model=UploadedFileResponse)
def upload_file(file: UploadFile) -> UploadedFileResponse:
    return files.upload_file(file.filename or "upload", file.file, file.content_type)


@router.get("/artifacts/{artifact_id}", response_class=FileResponse)
def download_artifact(artifact_id: str) -> FileResponse:
    """通过后端结果标识下载新文档，业务定位逻辑尚未实现。

    Args:
        artifact_id: 任务结果中的文件标识，不接受任意文件路径。

    Returns:
        业务实现后返回带下载文件名的附件响应。
    """
    # 1. 由业务边界定位允许下载的结果文件。
    path, filename, media_type = files.download_artifact(artifact_id)
    # 2. 在 HTTP 边界组装附件响应。
    return FileResponse(path, filename=filename, media_type=media_type)
