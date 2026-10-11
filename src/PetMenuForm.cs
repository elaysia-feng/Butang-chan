using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ButangPet
{
    /// <summary>紧凑桌宠菜单；激活外关闭，空白点击和Esc均能收起。</summary>
    internal sealed class PetMenuForm : Form
    {
        internal sealed class Item
        {
            public string Text;
            public Action Run;
            public Rectangle Bounds;
        }
        private readonly List<Item> _items = new List<Item>();
        private readonly Native.LayeredSurface _surface = new Native.LayeredSurface();
        private readonly Timer _outside = new Timer { Interval = 16 };
        private int _hover = -1;
        private float _unit = 1;
        private bool _wasPressed;
        private IntPtr _foreground;
        private IntPtr _keyboardHook;
        private readonly Native.KeyboardHook _keyCallback;
        private IntPtr _mouseHook;
        private readonly Native.MouseHook _mouseCallback;

        public PetMenuForm(Action[] primary, Action<int> gesture, Action smaller, Action larger, Action reset, Action hide, Action exit)
        {
            Text = "不糖酱 · 快捷菜单"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; TopMost = true; KeyPreview = true;
            // 只在菜单显示期间截取导航键，不读取或记录输入文字。
            _keyCallback = (code, message, data) => {
                if (code >= 0 && Visible && (message.ToInt32() == 0x0100 || message.ToInt32() == 0x0104))
                {
                    Keys key = (Keys)Marshal.ReadInt32(data);
                    if (key == Keys.Escape) { Hide(); return new IntPtr(1); }
                    if (key == Keys.Enter) { Run(_hover); return new IntPtr(1); }
                    if (key == Keys.Down || key == Keys.Up)
                    { _hover = (_hover + (key == Keys.Down ? 1 : _items.Count - 1) + _items.Count) % _items.Count; PaintMenu(); return new IntPtr(1); }
                }
                return Native.CallNextHookEx(_keyboardHook, code, message, data);
            };
            _mouseCallback = (code, message, data) => {
                if (code >= 0 && Visible && (message.ToInt32() == 0x0201 || message.ToInt32() == 0x0204))
                {
                    Point point = new Point(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
                    if (!Bounds.Contains(point)) Hide();
                }
                return Native.CallNextHookEx(_mouseHook, code, message, data);
            };
            string[] labels = { "聊一会儿", "提醒与任务", "外观大小", "看看画面", "设置", "去爬墙" };
            for (int i = 0; i < labels.Length; i++) _items.Add(new Item { Text = labels[i], Run = primary[i] });
            string[] gestures = { "挥手", "开心", "害羞", "困啦", "走走" };
            for (int i = 0; i < gestures.Length; i++) { int action = i + 6; _items.Add(new Item { Text = gestures[i], Run = () => gesture(action) }); }
            _items.Add(new Item { Text = "−", Run = smaller }); _items.Add(new Item { Text = "+", Run = larger });
            _items.Add(new Item { Text = "回到角落", Run = reset }); _items.Add(new Item { Text = "隐藏 · F10", Run = hide });
            _items.Add(new Item { Text = "退出", Run = exit });
            VisibleChanged += (s, e) => {
                if (Visible)
                {
                    _outside.Start();
                    _keyboardHook = Native.SetWindowsHookEx(13, _keyCallback, Native.GetModuleHandle(null), 0);
                    _mouseHook = Native.SetMouseHook(14, _mouseCallback, Native.GetModuleHandle(null), 0);
                }
                else { _outside.Stop(); ReleaseHook(); }
            };
            MouseMove += (s, e) => {
                int index = _items.FindIndex(item => item.Bounds.Contains(e.Location));
                if (index != _hover) { _hover = index; PaintMenu(); }
                Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            };
            MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) Run(_items.FindIndex(item => item.Bounds.Contains(e.Location))); };
            KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape) Hide();
                else if (e.KeyCode == Keys.Enter) Run(_hover);
                else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
                { _hover = (_hover + (e.KeyCode == Keys.Down ? 1 : _items.Count - 1) + _items.Count) % _items.Count; PaintMenu(); }
            };
            _outside.Tick += (s, e) => {
                if (_keyboardHook == IntPtr.Zero && (Native.GetAsyncKeyState((int)Keys.Escape) & 0x8001) != 0) { Hide(); return; }
                IntPtr foreground = Native.GetForegroundWindow();
                if (foreground != IntPtr.Zero && foreground != _foreground && foreground != Handle) { Hide(); return; }
                bool pressed = (Native.GetAsyncKeyState(1) & 0x8000) != 0 || (Native.GetAsyncKeyState(2) & 0x8000) != 0;
                if (pressed && !_wasPressed && !Bounds.Contains(Cursor.Position)) Hide();
                _wasPressed = pressed;
            };
        }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        public void ShowPopup(Point point, float unit)
        {
            _unit = unit; Size = new Size((int)(246 * unit), (int)(274 * unit));
            Rectangle work = Screen.FromPoint(point).WorkingArea;
            Location = new Point(Math.Max(work.Left, Math.Min(point.X, work.Right - Width)), Math.Max(work.Top, Math.Min(point.Y, work.Bottom - Height)));
            _hover = -1; _wasPressed = (Native.GetAsyncKeyState(2) & 0x8000) != 0;
            _foreground = Native.GetForegroundWindow(); Show(); PaintMenu();
        }
        private void Run(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            Action action = _items[index].Run; Hide(); action();
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); return true; }
            if (keyData == Keys.Enter) { Run(_hover); return true; }
            if (keyData == Keys.Down || keyData == Keys.Up)
            { _hover = (_hover + (keyData == Keys.Down ? 1 : _items.Count - 1) + _items.Count) % _items.Count; PaintMenu(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private void PaintMenu()
        {
            if (!Visible || IsDisposed) return;
            _surface.Ensure(Width, Height);
            using (Graphics g = Graphics.FromImage(_surface.Canvas))
            {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle card = new Rectangle(2, 2, Width - 4, Height - 4);
                using (GraphicsPath path = Round(card, 15 * _unit))
                using (Brush paper = new SolidBrush(Color.FromArgb(253, 255, 250, 253)))
                using (Pen border = new Pen(Color.FromArgb(220, 199, 225), _unit))
                { g.FillPath(paper, path); g.DrawPath(border, path); }
                using (Font title = new Font("Microsoft YaHei UI", 12 * _unit, FontStyle.Bold))
                using (Brush ink = new SolidBrush(Color.FromArgb(72, 48, 83))) g.DrawString("不糖酱", title, ink, 16 * _unit, 12 * _unit);
                using (Font hint = new Font("Microsoft YaHei UI", 8 * _unit))
                using (Brush ink = new SolidBrush(Color.FromArgb(126, 102, 137))) g.DrawString("Esc 收起", hint, ink, 176 * _unit, 18 * _unit);
                for (int i = 0; i < 6; i++) _items[i].Bounds = Rect(14 + i % 2 * 110, 48 + i / 2 * 34, 104, 29);
                for (int i = 0; i < 5; i++) _items[6 + i].Bounds = Rect(14 + i * 44, 160, 40, 28);
                _items[11].Bounds = Rect(14, 201, 32, 27); _items[12].Bounds = Rect(51, 201, 32, 27);
                _items[13].Bounds = Rect(129, 201, 99, 27); _items[14].Bounds = Rect(14, 237, 128, 25); _items[15].Bounds = Rect(164, 237, 64, 25);
                using (Font text = new Font("Microsoft YaHei UI", 9 * _unit))
                using (Brush ink = new SolidBrush(Color.FromArgb(83, 58, 94)))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    for (int i = 0; i < _items.Count; i++)
                    {
                        Item item = _items[i];
                        using (GraphicsPath path = Round(item.Bounds, 7 * _unit))
                        using (Brush fill = new SolidBrush(i == _hover ? Color.FromArgb(236, 220, 243) : i < 6 ? Color.FromArgb(248, 240, 250) : Color.FromArgb(255, 250, 253))) g.FillPath(fill, path);
                        g.DrawString(item.Text, text, ink, item.Bounds, format);
                    }
                g.Flush(FlushIntention.Sync);
            }
            _surface.Present(Handle, Location, 255);
        }
        private Rectangle Rect(int x, int y, int width, int height) { return new Rectangle((int)(x * _unit), (int)(y * _unit), (int)(width * _unit), (int)(height * _unit)); }
        private static GraphicsPath Round(Rectangle r, float radius)
        {
            var path = new GraphicsPath(); float d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        private void ReleaseHook()
        {
            if (_keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyboardHook);
            if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
            _keyboardHook = _mouseHook = IntPtr.Zero;
        }
        protected override void Dispose(bool disposing) { if (disposing) { ReleaseHook(); _outside.Dispose(); _surface.Dispose(); } base.Dispose(disposing); }
    }
}
