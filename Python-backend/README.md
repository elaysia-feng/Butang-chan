# 不糖酱 Python 后端骨架

使用 FastAPI，遵循 code-dev 的 `src/app/` 包布局。这里只提供启动入口、接口契约及业务 TODO，知识库、模型调用、文件保存、任务执行和文档处理由你实现。业务接口目前返回 HTTP 501，不会生成虚假的任务或处理结果。

## 目录职责

```text
Python-backend/
├── pyproject.toml          # 依赖、包发现和安装后的启动命令
├── .env.example           # 环境变量示例
├── docs/
│   └── desktop-integration.md
└── src/app/
    ├── main.py            # 应用组装、占位错误映射及健康检查
    ├── api/               # HTTP 参数解析、响应及路由
    ├── core/config.py     # 运行配置
    ├── schemas/           # 请求与响应模型
    └── services/          # 你填写业务逻辑的位置
```

目前没有选定数据库、向量库或任务队列，因此没有提前创建 models、repositories、memory、graphs 等目录。需要 LangGraph 编排时再按规范添加所属图的文件；普通检索问答也可以直接从 `services/chat.py` 开始。

## 安装与启动

在仓库根目录执行（Python 3.11 或更高版本）：

```powershell
uv venv Python-backend/.venv --python 3.11
uv pip install --python Python-backend/.venv/Scripts/python.exe -e "./Python-backend[dev]"
& ./Python-backend/.venv/Scripts/butang-backend.exe
```

也可以使用标准 Python 的 venv 和 pip 安装。安装后的入口不依赖当前工作目录。配置从环境变量读取，`.env.example` 是参考模板，不会自动加载 `.env`：

```powershell
$env:BUTANG_HOST = "127.0.0.1"
$env:BUTANG_PORT = "8765"
& ./Python-backend/.venv/Scripts/butang-backend.exe
```

启动后打开 `http://127.0.0.1:8765/docs` 查看交互文档，`GET /health` 验证进程可响应。健康检查不会验证数据库或模型。

## 预留接口

| 方法 | 路径 | 用途 | 填写位置 |
| --- | --- | --- | --- |
| POST | `/api/files` | multipart 上传，字段名 `file` | `services/files.py` |
| GET | `/api/files/artifacts/{artifact_id}` | 下载生成的新文档 | `services/files.py` |
| POST / GET | `/api/knowledge-bases` | 创建 / 列出知识库 | `services/knowledge.py` |
| POST | `/api/knowledge-bases/{knowledge_base_id}/ingestions` | 提交入库任务 | `services/knowledge.py` |
| POST | `/api/chat` | 基于指定知识库问答 | `services/chat.py` |
| POST | `/api/documents/parse` | 独立解析文件，不自动入库 | `services/documents.py` |
| POST | `/api/documents/process` | 去水印并生成新文件 | `services/documents.py` |
| GET | `/api/tasks/{task_id}` | 查询状态、进度和结果 | `services/tasks.py` |

上传、解析、入库与文档处理是独立操作。原始文件以 `file_id` 引用；新文件以 `artifact_id` 引用；后台操作以 `task_id` 查询。实现任务服务后，提交接口才能返回 202；当前统一返回 501。错误格式：

```json
{"code":"NOT_IMPLEMENTED","detail":"文档处理尚未实现"}
```

## 建议实现顺序

1. 文件上传与受控存储：校验大小、真实格式及文件名，保存原件和文件元数据。
2. 文档解析：文本提取、必要时 OCR，保留页码及结构。
3. 任务持久化及执行：不要把耗时处理直接塞进 HTTP 请求；明确失败、重启恢复及清理策略。
4. 知识库管理和入库：分块、向量化、索引写入及重复入库规则。
5. 问答：检索、模型调用、真实引用、无相关知识时的回答策略及会话记录。
6. 文档处理：按 PDF、DOCX 等格式分别实现去水印；对于扫描件及图片水印单独评估，不假设一种方法适用于全部文件。保存新文档，保留原件。

TODO 的负责人暂写为 `to be confirmed`，后续按你的项目负责人约定替换。数据库连接、模型实例等需要实现时再添加；不要在包 `__init__.py` 内初始化。

C# 接入边界与请求示例见 [桌宠接入说明](docs/desktop-integration.md)。

已执行的验证及规范检查器人工豁免说明见 [验证记录](docs/verification.md)。
