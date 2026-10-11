using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace ButangPet
{
    /// <summary>透明连续帧播放器；交互通过离线光流短衔接进入目标姿态。</summary>
    internal sealed class MotionPlayer : IDisposable
    {
        private sealed class Clip
        {
            public string Name, Next;
            public string[] Files;
            public int[] Durations;
            public bool Loop;
        }
        private readonly Dictionary<string, Clip> _clips = new Dictionary<string, Clip>();
        private readonly Dictionary<string, Bitmap> _cache = new Dictionary<string, Bitmap>();
        private readonly Queue<string> _cacheOrder = new Queue<string>();
        private readonly Size _canvas;
        private Size _renderCanvas;
        private Clip _current;
        private int _frame, _sampleStep = 4, _pendingFrame;
        private double _elapsed;
        private bool _loop, _dragging, _walking, _facingLeft;
        private string _queued, _pendingGoal, _pendingAction, _bridgeNext, _deferredGoal, _deferredAction;
        public int Revision { get; private set; }
        public int DecodeCount { get; private set; }
        public float ClimbContactX { get; private set; }
        public string CurrentName { get { return _current.Name; } }
        public int FrameIndex { get { return _frame; } }
        public int FrameCount { get { return _current.Files.Length; } }
        public bool FacingLeft { get { return _facingLeft && (_current.Name == "walk" || _current.Name == "climb"); } }
        public bool InWalkingPose { get { return _current.Name == "walk"; } }
        public bool InClimbingPose { get { return _current.Name == "climb"; } }
        public int WalkDuration { get { return _clips["walk"].Durations.Sum() * 2; } }
        public Bitmap Frame
        {
            get
            {
                string file = _current.Files[_frame];
                if (_cache.TryGetValue(file, out Bitmap cached)) return cached;
                using (Image source = Image.FromFile(file))
                {
                    if (source.Size != _canvas) throw new InvalidDataException("动画画布不一致：" + file);
                    cached = new Bitmap(_renderCanvas.Width, _renderCanvas.Height, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(cached))
                    { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(source, 0, 0, cached.Width, cached.Height); }
                }
                DecodeCount++;
                // 96帧覆盖完整86帧待机和一组短衔接，按实际显示尺寸缓存。
                while (_cache.Count >= 96)
                {
                    string oldest = _cacheOrder.Dequeue(); _cache[oldest].Dispose(); _cache.Remove(oldest);
                }
                _cache.Add(file, cached); _cacheOrder.Enqueue(file); return cached;
            }
        }

        public MotionPlayer(string root)
        {
            var document = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(
                File.ReadAllText(Path.Combine(root, "manifest.json")));
            var canvas = (Dictionary<string, object>)document["canvas"];
            _canvas = _renderCanvas = new Size(Convert.ToInt32(canvas["width"]), Convert.ToInt32(canvas["height"]));
            if (document.TryGetValue("bridgeSampleStep", out object step)) _sampleStep = Convert.ToInt32(step);
            foreach (var entry in (Dictionary<string, object>)document["clips"])
            {
                var spec = (Dictionary<string, object>)entry.Value;
                var files = (System.Collections.IList)spec["frames"];
                var durations = (System.Collections.IList)spec["durations"];
                if (files.Count != durations.Count || files.Count == 0) throw new InvalidDataException("动画时间线无效：" + entry.Key);
                Clip clip = new Clip { Name = entry.Key, Files = new string[files.Count], Durations = new int[files.Count], Loop = Convert.ToBoolean(spec["loop"]) };
                if (spec.TryGetValue("next", out object next)) clip.Next = Convert.ToString(next);
                for (int i = 0; i < files.Count; i++)
                {
                    clip.Files[i] = Path.Combine(root, Convert.ToString(files[i]));
                    if (!File.Exists(clip.Files[i])) throw new FileNotFoundException("缺少动画帧", clip.Files[i]);
                    clip.Durations[i] = Math.Max(20, Convert.ToInt32(durations[i]));
                }
                _clips.Add(entry.Key, clip);
            }
            foreach (string name in new[] { "idle", "wave", "walk", "cheer", "shy", "sleepy", "lift", "climb" })
                if (!_clips.ContainsKey(name) || _clips[name].Files.Length < 8) throw new InvalidDataException("缺少完整动画：" + name);
            int contact = 0;
            foreach (int index in new[] { 0, 7, 14, 21 })
                using (Bitmap image = new Bitmap(_clips["climb"].Files[index]))
                    for (int y = 0; y < image.Height; y += 2)
                        for (int x = image.Width - 1; x > contact; x--)
                            if (image.GetPixel(x, y).A > 96) { contact = x; break; }
            ClimbContactX = (float)(contact + 1) / _canvas.Width;
            Start("idle");
        }

        public void SetRenderHeight(int height)
        {
            height = Math.Max(64, Math.Min(_canvas.Height, height));
            Size size = new Size((int)Math.Round((double)height * _canvas.Width / _canvas.Height), height);
            if (size == _renderCanvas) return;
            _renderCanvas = size; ClearCache(); Revision++;
        }
        private void ClearCache()
        {
            foreach (Bitmap bitmap in _cache.Values) bitmap.Dispose();
            _cache.Clear(); _cacheOrder.Clear();
        }
        private void Start(string name, bool? loop = null)
        {
            _current = _clips[name]; _frame = 0; _elapsed = 0;
            _loop = loop ?? (name == "walk" ? _walking : _current.Loop); Revision++;
            if (name == "idle") _facingLeft = false;
            if (name == "walk-entry" || name == "climb-entry") _facingLeft = false;
            if (name == "walk-entry-left" || name == "climb-entry-left") _facingLeft = true;
        }

        /// <summary>普通自主动作顺序播放；用户触摸与提起走更高优先级的短衔接。</summary>
        public void Play(string name)
        {
            if (!_clips.ContainsKey(name) || _dragging) return;
            if (name == "walk") { SetWalking(true); return; }
            if (_current.Name != "idle") { _queued = name; return; }
            Start(name, name == "idle");
        }
        public void React(string name)
        {
            if (!_clips.ContainsKey(name) || _dragging) return;
            _walking = false; _queued = null;
            Request("settle", name);
        }
        public void SetTalking(bool value)
        {
            // 保留宿主入口名称；文本回复期间只用思考反馈，不播放张嘴动画。
            if (value && !_dragging && _current.Name == "idle") Play("blink");
        }
        public void SetDragging(bool value, bool facingLeft = false)
        {
            if (_dragging == value) return;
            _dragging = value; _walking = false; _queued = null;
            Request(value ? "lift" : "settle", value ? "lift" : "idle");
        }
        public void SetWalking(bool value, bool facingLeft = false)
        {
            _walking = value;
            Request("settle", value ? (facingLeft ? "walk-entry-left" : "walk-entry") : "idle");
        }
        public void SetClimbing(bool value, bool facingLeft = false)
        {
            _walking = false;
            Request("settle", value ? (facingLeft ? "climb-entry-left" : "climb-entry") : "idle");
        }

        private void Request(string goal, string after)
        {
            _queued = null;
            if (_current.Name.StartsWith("bridge-", StringComparison.Ordinal))
            { _deferredGoal = goal; _deferredAction = after; return; }
            _pendingGoal = goal; _pendingAction = after;
            _pendingFrame = ((_frame + _sampleStep - 1) / _sampleStep) * _sampleStep;
            if (_pendingFrame >= _current.Files.Length) _pendingFrame = _loop ? 0 : -1;
            if (_frame == _pendingFrame) BeginBridge();
        }
        private void BeginBridge()
        {
            string source = _current.Name + (FacingLeft ? "-left" : "");
            string bridge = "bridge-" + _pendingGoal + "-" + source + "-" + _frame.ToString("000");
            if (!_clips.ContainsKey(bridge)) throw new InvalidDataException("缺少交互短衔接：" + bridge);
            _bridgeNext = _pendingAction; _pendingGoal = _pendingAction = null;
            Start(bridge, false);
        }

        public void Update(double milliseconds)
        {
            _elapsed += Math.Max(0, Math.Min(100, milliseconds));
            while (_elapsed >= _current.Durations[_frame])
            {
                _elapsed -= _current.Durations[_frame]; _frame++; Revision++;
                if (_frame >= _current.Files.Length)
                {
                    if (_current.Name.StartsWith("bridge-", StringComparison.Ordinal))
                    {
                        string next = _bridgeNext; _bridgeNext = null; Start(next);
                        if (_deferredGoal != null)
                        {
                            string goal = _deferredGoal, action = _deferredAction; _deferredGoal = _deferredAction = null;
                            Request(goal, action);
                        }
                    }
                    else if (_loop && _queued == null) _frame = 0;
                    else
                    {
                        string next = _queued ?? _current.Next ?? "idle"; _queued = null; Start(next);
                        if (_pendingGoal != null) Request(_pendingGoal, _pendingAction);
                    }
                }
                if (_pendingGoal != null && _frame == _pendingFrame) BeginBridge();
            }
        }
        public void Dispose() { ClearCache(); _clips.Clear(); }
    }
}
