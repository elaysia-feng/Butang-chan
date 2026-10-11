using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ButangPet
{
    /// <summary>原生逐像素透明桌宠：帧动画、拖动、托盘和生活提醒。</summary>
    internal class PetForm : Form
    {
        private readonly string _appRoot;
        private readonly Config _cfg;
        private readonly MemoryStore _memory;
        private readonly LlmClient _llm;
        private readonly MotionPlayer _motion;
        private readonly Native.LayeredSurface _surface = new Native.LayeredSurface();
        private readonly BubbleForm _bubbleWindow = new BubbleForm();
        private int _paintStamp = int.MinValue;
        private DateTime _panelHiddenSince;
        internal int PaintCount { get; private set; }
        internal BubbleForm MessageWindow { get { return _bubbleWindow; } }
        internal string DraftText { get; set; } = "";
        internal CompanionFeatures Features { get; private set; }
        private bool _climbing;
        private Point _climbStart;
        private DateTime _climbStarted;
        private bool _approachingWall, _wallLeft, _climbTravelStarted;
        private int _wallX;
        private DateTime _lastMoveTick = DateTime.UtcNow;
        private readonly Timer _animation = new Timer { Interval = 16 };
        private readonly Timer _clock = new Timer { Interval = 1000 };
        private readonly Random _random = new Random();
        private PetMenuForm _menu;
        private NotifyIcon _tray;
        private ChatForm _chat;
        internal string AppRoot { get { return _appRoot; } }
        internal float DisplayScale { get { return _dpiScale; } }
        private DateTime _lastFrame = DateTime.UtcNow;
        private DateTime _nextAction = DateTime.UtcNow.AddSeconds(12);
        private DateTime _hoverStarted, _lastHoverReaction;
        private DateTime _bubbleEnd, _pomodoroEnd;
        private DateTime _lastActivity = DateTime.Now;
        private int _lastHour = -1, _expression;
        private bool _talking, _dragging, _moved, _renderFailed;
        private bool _wandering;
        private bool _manualWalk;
        private Point _walkStart, _walkTarget;
        private DateTime _walkStarted;
        private Point _dragCursor, _dragPosition, _lastCursor;
        private string _bubble = "";
        private Rectangle _composeBounds, _historyBounds;
        private sealed class Bubble
        {
            public string Text, Key;
            public DateTime End;
            public Rectangle Bounds;
        }
        private readonly List<Bubble> _bubbles = new List<Bubble>();
        private RectangleF _headBounds;
        private Point _strokeCursor;
        private DateTime _strokeStarted, _lastStroke, _heartsEnd;
        private double _strokeDistance, _holdAngle, _holdVelocity, _holdTarget;
        private DateTime _lastDragMove;
        private DateTime _nextInputPoll, _lastInputReaction;
        private uint _lastInputTick;
        private ushort _lastGamepadButtons;
        private bool _lastMousePressed;
        private Point _inputCursor;
        private int _lastStickDirection;
        private readonly ToolTip _toolbarTip = new ToolTip();
        private float _dpiScale = 1;
        private const int HotKeyId = 0xBEEF + 1;

        public PetForm(string appRoot, string projectRoot, Config cfg, MemoryStore memory, LlmClient llm)
        {
            _appRoot = appRoot; _cfg = cfg; _memory = memory; _llm = llm;
            Text = "不糖酱"; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            TopMost = cfg.topMost;
            _motion = new MotionPlayer(Path.Combine(appRoot, "assets", "motions"));
            _bubbleWindow.MessageClicked += text => { ShowChat(); _chat.ShowText(text); };
            _bubbleWindow.Dismissed += () => { _bubbles.Clear(); UpdateBubbles(); };
            _bubbleWindow.ExpandedRequested += () => {
                ShowChat(); foreach (Bubble item in _bubbles) _chat.AddNotification(item.Text); _chat.ShowHistory();
            };
            VisibleChanged += (s, e) => { if (!Visible) _menu?.Hide(); UpdateBubbles(); };
            Features = new CompanionFeatures(appRoot, action => {
                if (!IsDisposed && IsHandleCreated) try { BeginInvoke(action); } catch (InvalidOperationException) { }
            }, Path.GetDirectoryName(cfg.ConfigPath));
            Features.Notify += (text, motion) => {
                CancelMovement(false);
                if (!string.IsNullOrEmpty(text)) Say(text, 20); _motion.React(motion);
            };
            Features.Changed += () => { if (_chat != null && !_chat.IsDisposed) _chat.RefreshState(); };
            _animation.Tick += (s, e) => {
                DateTime tick = DateTime.UtcNow;
                double stepSeconds = Math.Min(.05, (tick - _lastMoveTick).TotalSeconds); _lastMoveTick = tick;
                if (_approachingWall)
                {
                    if (!_cfg.climb || _dragging || _talking || (_chat != null && _chat.Visible)) CancelMovement();
                    else if (_motion.InWalkingPose)
                    {
                        int step = Math.Max(1, (int)(90 * _dpiScale * stepSeconds));
                        int distance = _wallX - Left;
                        Location = new Point(Left + Math.Sign(distance) * Math.Min(step, Math.Abs(distance)), Top);
                        if (Left == _wallX)
                        {
                            _approachingWall = false; _climbing = true; _climbTravelStarted = false;
                            _motion.SetClimbing(true, _wallLeft);
                        }
                    }
                }
                if (_climbing)
                {
                    bool cancel = !_cfg.climb || _dragging || _talking || (_chat != null && _chat.Visible);
                    Rectangle work = Screen.FromControl(this).WorkingArea;
                    if (!_climbTravelStarted && _motion.InClimbingPose) { _climbStarted = tick; _climbStart = Location; _climbTravelStarted = true; }
                    double progress = _climbTravelStarted ? Math.Min(1, (tick - _climbStarted).TotalSeconds / 4) : 0;
                    if (!cancel && _climbTravelStarted) Location = new Point(_climbStart.X, Math.Max(work.Top, _climbStart.Y - (int)(progress * 70 * _dpiScale)));
                    if (cancel || progress >= 1) { _climbing = false; _motion.SetClimbing(false); RememberPosition(); }
                }
                if (_wandering)
                {
                    bool cancel = (!_manualWalk && !_cfg.wander) || (_chat != null && _chat.Visible);
                    if (_walkStarted == DateTime.MinValue && _motion.InWalkingPose) _walkStarted = tick;
                    double progress = _walkStarted == DateTime.MinValue ? 0 : Math.Min(1, (tick - _walkStarted).TotalMilliseconds / _motion.WalkDuration);
                    if (!cancel)
                        Location = new Point(_walkStart.X + (int)Math.Round((_walkTarget.X - _walkStart.X) * progress), _walkStart.Y);
                    if (cancel || progress >= 1) { _wandering = false; _manualWalk = false; _motion.SetWalking(false); RememberPosition(); }
                }
                // 透明像素不会持续收到鼠标移动，拖动时直接跟踪全局光标。
                if (_dragging)
                {
                    if ((Native.GetAsyncKeyState(1) & 0x8000) != 0) Drag(this, null);
                    else
                    {
                        // 点击结束后光标可能已移走，仍按最初按下的位置识别工具条。
                        Point point = PointToClient(_moved ? Cursor.Position : _dragCursor);
                        EndDrag(this, new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
                    }
                }
                if (Visible) RenderPet();
            };
            _clock.Tick += (s, e) => TickClock();
            MouseDown += BeginDrag; MouseMove += Drag; MouseUp += EndDrag;
            MouseLeave += (s, e) => _hoverStarted = DateTime.MinValue;
            MouseWheel += (s, e) => ChangeScale(e.Delta > 0 ? 1.1 : 1 / 1.1);
            Shown += (s, e) => InitializePet();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                if (_cfg != null && _cfg.clickThrough) cp.ExStyle |= Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        /// <summary>按当前显示器恢复位置，启动托盘、热键和动画。</summary>
        private void InitializePet()
        {
            // 1. 恢复尺寸与位置，离线显示器的旧位置回到主屏。
            using (Graphics g = CreateGraphics()) _dpiScale = g.DpiX / 96f;
            ResizePet();
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            int x = 0, y = 0;
            bool saved = int.TryParse(_cfg.lastX, out x) && int.TryParse(_cfg.lastY, out y);
            if (!saved) { x = work.Right - Width - 24; y = work.Bottom - Height - 10; }
            Location = new Point(x, y);
            ClampPosition();
            // 2. 注册原生交互，不加载浏览器或旧模型。
            BuildMenu();
            string iconPath = Path.Combine(_appRoot, "assets", "tray.ico");
            _tray = new NotifyIcon { Text = "不糖酱 · 原生桌宠", Visible = true,
                Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application };
            _tray.DoubleClick += (s, e) => { ShowPet(); ShowChat(); };
            _tray.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) _menu.ShowPopup(Cursor.Position, _dpiScale); };
            Native.RegisterHotKey(Handle, HotKeyId, 0, (uint)Keys.F10);
            Features.Start();
            SetExpression(6); Say("你好呀，今天也陪着你。", 6);
            _animation.Start(); _clock.Start();
            Trace.Log("原生桌宠已就绪，逐像素 Alpha 合成，DPI=" + _dpiScale + " 窗口=" + Size);
        }

        private void ResizePet()
        {
            float scale = (float)_cfg.scale * _dpiScale;
            // 角色可以缩小，气泡保持可读字号；多余透明区域不拦截桌面点击。
            Size = new Size((int)Math.Max(220 * _dpiScale, 420 * scale),
                (int)(133 * _dpiScale + 475 * scale));
            _motion.SetRenderHeight((int)Math.Round(475 * scale));
            _paintStamp = int.MinValue;
        }

        /// <summary>选择表情帧并绘制呼吸、点击反馈和消息气泡。</summary>
        private void RenderPet()
        {
            if (_renderFailed || IsDisposed) return;
            DateTime now = DateTime.UtcNow;
            double elapsed = (now - _lastFrame).TotalMilliseconds;
            _lastFrame = now;
            _motion.Update(elapsed);
            UpdateBubbles();
            if (now >= _nextInputPoll)
            {
                _nextInputPoll = now.AddMilliseconds(100);
                uint tick = Native.LastInputTick();
                if (tick != _lastInputTick) _lastActivity = DateTime.Now;
                ushort buttons = 0; float horizontal = 0;
                if (_cfg.inputReactions) Native.ReadGamepad(out buttons, out horizontal);
                ushort pressed = (ushort)(buttons & ~_lastGamepadButtons);
                int direction = horizontal < -.3 ? -1 : horizontal > .3 ? 1 : 0;
                bool stickMoved = direction != 0 && direction != _lastStickDirection;
                Point inputCursor = Cursor.Position;
                bool mouseDown = (Native.GetAsyncKeyState(1) & 0x8000) != 0 || (Native.GetAsyncKeyState(2) & 0x8000) != 0;
                bool clicked = mouseDown && !_lastMousePressed && !Bounds.Contains(inputCursor) && (_chat == null || !_chat.Visible);
                bool keyboard = tick != _lastInputTick && inputCursor == _inputCursor && !mouseDown;
                bool meaningful = pressed != 0 || stickMoved || clicked || keyboard;
                double cooldown = keyboard && !clicked && pressed == 0 && !stickMoved ? 1.2 : 2.5;
                if (_cfg.inputReactions && meaningful && !_dragging && !_talking && !_wandering && !_climbing && !_approachingWall &&
                    _motion.CurrentName == "idle" && (now - _lastInputReaction).TotalSeconds >= cooldown)
                {
                    _lastInputReaction = now;
                    if (stickMoved || (pressed & 12) != 0) StartSmallWalk(direction < 0 || (pressed & 4) != 0);
                    else if ((pressed & 4096) != 0) _motion.React("cheer");
                    else if ((pressed & 8192) != 0) _motion.React("shy");
                    else if ((pressed & 32768) != 0) _motion.React("sleepy");
                    else _motion.Play(clicked || pressed != 0 ? "wave" : "blink");
                }
                _lastInputTick = tick; _lastGamepadButtons = buttons;
                _lastStickDirection = direction; _inputCursor = inputCursor; _lastMousePressed = mouseDown;
            }
            // 拿起时只做小幅惯性反馈，放下回到原角度；位置始终由用户决定。
            if ((now - _lastDragMove).TotalMilliseconds > 80) _holdTarget = 0;
            double dt = Math.Min(.04, Math.Max(0, elapsed / 1000));
            _holdVelocity += ((_holdTarget - _holdAngle) * 100 - _holdVelocity * 20) * dt;
            _holdAngle += _holdVelocity * dt;
            if (!_dragging && !_talking && _motion.CurrentName == "idle" &&
                _hoverStarted != DateTime.MinValue && (now - _hoverStarted).TotalMilliseconds > 1200 &&
                (now - _lastHoverReaction).TotalSeconds > 30)
            {
                _lastHoverReaction = now; _hoverStarted = DateTime.MinValue; SetExpression(6);
            }
            if (!_dragging && !_talking && now >= _nextAction && _motion.CurrentName == "idle" && _motion.FrameIndex == 0 && (_chat == null || !_chat.Visible))
            {
                int[] choices = { 6, 8, 9 };
                SetExpression(choices[_random.Next(choices.Length)]);
            }
            // 1. 渲染完整动作播放器的当前帧，不再用单张姿态图叠加旋转模拟动作。
            int stamp = _motion.Revision ^ Location.GetHashCode() ^ Size.GetHashCode() ^ (int)(_holdAngle * 20) ^ (int)(_cfg.opacity * 255);
            var activeTask = Features.Data.Tasks.FindLast(t => t.Status == "running" || (t.Status == "success" || t.Status == "error") && (now - t.Updated).TotalSeconds < 15);
            if (activeTask != null) stamp ^= activeTask.Progress * 397 ^ activeTask.Status.GetHashCode();
            if (now < _heartsEnd || _talking) stamp ^= (int)(now.Ticks / TimeSpan.TicksPerMillisecond / 33);
            if (_pomodoroEnd > DateTime.Now) stamp ^= (int)(DateTime.Now.Ticks / TimeSpan.TicksPerSecond);
            if (stamp == _paintStamp) return;
            _paintStamp = stamp;
            Bitmap frame = _motion.Frame;
            // 2. 使用 PArgb 画布保留半透明蕾丝和抗锯齿边缘。
            _surface.Ensure(Width, Height);
            Bitmap canvas = _surface.Canvas;
            {
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    float unit = _dpiScale;
                    float h = 475 * (float)_cfg.scale * unit;
                    float w = h * frame.Width / frame.Height;
                    GraphicsState artState = g.Save();
                    float centerX = Width / 2f, centerY = 80 * unit + h * .3f;
                    g.TranslateTransform(centerX, centerY); g.RotateTransform((float)_holdAngle);
                    g.TranslateTransform(-centerX, -centerY);
                    if (_motion.FacingLeft) { g.TranslateTransform(Width, 0); g.ScaleTransform(-1, 1); }
                    if (frame.Height == (int)Math.Round(h))
                        g.DrawImageUnscaled(frame, (int)Math.Round((Width - frame.Width) / 2f), (int)Math.Round(80 * unit));
                    else
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(frame, (Width - w) / 2, 80 * unit, w, h);
                    }
                    g.Restore(artState);
                    _headBounds = new RectangleF(centerX - w * .22f, 80 * unit + h * .05f, w * .44f, h * .3f);
                    if (now < _heartsEnd) DrawHearts(g, now, unit);
                    DrawToolbar(g, unit);
                    DrawTaskStatus(g, unit);
                    if (_talking)
                        using (Brush ink = new SolidBrush(Color.FromArgb(112, 82, 121)))
                            for (int i = 0; i < 3; i++) g.FillEllipse(ink, Width / 2f - 10 * unit + i * 8 * unit,
                                Height - 55 * unit - (float)(Math.Sin(now.TimeOfDay.TotalSeconds * 5 + i) * 2) * unit, 3 * unit, 3 * unit);
                    if (_pomodoroEnd > DateTime.Now)
                    {
                        string timer = (_pomodoroEnd - DateTime.Now).ToString(@"mm\:ss");
                        using (Font font = new Font("Microsoft YaHei UI", 10 * unit, FontStyle.Bold))
                        using (Brush brush = new SolidBrush(Color.FromArgb(255, 74, 51, 80)))
                            g.DrawString(timer, font, brush, Width - 66 * unit, Height - 28 * unit);
                    }
                }
                // 3. 窗口透明度由合成参数控制，不能设置 Form.Opacity。
                try { _surface.Present(Handle, Location, (byte)(_cfg.opacity * 255)); PaintCount++; }
                catch (Exception ex)
                {
                    _renderFailed = true; _animation.Stop();
                    Trace.Log("原生透明绘制失败: " + ex);
                    MessageBox.Show("透明绘制失败：" + ex.Message, "不糖酱");
                }
            }
        }

        private void UpdateBubbles()
        {
            if (_bubbleWindow.IsDisposed) return;
            _bubbles.RemoveAll(item => item.End <= DateTime.UtcNow);
            foreach (Bubble item in _bubbles) item.Bounds = Rectangle.Empty;
            if (!Visible || _renderFailed || (_chat != null && _chat.Visible) || (_menu != null && _menu.Visible)) { _bubbleWindow.Hide(); return; }
            List<Bubble> active = _bubbles.AsEnumerable().Reverse().Take(3).ToList();
            float h = 475 * (float)_cfg.scale * _dpiScale, w = h * .8f;
            Rectangle anchor = new Rectangle(Left + (int)((Width - w) / 2), Top + (int)(80 * _dpiScale), (int)w, (int)h);
            _bubbleWindow.UpdateMessages(active.Select(item => item.Text).ToList(), anchor, _dpiScale, _cfg.topMost, this);
            for (int i = 0; i < active.Count && i < _bubbleWindow.CardBounds.Length; i++) active[i].Bounds = _bubbleWindow.CardBounds[i];
        }

        /// <summary>摸头反馈在头部缓慢上浮，不改变角色帧或身体比例。</summary>
        private void DrawHearts(Graphics g, DateTime now, float unit)
        {
            double progress = 1 - (_heartsEnd - now).TotalSeconds / 1.6;
            int alpha = (int)(220 * Math.Sin(Math.PI * Math.Max(0, Math.Min(1, progress))));
            using (Font font = new Font("Segoe UI Symbol", 11 * unit))
            using (Brush brush = new SolidBrush(Color.FromArgb(alpha, 223, 111, 160)))
                for (int i = 0; i < 3; i++)
                    g.DrawString("♥", font, brush, _headBounds.X + _headBounds.Width * (i / 2f), _headBounds.Y - (float)(progress * 24 + i * 5) * unit);
        }

        private void StrokeHead()
        {
            if ((DateTime.UtcNow - _lastStroke).TotalSeconds < 5 || _talking) return;
            _lastStroke = DateTime.UtcNow; _heartsEnd = _lastStroke.AddSeconds(1.6);
            _lastHoverReaction = _lastStroke; _hoverStarted = DateTime.MinValue;
            _strokeDistance = 0; _expression = 8; CancelMovement(false); _motion.React("shy");
            if (_cfg.speakOnClick) Say("嗯…再摸一下也可以。", 5);
        }

        /// <summary>绘制脚下工具条：左侧写消息，右侧看消息历史。</summary>
        private void DrawToolbar(Graphics g, float unit)
        {
            float w = 104 * unit, h = 36 * unit, x = (Width - w) / 2, y = Height - 42 * unit;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(x, y, h, h, 90, 180);
                path.AddArc(x + w - h, y, h, h, 270, 180); path.CloseFigure();
                using (Brush fill = new SolidBrush(Color.White)) g.FillPath(fill, path);
            }
            _composeBounds = new Rectangle((int)x, (int)y, (int)(w / 2), (int)h);
            _historyBounds = new Rectangle((int)(x + w / 2), (int)y, (int)(w / 2), (int)h);
            using (Pen divider = new Pen(Color.FromArgb(228, 225, 229), unit))
                g.DrawLine(divider, x + w / 2, y + 10 * unit, x + w / 2, y + h - 10 * unit);
            using (Pen ink = new Pen(Color.FromArgb(58, 49, 62), 1.5f * unit))
            {
                ink.StartCap = LineCap.Round; ink.EndCap = LineCap.Round;
                float cx = x + 17 * unit, cy = y + 11 * unit;
                g.DrawLines(ink, new[] { new PointF(cx + 8 * unit, cy), new PointF(cx, cy),
                    new PointF(cx, cy + 14 * unit), new PointF(cx + 14 * unit, cy + 14 * unit), new PointF(cx + 14 * unit, cy + 7 * unit) });
                g.DrawLine(ink, cx + 5 * unit, cy + 8 * unit, cx + 15 * unit, cy - 2 * unit);
                cx = x + 69 * unit;
                g.DrawRectangle(ink, cx, cy, 17 * unit, 13 * unit);
                g.DrawLine(ink, cx + 3 * unit, cy + 5 * unit, cx + 13 * unit, cy + 5 * unit);
                g.DrawLine(ink, cx + 3 * unit, cy + 9 * unit, cx + 10 * unit, cy + 9 * unit);
                g.DrawLine(ink, cx + 4 * unit, cy + 13 * unit, cx + 4 * unit, cy + 17 * unit);
            }
        }

        public void Say(string text, int seconds = 7, string key = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _bubble = text; _bubbleEnd = DateTime.UtcNow.AddSeconds(seconds);
            Bubble item = key == null ? null : _bubbles.Find(b => b.Key == key);
            if (item == null) { item = new Bubble { Key = key }; _bubbles.Add(item); }
            item.Text = text; item.End = _bubbleEnd;
            while (_bubbles.Count > 8) _bubbles.RemoveAt(0);
            UpdateBubbles();
        }

        /// <summary>执行中的外部任务在脚下工具条持续显示进度，结果短暂保留颜色反馈。</summary>
        private void DrawTaskStatus(Graphics g, float unit)
        {
            CompanionFeatures.PetTask task = Features.Data.Tasks.FindLast(t => t.Status == "running");
            if (task == null) task = Features.Data.Tasks.FindLast(t => (t.Status == "success" || t.Status == "error") && (DateTime.UtcNow - t.Updated).TotalSeconds < 15);
            if (task == null) return;
            Color color = task.Status == "error" ? Color.FromArgb(188, 83, 106) : task.Status == "success" ? Color.FromArgb(80, 139, 122) : Color.FromArgb(150, 112, 160);
            using (Brush brush = new SolidBrush(color))
                g.FillEllipse(brush, _historyBounds.Right - 9 * unit, _historyBounds.Top + 4 * unit, 5 * unit, 5 * unit);
            if (task.Status == "running")
                using (Pen pen = new Pen(color, 2 * unit))
                    g.DrawLine(pen, _composeBounds.Left + 10 * unit, _composeBounds.Bottom - 4 * unit,
                        _composeBounds.Left + (10 + 84 * task.Progress / 100f) * unit, _composeBounds.Bottom - 4 * unit);
        }

        public void SetTalking(bool talking) { _talking = talking; _motion.SetTalking(talking); }
        public void SetExpression(int index)
        {
            _expression = index;
            if (index == 10)
            {
                StartSmallWalk(null); return;
            }
            string[] clips = { "idle", "blink", "cheer", "wave", "shy", "sleepy", "wave", "cheer", "shy", "sleepy", "walk" };
            if (index >= 0 && index < clips.Length) { CancelMovement(false); _motion.React(clips[index]); }
            _nextAction = DateTime.UtcNow.AddSeconds(20 + _random.Next(16));
        }

        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) return;
            if (e.Button != MouseButtons.Left) return;
            if (_wandering || _climbing || _approachingWall) { _wandering = _climbing = _approachingWall = _manualWalk = false; }
            _dragging = true; _moved = false;
            _toolbarTip.Hide(this); _toolbarTip.Active = false;
            _hoverStarted = DateTime.MinValue;
            _dragCursor = Cursor.Position; _dragPosition = Location; Capture = true;
            _strokeCursor = _dragCursor;
        }
        private void Drag(object sender, MouseEventArgs e)
        {
            if (!_dragging)
            {
                if (e != null)
                {
                    bool compose = _composeBounds.Contains(e.Location), history = _historyBounds.Contains(e.Location);
                    bool bubble = false;
                    if (!compose && !history && !bubble && _hoverStarted == DateTime.MinValue)
                        _hoverStarted = DateTime.UtcNow;
                    if (_headBounds.Contains(e.Location) && !bubble)
                    {
                        DateTime now = DateTime.UtcNow;
                        if ((now - _strokeStarted).TotalSeconds > 1.2) { _strokeDistance = 0; _strokeStarted = now; }
                        else _strokeDistance += Math.Min(25 * _dpiScale, Math.Abs(e.X - _strokeCursor.X));
                        _strokeCursor = e.Location;
                        if (_strokeDistance > 55 * _dpiScale) StrokeHead();
                    }
                    else _strokeDistance = 0;
                    Cursor = compose || history || bubble || _headBounds.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
                    _toolbarTip.SetToolTip(this, compose ? "写消息" : history ? "消息历史" : bubble ? "点击查看这条消息" : "");
                }
                return;
            }
            Point cursor = Cursor.Position;
            int dx = cursor.X - _dragCursor.X, dy = cursor.Y - _dragCursor.Y;
            if (Math.Abs(dx) + Math.Abs(dy) <= 5 && !_moved) return;
            _moved = true; _motion.SetDragging(true, dx < 0);
            _holdTarget = Math.Max(-2.5, Math.Min(2.5, (cursor.X - _strokeCursor.X) * .12));
            _strokeCursor = cursor; _lastDragMove = DateTime.UtcNow;
            Location = new Point(_dragPosition.X + dx, _dragPosition.Y + dy);
            if (_chat != null && _chat.Visible) PositionChat();
        }
        private void EndDrag(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Point point = Cursor.Position; Capture = false;
                // 等当前鼠标释放消息处理完，再激活菜单，避免旧捕获窗口抢回焦点。
                BeginInvoke(new Action(() => _menu.ShowPopup(point, _dpiScale))); return;
            }
            if (!_dragging) return;
            _dragging = false; _motion.SetDragging(false); _holdTarget = 0; Capture = false; _lastActivity = DateTime.Now;
            _toolbarTip.Active = true;
            if (_moved) { RememberPosition(); }
            else if (_composeBounds.Contains(e.Location))
            { if (_chat != null && _chat.Visible) _chat.Hide(); else { ShowChat(); _chat.FocusInput(); } }
            else if (_historyBounds.Contains(e.Location)) { ShowChat(); _chat.ShowHistory(); }
            else if (_headBounds.Contains(e.Location)) StrokeHead();
            else { SetExpression(6); }
        }

        private void ChangeScale(double multiplier)
        {
            SetScale(_cfg.scale * multiplier, true);
        }

        /// <summary>实时预览大小；拖动过程中不写盘，完成调整后保存。</summary>
        internal void SetScale(double value, bool save)
        {
            Rectangle work = Screen.FromControl(this).WorkingArea;
            int bottom = Bottom;
            int center = Left + Width / 2;
            _cfg.scale = Math.Max(0.2, Math.Min(1.5, value));
            ResizePet(); Location = new Point(center - Width / 2, bottom - Height);
            // 调整尺寸时保留可见性；鼠标自由拖动仍不吸附或限制位置。
            Location = new Point(Math.Max(work.Left, Math.Min(Left, work.Right - Width)),
                Math.Max(work.Top, Math.Min(Top, work.Bottom - Height)));
            if (save) RememberPosition();
            RenderPet();
        }
        private void ClampPosition()
        {
            // 只救回完全移出显示器的旧位置，不把用户放置的位置吸回工作区。
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle visible = Rectangle.Intersect(Bounds, screen.Bounds);
                if (visible.Width >= Math.Min(40, Width) && visible.Height >= Math.Min(40, Height)) return;
            }
            Rectangle work = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2)).WorkingArea;
            Location = new Point(Math.Max(work.Left, Math.Min(Left, work.Right - Width)),
                Math.Max(work.Top, Math.Min(Top, work.Bottom - Height)));
        }
        private void RememberPosition()
        {
            _cfg.lastX = Left.ToString(); _cfg.lastY = Top.ToString(); _cfg.Save();
        }
        public void ShowPet() { Show(); RenderPet(); }
        public void ShowChat()
        {
            if (_chat == null || _chat.IsDisposed) _chat = new ChatForm(_cfg, _memory, _llm, this);
            PositionChat();
            _chat.Show(); _chat.Activate();
            _panelHiddenSince = DateTime.MinValue; UpdateBubbles();
        }

        internal void PositionChat()
        {
            Rectangle work = Screen.FromControl(this).WorkingArea;
            int x = Left + (Width - _chat.Width) / 2;
            int y = Top - _chat.Height + (int)(75 * _dpiScale);
            if (y < work.Top)
            {
                int gap = (int)(12 * _dpiScale);
                x = Left - _chat.Width - gap >= work.Left ? Left - _chat.Width - gap : Right + gap;
                y = Math.Max(work.Top, Math.Min(Top, work.Bottom - _chat.Height));
            }
            _chat.Location = new Point(Math.Max(work.Left, Math.Min(x, work.Right - _chat.Width)),
                Math.Max(work.Top, Math.Min(y, work.Bottom - _chat.Height)));
        }

        internal void ApplyAppearance()
        {
            TopMost = _cfg.topMost;
            int style = Native.GetExStyle(Handle);
            Native.SetExStyle(Handle, _cfg.clickThrough ? style | Native.WS_EX_TRANSPARENT : style & ~Native.WS_EX_TRANSPARENT);
            if (_chat != null && !_chat.IsDisposed) _chat.TopMost = _cfg.topMost;
            RenderPet();
        }

        private void BuildMenu()
        {
            _menu = new PetMenuForm(new Action[] {
                () => { ShowChat(); _chat.FocusInput(); },
                () => { ShowChat(); _chat.ShowTools(); },
                () => { ShowChat(); _chat.ShowAppearance(); },
                () => { ShowChat(); _chat.ShowVision(); },
                () => { ShowChat(); _chat.ShowSettings(); },
                () => { _cfg.climb = true; _cfg.Save(); StartClimbing(); }
            }, SetExpression, () => ChangeScale(1 / 1.1), () => ChangeScale(1.1),
            () => { Rectangle work = Screen.PrimaryScreen.WorkingArea; Location = new Point(work.Right - Width - 24, work.Bottom - Height - 10); RememberPosition(); },
            () => { if (Visible) Hide(); else ShowPet(); }, Close);
        }
        /// <summary>低频处理提醒和闲置移动，动画不参与业务调度。</summary>
        private void TickClock()
        {
            DateTime now = DateTime.Now;
            if (_chat != null && !_chat.IsDisposed && !_chat.Visible && _chat.CanRelease)
            {
                if (_panelHiddenSince == DateTime.MinValue) _panelHiddenSince = DateTime.UtcNow;
                if ((DateTime.UtcNow - _panelHiddenSince).TotalSeconds >= 30)
                {
                    _chat.Shutdown(); _chat.Dispose(); _chat = null; _panelHiddenSince = DateTime.MinValue;
                    Trace.Log("闲置面板已释放");
                }
            }
            else _panelHiddenSince = DateTime.MinValue;
            Features.Tick(DateTime.UtcNow);
            Point cursor = Cursor.Position;
            if (cursor != _lastCursor) { _lastActivity = now; _lastCursor = cursor; }
            if (_pomodoroEnd != DateTime.MinValue && now >= _pomodoroEnd)
            {
                _pomodoroEnd = DateTime.MinValue;
                System.Media.SystemSounds.Asterisk.Play();
            }
            if (_cfg.hourlyChime && now.Minute == 0 && _lastHour != now.Hour)
            { _lastHour = now.Hour; Say("现在是 " + now.Hour + " 点，记得喝水。 "); }
            if (_cfg.sedentaryMinutes > 0 && (now - _lastActivity).TotalMinutes >= _cfg.sedentaryMinutes)
            { _lastActivity = now; Say("坐了好一会儿了，起来走走吧。 "); }
            if (_cfg.climb && Visible && !_dragging && !_wandering && !_climbing && !_approachingWall && !_talking && _motion.CurrentName == "idle" &&
                (_chat == null || !_chat.Visible) && (now - _lastActivity).TotalSeconds > 60 && _random.Next(45) == 0)
            {
                Rectangle work = Screen.FromControl(this).WorkingArea;
                if (Left <= work.Left + 35 || Right >= work.Right - 35) StartClimbing();
            }
            if (_cfg.wander && Visible && !_dragging && !_wandering && !_talking && _motion.CurrentName == "idle" && (_chat == null || !_chat.Visible) &&
                (now - _lastActivity).TotalSeconds > 90 && _random.Next(40) == 0)
            {
                Rectangle work = Screen.FromControl(this).WorkingArea;
                int step = (int)(40 * _dpiScale) * (_random.Next(2) == 0 ? -1 : 1);
                int next = Math.Max(work.Left, Math.Min(Left + step, work.Right - Width));
                if (next == Left) return;
                _walkStart = Location; _walkTarget = new Point(next, Top); _walkStarted = DateTime.MinValue;
                _manualWalk = false; _wandering = true; _motion.SetWalking(true, next < Left);
            }
        }

        /// <summary>仅在屏幕边缘爬行，主动拖动会立即接管位置。</summary>
        internal void StartClimbing()
        {
            if (_dragging || _talking || _wandering || _climbing || _approachingWall) return;
            Rectangle work = Screen.FromControl(this).WorkingArea;
            bool left = Left + Width / 2 < work.Left + work.Width / 2;
            float h = 475 * (float)_cfg.scale * _dpiScale, w = h * .8f;
            float imageLeft = (Width - w) / 2;
            _wallLeft = left;
            _wallX = left ? (int)(work.Left - imageLeft - w * (1 - _motion.ClimbContactX)) : (int)(work.Right - imageLeft - w * _motion.ClimbContactX);
            _approachingWall = true; _motion.SetWalking(true, left);
        }

        internal void StartPomodoro(int minutes)
        {
            foreach (var reminder in Features.Data.Reminders.FindAll(r => r.PluginId == "pomodoro")) Features.ChangeReminder(reminder.Id, "delete");
            _pomodoroEnd = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : DateTime.MinValue;
            if (minutes > 0) { Features.AddReminder("时间到啦，休息一下吧！", DateTime.UtcNow.AddMinutes(minutes), 0, "pomodoro"); Say("一起专注 " + minutes + " 分钟吧。"); }
        }

        private void CancelMovement(bool settle = true)
        {
            if (settle && (_approachingWall || _wandering)) _motion.SetWalking(false);
            else if (settle && _climbing) _motion.SetClimbing(false);
            if (_approachingWall || _wandering || _climbing) RememberPosition();
            _approachingWall = _wandering = _climbing = _manualWalk = false;
        }

        private void StartSmallWalk(bool? left)
        {
            if (_dragging || _talking) return;
            CancelMovement(false);
            Rectangle work = Screen.FromControl(this).WorkingArea;
            int distance = (int)(45 * _dpiScale);
            bool goLeft = left ?? Right + distance > work.Right;
            int next = goLeft ? Math.Max(work.Left, Left - distance) : Math.Min(work.Right - Width, Left + distance);
            _walkStart = Location; _walkTarget = new Point(next, Top); _walkStarted = DateTime.MinValue;
            _manualWalk = true; _wandering = true; _motion.SetWalking(true, next < Left);
        }

        protected override void WndProc(ref Message m)
        {
            // 保持不抢焦点，同时正常接收鼠标按下，不能吃掉第一次拖动。
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotKeyId)
            { if (Visible) Hide(); else ShowPet(); return; }
            base.WndProc(ref m);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _animation.Stop(); _clock.Stop(); _animation.Dispose(); _clock.Dispose();
            RememberPosition();
            if (_chat != null && !_chat.IsDisposed) { _chat.Shutdown(); _chat.Dispose(); }

            _toolbarTip.Dispose();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            _menu.Dispose(); Native.UnregisterHotKey(Handle, HotKeyId);
            _motion.Dispose();
            _surface.Dispose(); _bubbleWindow.Dispose();
            Features.Dispose();
            base.OnFormClosed(e);
        }
    }
}
