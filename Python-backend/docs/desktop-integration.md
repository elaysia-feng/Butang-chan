# C# 桌宠与 Python 后端接入边界

当前宿主是 WinForms：`src/PetForm.cs` 管理桌宠窗口，`src/ChatForm.cs` 管理聊天界面，`src/LlmClient.cs` 是已有模型客户端。本次仅新增 Python 骨架，没有修改现有聊天或拖拽逻辑。

## C# 负责的交互

- 接收从资源管理器拖入的文件，识别 `DataFormats.FileDrop`；使用 `AllowDrop`、`DragEnter`、`DragDrop`。
- 显示文件名和操作菜单：进入知识库、知识解析、文档处理（例如去水印）。
- 入库前让用户选择目标知识库；文档处理前收集所需选项。
- 使用异步 HTTP 请求上传文件及提交操作，保持 UI 可响应；取消菜单时不提交任务。
- 根据 `task_id` 轮询状态，显示进度或错误；拿到 `artifact_id` 后下载到用户选择的新路径。
- 在聊天界面选择知识库，再请求问答接口，展示来源引用。

WinForms 官方拖放说明：https://learn.microsoft.com/en-us/dotnet/desktop/winforms/input-mouse/drag-and-drop

当前窗口使用逐像素透明绘制，后续需要实测文件拖到角色可见区域时的命中行为。现有鼠标拖动桌宠位置的事件不等于接收外部文件；实现文件拖放时要验证两种交互是否冲突。当前桌宠进程与资源管理器的权限级别也需要一致，验收时分别验证正常运行和管理员运行场景。

## 请求顺序

1. 文件拖入后展示处理选项。
2. 用户确认操作后，逐个以 multipart 字段 `file` 上传到 `POST /api/files`。
3. 获取 `file_id`，根据所选操作提交下面的 JSON 请求。
4. 获取 `task_id`，查询 `GET /api/tasks/{task_id}`。
5. 成功后显示解析文本或下载新文档；失败时显示错误；501 表示功能尚未实现。

不能把 `C:\\Users\\...` 等客户端绝对路径直接交给后端打开。文件内容通过上传传输，Python 后端按自己的受控存储标识读取。

### 进入知识库

`POST /api/knowledge-bases/{knowledge_base_id}/ingestions`

```json
{"file_ids":["上传返回的文件标识"]}
```

### 知识解析

`POST /api/documents/parse`

```json
{"file_id":"上传返回的文件标识","enable_ocr":false}
```

这里返回独立解析任务，后续实现可将文本放在任务结果的 `result_text`，或生成可下载解析文件；不会自动写入知识库。

### 去水印并生成新文档

`POST /api/documents/process`

```json
{"file_id":"上传返回的文件标识","operation":"remove_watermark","watermark_text":"示例水印"}
```

`watermark_text` 可不传；具体处理能力及支持格式由你实现。其他文档处理能力确定后再扩展 operation 枚举及对应选项模型。

### 知识库问答

`POST /api/chat`

```json
{"question":"这份文档讲了什么？","knowledge_base_ids":["知识库标识"],"conversation_id":null}
```

当前契约是普通 JSON 响应。流式问答需要另行定义 SSE 事件、终止事件和错误事件。现有 `LlmClient` 调用的是模型厂商协议，未来接入此后端时应明确增加后端客户端并选择问答入口，不把厂商协议直接发送到 `/api/chat`。

## 后续验收

拖入单文件和多文件、取消操作、选库后入库、解析不入库、去水印生成新文件、失败反馈、重复提交、后端不可达，以及关闭窗口后重新查询任务。拖放交互及以上业务目前尚未实现，本说明提供后续接入契约。
