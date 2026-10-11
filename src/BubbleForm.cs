using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ButangPet
{
    /// <summary>独立透明消息窗口；每条摘要可点击，布局避开角色并留在当前屏幕内。</summary>
    internal sealed class BubbleForm : Form
    {
        private readonly Native.LayeredSurface _surface = new Native.LayeredSurface();
        private readonly List<string> _texts = new List<string>();
        private float _unit = 1;
        private int _hover = -1;
        private bool _dirty = true;
        private Rectangle _close;
        private bool _compact;
        public Rectangle[] CardBounds { get; private set; } = new Rectangle[0];
        public event Action<string> MessageClicked;
        public event Action Dismissed;
        public event Action ExpandedRequested;

        public BubbleForm()
        {
            Text = "不糖酱 · 消息气泡"; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            MouseMove += (s, e) => {
                int hover = Array.FindIndex(CardBounds, r => r.Contains(e.Location));
                if (hover != _hover) { _hover = hover; _dirty = true; PaintMessages(); }
                Cursor = hover >= 0 || _close.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            };
            MouseLeave += (s, e) => { _hover = -1; _dirty = true; PaintMessages(); };
            MouseUp += (s, e) => {
                if (e.Button != MouseButtons.Left) return;
                if (_close.Contains(e.Location)) { Dismissed?.Invoke(); return; }
                if (_compact) { ExpandedRequested?.Invoke(); return; }
                int index = Array.FindIndex(CardBounds, r => r.Contains(e.Location));
                if (index >= 0 && index < _texts.Count) MessageClicked?.Invoke(_texts[index]);
            };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE; return cp; }
        }

        public void UpdateMessages(IList<string> texts, Rectangle anchor, float unit, bool topMost, Form owner)
        {
            if (texts.Count == 0) { Hide(); return; }
            if (_unit != unit) { _unit = unit; _dirty = true; }
            bool changed = _texts.Count != texts.Count;
            for (int i = 0; !changed && i < texts.Count; i++) changed = _texts[i] != texts[i];
            if (changed) { _texts.Clear(); _texts.AddRange(texts); _dirty = true; }
            Rectangle work = Screen.FromRectangle(anchor).WorkingArea;
            int gap = (int)(12 * unit), width = (int)(260 * unit);
            int rightSpace = work.Right - anchor.Right - gap, leftSpace = anchor.Left - work.Left - gap;
            bool right = rightSpace >= width || rightSpace >= leftSpace;
            int available = right ? rightSpace : leftSpace;
            if (available >= 150 * unit) width = Math.Min(width, available);
            int height = (int)((90 + (texts.Count - 1) * 54) * unit);
            bool compact = false;
            int x = right ? anchor.Right + gap : anchor.Left - gap - width;
            int y = Math.Max(work.Top, Math.Min(anchor.Top, work.Bottom - height));
            if (available < width)
            {
                x = Math.Max(work.Left, Math.Min(anchor.Left + (anchor.Width - width) / 2, work.Right - width));
                y = anchor.Top - height - gap >= work.Top ? anchor.Top - height - gap : anchor.Bottom + gap;
                if (y + height > work.Bottom)
                {
                    compact = true; width = Math.Min(work.Width - gap * 2, (int)(190 * unit)); height = (int)(48 * unit);
                    x = Math.Max(work.Left + gap, Math.Min(anchor.Left + (anchor.Width - width) / 2, work.Right - width - gap));
                    y = anchor.Top - height - gap >= work.Top ? anchor.Top - height - gap : Math.Min(anchor.Bottom + gap, work.Bottom - height - gap);
                }
            }
            if (_compact != compact) { _compact = compact; _dirty = true; }
            Rectangle bounds = new Rectangle(x, y, width, height);
            if (Bounds != bounds) { Bounds = bounds; _dirty = true; }
            TopMost = topMost;
            if (!Visible) { Show(owner); _dirty = true; }
            PaintMessages();
        }

        private void PaintMessages()
        {
            if (!_dirty || !Visible || IsDisposed || Width < 20 || Height < 20) return;
            _surface.Ensure(Width, Height);
            CardBounds = new Rectangle[_compact ? 1 : _texts.Count];
            using (Graphics g = Graphics.FromImage(_surface.Canvas))
            {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
                float y = 3 * _unit;
                int count = _compact ? 1 : _texts.Count;
                for (int i = 0; i < count; i++)
                {
                    int inset = (int)((3 + i * 4) * _unit), h = (int)((_compact ? 42 : i == 0 ? 82 : 48) * _unit);
                    Rectangle r = new Rectangle(inset, (int)y, Width - inset * 2, h); CardBounds[i] = r;
                    using (GraphicsPath path = Rounded(r, 14 * _unit))
                    using (Brush fill = new SolidBrush(i == _hover ? Color.FromArgb(253, 248, 237, 247) : i == 0 ? Color.FromArgb(253, 255, 250, 253) : Color.FromArgb(253, 246, 235, 248)))
                    using (Pen border = new Pen(Color.FromArgb(207, 180, 212), _unit))
                    {
                        g.FillPath(fill, path); g.DrawPath(border, path);
                    }
                    float textTop = r.Top + (_compact ? 23 : i == 0 ? 25 : 7) * _unit;
                    if (i == 0)
                    {
                        using (Font font = new Font("Microsoft YaHei UI", 8 * _unit))
                        using (Brush ink = new SolidBrush(Color.FromArgb(112, 82, 121)))
                            g.DrawString(_compact ? _texts.Count + " 条消息 · 点击展开" : "不糖酱" + (_texts.Count > 1 ? " · 最近 " + _texts.Count + " 条" : ""), font, ink, r.Left + 12 * _unit, r.Top + 7 * _unit);
                        _close = new Rectangle(r.Right - (int)(24 * _unit), r.Top + (int)(5 * _unit), (int)(18 * _unit), (int)(18 * _unit));
                        using (Pen pen = new Pen(Color.FromArgb(130, 104, 140), 1.2f * _unit))
                        { g.DrawLine(pen, _close.Left + 5 * _unit, _close.Top + 5 * _unit, _close.Right - 5 * _unit, _close.Bottom - 5 * _unit); g.DrawLine(pen, _close.Right - 5 * _unit, _close.Top + 5 * _unit, _close.Left + 5 * _unit, _close.Bottom - 5 * _unit); }
                    }
                    using (Font font = new Font("Microsoft YaHei UI", (i == 0 ? 10 : 9) * _unit))
                    using (Brush ink = new SolidBrush(Color.FromArgb(60, 43, 67)))
                    using (StringFormat format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit })
                        g.DrawString(_texts[i], font, ink, new RectangleF(r.Left + 12 * _unit, textTop, r.Width - 24 * _unit, (_compact ? 18 : i == 0 ? 36 : 34) * _unit), format);
                    if (i == 0 && !_compact)
                        using (Font font = new Font("Microsoft YaHei UI", 8 * _unit))
                        using (Brush ink = new SolidBrush(Color.FromArgb(112, 82, 121)))
                            g.DrawString("点击查看全文", font, ink, r.Left + 12 * _unit, r.Bottom - 18 * _unit);
                    y += h + 6 * _unit;
                }
                g.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            }
            _surface.Present(Handle, Location, 255); _dirty = false;
        }
        private static GraphicsPath Rounded(Rectangle r, float radius)
        {
            var path = new GraphicsPath(); float d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; } base.WndProc(ref m);
        }
        protected override void Dispose(bool disposing) { if (disposing) _surface.Dispose(); base.Dispose(disposing); }
    }
}
