"""应用组装与可安装启动入口，业务实现保留在 services 中。"""

import uvicorn
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from app.api import chat, documents, files, knowledge, tasks
from app.core.config import Settings

app = FastAPI(title="不糖酱知识库与文档后端", version="0.1.0")
app.include_router(files.router, prefix="/api")
app.include_router(knowledge.router, prefix="/api")
app.include_router(chat.router, prefix="/api")
app.include_router(documents.router, prefix="/api")
app.include_router(tasks.router, prefix="/api")


@app.exception_handler(NotImplementedError)
def handle_unimplemented(request: Request, exc: NotImplementedError) -> JSONResponse:
    """将业务占位异常转成明确的 501，客户端不得展示处理成功。

    Args:
        request: 框架注入的请求，当前不读取其内容。
        exc: 业务占位异常，仅携带尚未实现的功能说明。

    Returns:
        统一的功能未实现错误响应。
    """
    return JSONResponse(status_code=501, content={"code": "NOT_IMPLEMENTED", "detail": str(exc)})


@app.get("/health", tags=["运行状态"])
def health() -> dict[str, str]:
    """仅表示应用进程可响应，不表示业务能力已经实现。

    Returns:
        进程状态和骨架实现标记。
    """
    return {"status": "ok", "implementation": "skeleton"}


def run() -> None:
    """从任意工作目录启动已安装的本机后端。"""
    # 1. 读取环境配置，默认监听本机回环地址。
    settings = Settings()
    # 2. 启动 HTTP 服务，业务依赖后续由各领域接入。
    uvicorn.run("app.main:app", host=settings.host, port=settings.port)
