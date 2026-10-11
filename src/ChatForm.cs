using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ButangPet
{
    /// <summary>定制网页浮层；密钥、聊天和持久化仍由本机宿主负责。</summary>
    internal class ChatForm : Form
    {
        private readonly Config _cfg;
        private readonly MemoryStore _memory;
        private readonly LlmClient _llm;
        private readonly PetForm _pet;
        private readonly WebView2 _web = new WebView2();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private CancellationTokenSource _requestCancel;
        private string _imageData;
        private readonly List<Dictionary<string, object>> _messages = new List<Dictionary<string, object>>();
        private bool _ready, _initializing, _busy, _shutdown;
        private string _view = "talk";
        private int _nextId;
        private int? _detail;
        private bool _focus;
        private bool _expanded, _autoStart;

        public ChatForm(Config cfg, MemoryStore memory, LlmClient llm, PetForm pet)
        {
            _cfg = cfg; _memory = memory; _llm = llm; _pet = pet;
            _json.MaxJsonLength = 8 * 1024 * 1024;
            _autoStart = Native.GetAutoStart(out string startupCommand);
            Text = "不糖酱 · 陪伴"; FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; TopMost = cfg.topMost;
            BackColor = Color.FromArgb(255, 250, 251);
            ClientSize = new Size((int)(312 * pet.DisplayScale), (int)(248 * pet.DisplayScale));
            _web.Dock = DockStyle.Fill; _web.DefaultBackgroundColor = BackColor;
            Controls.Add(_web); Shown += async (s, e) => await InitializePage();
            foreach (ChatMessage message in memory.Tail(100))
                _messages.Add(NewMessage(message.role == "user" ? "你" : "不糖酱", message.content, message.ts));
        }

        /// <summary>在不透明浮层中加载网页，角色窗口不参与浏览器合成。</summary>
        private async Task InitializePage()
        {
            if (_initializing) return;
            _initializing = true;
            try
            {
                // 1. 单独浏览器目录，不读取旧舞台缓存或把密钥交给页面。
                string data = Path.Combine(_pet.AppRoot, "data", "panel-webview2");
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, data);
                await _web.EnsureCoreWebView2Async(env);
                CoreWebView2 core = _web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false; core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                string root = Path.Combine(_pet.AppRoot, "web", "panel");
                core.SetVirtualHostNameToFolderMapping("companion.local", root, CoreWebView2HostResourceAccessKind.DenyCors);
                // 2. 本地资源禁缓存，重启即加载本次打包的界面与图片。
                core.AddWebResourceRequestedFilter("https://companion.local/*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (s, e) => {
                    Uri uri = new Uri(e.Request.Uri);
                    string path = Path.GetFullPath(Path.Combine(root, Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
                    if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return;
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    string type = ext == ".html" ? "text/html; charset=utf-8" : ext == ".css" ? "text/css; charset=utf-8" : ext == ".js" ? "application/javascript; charset=utf-8" : ext == ".png" ? "image/png" : "image/webp";
                    e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(File.ReadAllBytes(path)), 200, "OK", "Content-Type: " + type + "\r\nCache-Control: no-store");
                };
                core.WebMessageReceived += OnPageMessage;
                core.PermissionRequested += (s, e) => {
                    if (e.PermissionKind == CoreWebView2PermissionKind.Camera)
                        e.State = Visible && _view == "vision" && e.Uri.StartsWith("https://companion.local/", StringComparison.Ordinal)
                            ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                };
                core.NewWindowRequested += (s, e) => e.Handled = true;
                core.Navigate("https://companion.local/index.html?v=" + DateTime.UtcNow.Ticks);
            }
            catch (Exception ex)
            {
                Trace.Log("定制浮层初始化失败: " + ex);
                _pet.Say("消息界面加载失败，请检查 WebView2 Runtime。", 15);
                Hide();
            }
            finally { _initializing = false; }
        }

        private Dictionary<string, object> NewMessage(string author, string text, long? at = null)
        {
            return new Dictionary<string, object> { { "id", ++_nextId }, { "author", author }, { "text", text ?? "" },
                { "at", at ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } };
        }

        public void AddMessage(string author, string text)
        {
            _messages.Add(NewMessage(author, text));
            if (_messages.Count > 100) _messages.RemoveAt(0);
            PushState();
        }
        internal void AddNotification(string text)
        {
            if (!_messages.Any(item => Convert.ToString(item["text"]) == text && Convert.ToString(item["author"]) != "你")) AddMessage("不糖酱", text);
        }

        public void ShowText(string text)
        {
            Dictionary<string, object> message = _messages.LastOrDefault(item => Convert.ToString(item["text"]) == text);
            if (message == null) { AddMessage("不糖酱", text); message = _messages.Last(); }
            _detail = Convert.ToInt32(message["id"]); _view = "talk"; PushState();
        }
        public void ShowHistory() { _detail = null; _view = "talk"; PushState(); }
        public void FocusInput() { _detail = null; _view = "talk"; _focus = true; PushState(); }
        public void ShowSettings() { _view = "settings"; PushState(); }
        public void ShowAppearance() { _view = "appearance"; PushState(); }
        public void ShowTools() { _view = "tools"; PushState(); }
        public void ShowVision() { _view = "vision"; PushState(); }
        internal void RefreshState() { PushState(); }
        internal WebView2 Browser { get { return _web; } }
        internal bool PageReady { get { return _ready; } }

        private void PushState()
        {
            if (!_ready || IsDisposed || _shutdown) return;
            Dictionary<string, object> config = _cfg.ToMapForPage(); config["systemPrompt"] = _cfg.systemPrompt;
            config["autoStart"] = _autoStart;
            bool expanded = _expanded || _view != "talk" || _detail.HasValue;
            Size desired = new Size((int)((expanded ? 340 : 312) * _pet.DisplayScale),
                (int)((expanded ? (_view == "talk" && !_detail.HasValue ? 350 : 420) : 248) * _pet.DisplayScale));
            if (ClientSize != desired) { ClientSize = desired; _pet.PositionChat(); }
            var state = new Dictionary<string, object> { { "t", "state" }, { "messages", _messages }, { "busy", _busy },
                { "view", _view }, { "detail", _detail }, { "focus", _focus }, { "config", config },
                { "features", _pet.Features.Snapshot() }, { "hasImage", _imageData != null }, { "draft", _pet.DraftText }, { "expanded", expanded } };
            _web.CoreWebView2.PostWebMessageAsJson(_json.Serialize(state)); _focus = false;
        }

        /// <summary>只接受本地面板的有限指令，所有业务仍在宿主中执行。</summary>
        private async void OnPageMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                if (!e.Source.StartsWith("https://companion.local/", StringComparison.Ordinal)) return;
                var message = _json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson);
                if (message == null || !message.TryGetValue("t", out object type)) return;
                switch (Convert.ToString(type))
                {
                    case "ready": _ready = true; PushState(); Trace.Log("定制网页浮层已就绪"); break;
                    case "hide": Hide(); break;
                    case "expand": _expanded = Convert.ToBoolean(message["value"]); _view = "talk"; _detail = null; PushState(); break;
                    case "view": _view = Convert.ToString(message["view"]); PushState(); break;
                    case "detail": _detail = message["id"] == null ? (int?)null : Convert.ToInt32(message["id"]); PushState(); break;
                    case "chat": await SendMessage(Convert.ToString(message["text"])); break;
                    case "draft": _pet.DraftText = Convert.ToString(message["text"]); break;
                    case "interrupt": InterruptReply(); break;
                    case "screen": await CaptureScreen(); break;
                    case "vision-attach": AttachImage(Convert.ToString(message["data"])); break;
                    case "vision-clear": _imageData = null; PushState(); break;
                    case "reminder-add":
                        _pet.Features.AddReminder(Convert.ToString(message["text"]), DateTime.Parse(Convert.ToString(message["due"]), null, System.Globalization.DateTimeStyles.RoundtripKind), Convert.ToInt32(message["repeat"]));
                        SendNotice("saved-feature", "提醒已记住"); break;
                    case "reminder-change": _pet.Features.ChangeReminder(Convert.ToString(message["id"]), Convert.ToString(message["action"])); break;
                    case "task-delete": _pet.Features.DeleteTask(Convert.ToString(message["id"])); break;
                    case "plugin-toggle": _pet.Features.TogglePlugin(Convert.ToString(message["id"]), Convert.ToBoolean(message["enabled"])); break;
                    case "plugin-run": _pet.Features.RunPlugin(Convert.ToString(message["id"])); break;
                    case "plugin-reload": _pet.Features.LoadPlugins(); SendNotice("saved-feature", "插件已重新加载"); break;
                    case "pomodoro": _pet.StartPomodoro(Math.Max(0, Math.Min(180, Convert.ToInt32(message["minutes"])))); SendNotice("saved-feature", "专注计时已更新"); break;
                    case "auto-start":
                        bool enabled = Convert.ToBoolean(message["enabled"]);
                        if (!Native.SetAutoStart(enabled, Application.ExecutablePath)) throw new InvalidOperationException("开机启动设置失败，请检查当前用户权限");
                        _autoStart = enabled; PushState(); SendNotice("saved-feature", enabled ? "已开启开机启动" : "已关闭开机启动"); break;
                    case "climb": _cfg.climb = true; _cfg.Save(); PushState(); Hide(); _pet.StartClimbing(); break;
                    case "size": _pet.SetScale(Convert.ToDouble(message["value"]), Convert.ToBoolean(message["save"])); break;
                    case "action": _pet.SetExpression(Convert.ToInt32(message["index"])); Hide(); break;
                    case "config":
                        var map = message["map"] as Dictionary<string, object>;
                        if (map == null) return;
                        if (map.TryGetValue("baseUrl", out object url) && (!Uri.TryCreate(Convert.ToString(url), UriKind.Absolute, out Uri parsed) || (parsed.Scheme != "https" && parsed.Scheme != "http")))
                        { SendNotice("error", "请输入有效的接口地址"); return; }
                        _cfg.UpdateFromPage(map);
                        if (message.TryGetValue("key", out object key) && !string.IsNullOrWhiteSpace(Convert.ToString(key))) _cfg.apiKey = Convert.ToString(key).Trim();
                        _cfg.Save(); _llm.Update(_cfg.baseUrl, _cfg.apiKey, _cfg.model); _pet.ApplyAppearance();
                        PushState(); SendNotice("saved", "设置已保存"); break;
                    case "clear": if (!_busy) { _memory.Clear(); _messages.Clear(); _detail = null; PushState(); } break;
                }
            }
            catch (Exception ex) { Trace.Log("浮层指令失败: " + ex.Message); SendNotice("error", "操作失败，请重试"); }
        }

        private void SendNotice(string type, string text)
        {
            if (_ready && !_shutdown) _web.CoreWebView2.PostWebMessageAsJson(_json.Serialize(new { t = type, text }));
        }

        /// <summary>串行聊天；流式摘要同时展示在角色头顶与定制消息卡片中。</summary>
        private async Task SendMessage(string text)
        {
            text = (text ?? "").Trim();
            if (_busy || _shutdown || text.Length == 0) return;
            LlmClient client = _imageData != null && !string.IsNullOrWhiteSpace(_cfg.visionModel) ? new LlmClient(_cfg.baseUrl, _cfg.apiKey, _cfg.visionModel) : _llm;
            if (!client.Ready) { AddMessage("提示", "还没有配置 AI。打开设置填写接口地址、模型和 API Key，就可以自由聊天了。"); PushState(); return; }
            // 1. 在写入本轮前采集历史，防止把用户消息重复传给模型。
            _busy = true; _detail = null; _view = "talk"; _pet.DraftText = "";
            string image = _imageData; _imageData = null;
            _requestCancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            CancellationTokenSource requestCancel = _requestCancel;
            List<ChatMessage> history = _memory.ContextFor(_cfg.recentTurns * 2);
            string prompt = _cfg.systemPrompt + (string.IsNullOrEmpty(_memory.SummaryText) ? "" : "\n长期记忆：" + _memory.SummaryText);
            _memory.Append("user", text); AddMessage("你", text);
            Dictionary<string, object> reply = NewMessage("不糖酱", "正在回复…"); _messages.Add(reply); PushState();
            string bubbleKey = "reply-" + reply["id"];
            _pet.SetTalking(true); _pet.Say("正在回复…", 60, bubbleKey); Hide();
            StringBuilder answer = new StringBuilder();
            try
            {
                // 2. 后台回调只投递 UI 线程，隐藏面板不取消当前回复。
                LlmClient.Result result = await client.ChatAsync(prompt, history, text, delta => {
                    answer.Append(delta); string current = answer.ToString();
                    if (!_shutdown && !IsDisposed)
                        try { BeginInvoke(new Action(() => { if (!_shutdown && !requestCancel.IsCancellationRequested) { reply["text"] = current; PushState(); _pet.Say(current, 20, bubbleKey); } })); }
                        catch (InvalidOperationException) { /* 退出后不再投递。 */ }
                }, r => { }, requestCancel.Token, image);
                if (_shutdown) return;
                if (requestCancel.IsCancellationRequested) { reply["text"] = answer.Length > 0 ? answer.ToString() + "\n（已打断）" : "（已打断）"; }
                else if (result.Ok) { _memory.Append("assistant", result.Text); reply["text"] = result.Text; _pet.SetExpression(7); }
                else { reply["author"] = "提示"; reply["text"] = "回复失败：" + result.Error + (image != null ? "\n请确认识图模型支持图片输入。" : ""); }
                _pet.Say(Convert.ToString(reply["text"]), 20, bubbleKey); PushState();
                // 3. 长期摘要继续沿用现有存储，整理期间避免另一轮覆盖上下文。
                if (result.Ok && !requestCancel.IsCancellationRequested && _memory.ShouldSummarize(_cfg.summarizeAt, _cfg.recentTurns * 2))
                {
                    var slice = _memory.PendingForSummary(_cfg.recentTurns * 2);
                    int covered = _memory.Covered + slice.Count;
                    var summary = await _llm.SummarizeAsync("用不超过300字保留用户偏好、称呼、约定和进行中的事情。只输出摘要。已有摘要：" + _memory.SummaryText, slice, requestCancel.Token);
                    if (summary.Ok && !_shutdown) _memory.SaveSummary(summary.Text, covered);
                }
            }
            catch (Exception ex)
            { if (!_shutdown) { reply["text"] = "回复失败：" + ex.Message; _pet.Say(Convert.ToString(reply["text"]), 15, bubbleKey); } }
            finally { _requestCancel = null; requestCancel.Dispose(); _busy = false; if (!_shutdown) { _pet.SetTalking(false); PushState(); } }
        }

        private void InterruptReply() { _requestCancel?.Cancel(); }
        internal bool CanRelease { get { return !_busy && !_initializing && _imageData == null; } }

        /// <summary>图像只在用户选定后暂存，发送一轮后清除，不写入聊天记录。</summary>
        private void AttachImage(string data)
        {
            if (data == null || !data.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) || data.Length > 3 * 1024 * 1024)
                throw new ArgumentException("请选择不超过 3 MB 的 JPEG 画面");
            byte[] bytes = Convert.FromBase64String(data.Substring(data.IndexOf(',') + 1));
            using (var stream = new MemoryStream(bytes))
            using (Image image = Image.FromStream(stream))
                if (image.Width > 4096 || image.Height > 4096) throw new ArgumentException("画面尺寸过大");
            _imageData = data;
            if (_ready) _web.CoreWebView2.PostWebMessageAsJson(_json.Serialize(new { t = "vision-image", data }));
            PushState();
        }

        private async Task CaptureScreen()
        {
            bool petVisible = _pet.Visible, panelVisible = Visible;
            Rectangle screen = Screen.FromControl(_pet).Bounds;
            Hide(); _pet.Hide();
            try
            {
                await Task.Delay(180);
                using (Bitmap full = new Bitmap(screen.Width, screen.Height))
                using (Graphics graphics = Graphics.FromImage(full))
                {
                    graphics.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
                    int width = Math.Min(1280, screen.Width), height = (int)((double)width * screen.Height / screen.Width);
                    using (Bitmap preview = new Bitmap(width, height))
                    using (Graphics g = Graphics.FromImage(preview))
                    using (MemoryStream stream = new MemoryStream())
                    {
                        g.DrawImage(full, 0, 0, width, height); preview.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
                        AttachImage("data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray()));
                    }
                }
            }
            finally { if (!_shutdown) { if (petVisible) _pet.ShowPet(); if (panelVisible) { Show(); Activate(); } } }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width < 30 || Height < 30) return;
            using (GraphicsPath path = new GraphicsPath())
            {
                int d = 40;
                path.AddArc(0, 0, d, d, 180, 90); path.AddArc(Width-d, 0, d, d, 270, 90);
                path.AddArc(Width-d, Height-d, d, d, 0, 90); path.AddArc(0, Height-d, d, d, 90, 90); path.CloseFigure();
                Region previous = Region; Region = new Region(path); previous?.Dispose();
            }
        }
        public void Shutdown() { if (_shutdown) return; _shutdown = true; _lifetime.Cancel(); }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_shutdown && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Shutdown(); _requestCancel?.Dispose(); _lifetime.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
