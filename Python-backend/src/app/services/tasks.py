"""任务状态和结果的业务查询入口。"""

from app.schemas.tasks import TaskResponse


def get_task(task_id: str) -> TaskResponse:
    """预留任务查询入口。

    Args:
        task_id: 提交处理操作后取得的任务标识。

    Returns:
        实现后返回状态、进度、失败原因和可用结果。

    Raises:
        NotImplementedError: 当前尚未接入任务存储。
    """
    # TODO(to be confirmed): 查询持久化任务状态、进度、解析文本及结果文件；不存在时返回领域错误。
    raise NotImplementedError("任务查询尚未实现")
