# 骨架验证记录

2026-10-12，Python 3.11 独立虚拟环境，开发模式安装。

## 已验证

- 全部 Python 源码可解析。
- `/docs`、`/openapi.json`、`/health` 可响应；OpenAPI 包含 9 个路径、10 个操作。
- 9 个业务占位操作均返回 501，错误码为 `NOT_IMPLEMENTED`，包括 multipart 文件上传。
- 空问题、空知识库范围、未知处理类型、空文件标识及缺少上传文件的校验检查通过（共 4 个请求）。
- 从仓库根目录、Python-backend 目录及系统临时目录导入已安装应用成功，没有修改 `sys.path`。
- 从系统临时目录启动安装后的 `butang-backend.exe`，实际 HTTP 健康请求成功；临时服务已关闭。
- `git diff --check` 通过。
- 抽象检查器无 WARNING，仅对 8 个单行路由给出 INFO；这些函数保留 FastAPI HTTP 边界，业务逻辑位于 services。

TestClient 冒烟验证出现 Starlette 对 httpx 的弃用提示，但检查通过；该提示来自验证依赖，不代表实际 HTTP 启动失败。未来维护测试时按所选 FastAPI/Starlette 版本更新验证客户端。

## 注释检查器逐项人工核对

运行 `check_comments.py Python-backend/src --fail-on warning` 得到 0 ERROR、21 WARNING，严格检查退出码为 1。以下逐项按 code-dev 注释规范核对；不宣称检查器零警告通过。

### 8 个 missing-doc 提示

规范允许“简单访问器和纯转发”省略文档。下列函数只有调用 service 并返回结果，路由路径、请求与响应模型均由装饰器及类型声明提供；没有额外业务阶段，因此不添加编号或重复文档：

| 文件 | 函数 |
| --- | --- |
| `api/chat.py` | `answer_question` |
| `api/documents.py` | `parse_document`、`process_document` |
| `api/files.py` | `upload_file` |
| `api/knowledge.py` | `create_knowledge_base`、`list_knowledge_bases`、`ingest_files` |
| `api/tasks.py` | `get_task` |

### 13 个 missing-attributes 提示

规范要求文档说明边界和特殊值，不重复字段类型或显然的含义；Attributes 小节不是无条件要求。以下均为配置或 Pydantic 数据定义，字段类型、默认值、Field 约束与类说明已经表达契约，逐项不补机械的属性列表：

| 文件 | 类及边界依据 |
| --- | --- |
| `core/config.py` | `Settings`：环境变量前缀、本机默认监听及端口范围 |
| `schemas/chat.py` | `ChatRequest`：指定库范围、空会话含义；`Citation`：页码从 1 开始且可为空；`ChatResponse`：答案、会话和来源 |
| `schemas/documents.py` | `ParseDocumentRequest`：独立解析不入库；`ProcessDocumentRequest`：去水印枚举、新文件与原件分离 |
| `schemas/files.py` | `UploadedFileResponse`：后端文件标识、不接收绝对路径；`ArtifactResponse`：结果标识用于下载 |
| `schemas/knowledge.py` | `CreateKnowledgeBaseRequest`：名称及说明的长度；`KnowledgeBaseResponse`：供界面选择的库信息；`IngestRequest`：非空已上传文件列表 |
| `schemas/tasks.py` | `TaskAcceptedResponse`：真实保存任务后才能返回；`TaskResponse`：状态枚举、百分比范围及可选文本和结果 |

service 占位函数均已记录 TODO 负责人占位、输入含义、实现后预期响应及当前 NotImplementedError 行为。下载路由和启动函数的真实多阶段流程已编号。尚未实现的业务阶段写在 TODO 内，不伪装成已执行流程。

## 未验证范围

文件持久化、知识库检索与问答、任务执行、文档解析与去水印、C# 文件拖放及端到端交互均未实现。当前验证仅覆盖骨架的安装、启动与接口契约。
