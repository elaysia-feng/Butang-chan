# Butang-chan · 不糖酱桌宠

面向 Windows 的桌面 AI 伙伴，支持透明角色动画、聊天、提醒、任务反馈和可选视觉输入。桌宠采用 **C# WinForms 原生透明窗口 + 独立 WebView2 面板**，知识库与文档处理预留为独立的 **Python FastAPI 后端**。

## 当前功能

- **角色互动**：待机、情绪动作、摸头、提起与放下、拖动、缩放，以及可选走动和沿屏幕边缘爬行。
- **聊天与消息**：OpenAI 兼容接口、流式回复、停止生成、聊天历史、摘要气泡和全文查看。
- **提醒**：一次性与重复提醒、稍后提醒、番茄钟，以及本地持久化。
- **任务反馈**：接收外部程序发送的任务状态、进度、详情和通知；状态由明确事件驱动。
- **插件**：声明式 JSON 插件，支持启停、手动运行及重载，内置喝水和伸懒腰提醒。
- **视觉输入**：当前显示器截图、摄像头预览与拍摄，确认后附图提问；需要支持图片输入的模型。
- **输入反馈**：可选键鼠与 XInput 手柄动作反馈。

完整操作说明见 [使用说明](使用说明.md)，功能与验收边界见 [桌宠功能清单](桌宠功能清单.md)。

## 快速开始

桌宠使用 Windows x64、.NET Framework 4.8 和 WebView2 Runtime。源码构建脚本会在首次构建时下载并缓存 Roslyn 编译器，需要网络连接。桌宠本体运行不需要 Python；知识库后端另行启动。

```powershell
git clone https://github.com/elaysia-feng/Butang-chan.git
cd Butang-chan
```

双击根目录的 `启动桌宠.bat`。如果尚未生成 `app/ButangPet.exe`，启动脚本会先构建；已有程序时直接启动。

修改源码后，手动重新构建：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1
```

然后运行 `启动桌宠.bat`。构建产物和本机数据位于 `app/`，不提交到仓库。

### 基本操作

- 拖动角色调整位置，滚轮调整大小，右键打开菜单。
- `F10` 隐藏或显示桌宠。
- 点击脚下消息入口开始聊天，在设置中填写接口地址、模型和 API Key。
- 自动走动、爬行和输入反馈默认关闭，按需开启。
- 截图或摄像头画面先预览，再确认发送；当前不包含语音功能。

## Python 后端

`Python-backend/` 按 code-dev 的 `src/app/` 规范组织，目前是可启动的接口骨架，业务由开发者实现。

| 领域 | 已预留的接口 |
| --- | --- |
| 文件 | 上传文件、下载生成结果 |
| 知识库 | 创建、列表、文件入库 |
| 问答 | 指定知识库范围的问答与来源引用 |
| 文档 | 独立解析、去水印并生成新文档 |
| 任务 | 状态、进度、失败原因与结果查询 |

**这些业务尚未实现，当前返回 HTTP 501。** 健康检查只表示服务可以响应。文件拖入桌宠后的操作菜单与 C# 客户端接入也尚未实现。

使用 Python 3.11 或更高版本，在仓库根目录执行：

```powershell
uv venv Python-backend/.venv --python 3.11
uv pip install --python Python-backend/.venv/Scripts/python.exe -e "./Python-backend[dev]"
& ./Python-backend/.venv/Scripts/butang-backend.exe
```

也可使用标准 Python 的 venv 和 pip。默认监听 `127.0.0.1:8765`：

- 接口文档：`http://127.0.0.1:8765/docs`
- 健康检查：`http://127.0.0.1:8765/health`

详细说明见 [后端 README](Python-backend/README.md)、[桌宠接入契约](Python-backend/docs/desktop-integration.md)和[骨架验证记录](Python-backend/docs/verification.md)。

## 外部任务接入

桌宠提供当前 Windows 用户限定的命名管道 `\\.\pipe\ButangPet.Events`，接收换行分隔的 UTF-8 JSON，支持 `task`、`notify`、`action` 和 `reminder` 事件。

桌宠运行时，可在仓库根目录发送事件：

```powershell
pwsh -File tools/send-pet-event.ps1 -Id document-index -Title '文档索引' -Status running -Progress 35 -Detail '正在处理文档'
pwsh -File tools/send-pet-event.ps1 -Id document-index -Title '文档索引' -Status success -Progress 100 -Detail '索引完成'
```

完整协议及 Python 示例见 [使用说明](使用说明.md#接入自己的任务或知识库程序)。

## 项目结构

```text
Butang-chan/
├── src/                  # C# 桌宠、窗口、聊天与事件接入
├── web/panel/            # WebView2 工具与设置面板
├── assets/               # 角色、动作、图集及制作来源记录
├── plugins/              # 内置声明式插件
├── vendor/               # WebView2 构建依赖
├── tools/                # 构建、事件发送及动画工具
├── Python-backend/       # 知识库与文档处理后端骨架
├── app/                  # 本地构建产物及运行数据，不提交
├── 启动桌宠.bat
└── 使用说明.md
```

## 后续整合方向

计划评估将以下项目的能力接入桌宠，目前尚未合并：

- [Snap-Lite](https://github.com/elaysia-feng/Snap-Lite)：区域截图、标注、贴图和 OCR，衔接截图提问及知识库入库。
- [AI-Task-Hub](https://github.com/elaysia-feng/AI-Task-Hub)：多平台 AI 任务事件、时间线与通知，通过桌宠统一展示。

后续文件处理流程为：拖入文件 → 选择解析、入库或文档处理 → 查看任务进度 → 获取文本或新文档。

## 本机数据

设置、密钥和任务记录保存在本机 `app/` 目录；配置文件、虚拟环境、数据库、模型和日志由 `.gitignore` 排除。不要把个人密钥或聊天记录放入源码及提交文件。

角色和动作素材的制作来源记录位于 `assets/` 中的 provenance 文件。项目产品约定见 [PRODUCT.md](PRODUCT.md)，界面设计说明见 [DESIGN.md](DESIGN.md)。
