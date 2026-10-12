"""文件接收、存储及结果下载的业务入口。"""

from typing import BinaryIO

from app.schemas.files import UploadedFileResponse


def upload_file(filename: str, content: BinaryIO, media_type: str | None) -> UploadedFileResponse:
    """预留上传入口，当前抛出 NotImplementedError。

    Args:
        filename: 客户端原始文件名，不作为可信存储路径。
        content: 请求期间有效的文件流，需在返回前处理或复制。
        media_type: 客户端声明的媒体类型，后续需核验实际格式。

    Returns:
        实现后返回文件标识及元数据。

    Raises:
        NotImplementedError: 当前不会保存文件。
    """
    # TODO(to be confirmed): 校验格式、大小及文件名，生成 file_id 并保存原件和元数据。
    raise NotImplementedError("文件上传尚未实现")


def download_artifact(artifact_id: str) -> tuple[str, str, str | None]:
    """预留结果定位入口，当前抛出 NotImplementedError。

    Args:
        artifact_id: 后端生成的结果标识，不允许用它直接拼接路径。

    Returns:
        实现后返回受控文件路径、下载文件名与媒体类型。

    Raises:
        NotImplementedError: 当前尚未接入结果存储。
    """
    # TODO(to be confirmed): 按结果元数据定位文件并检查存在性及访问范围。
    raise NotImplementedError("结果下载尚未实现")
