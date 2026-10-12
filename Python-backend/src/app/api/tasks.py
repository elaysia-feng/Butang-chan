"""处理任务轮询 HTTP 边界。"""

from fastapi import APIRouter

from app.schemas.tasks import TaskResponse
from app.services import tasks

router = APIRouter(prefix="/tasks", tags=["任务"])


@router.get("/{task_id}", response_model=TaskResponse)
def get_task(task_id: str) -> TaskResponse:
    return tasks.get_task(task_id)
