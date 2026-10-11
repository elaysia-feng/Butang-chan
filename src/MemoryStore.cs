using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ButangPet
{
    internal class ChatMessage
    {
        public string role;      // user | assistant
        public string content;
        public long ts;

        public Dictionary<string, object> ToMap()
        {
            Dictionary<string, object> m = new Dictionary<string, object>();
            m["role"] = role;
            m["content"] = content;
            m["ts"] = ts;
            return m;
        }
    }

    /// <summary>
    /// 长对话记忆。
    ///
    /// 存法：每天一个 JSONL，一行一条消息，永不删除——删了就没法回看，也让"摘要"失去依据。
    /// 取法：滚动摘要 + 最近 N 轮原文。
    ///
    /// 为什么要有摘要：把所有历史原样塞进 prompt 会被 token 上限卡死，而且模型对
    /// 几千轮前的细节本来就记不住。摘要让上下文长度固定，代价是多一次模型调用。
    /// </summary>
    internal class MemoryStore
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly string _dir;
        private readonly string _summaryPath;
        private readonly List<ChatMessage> _cache = new List<ChatMessage>();
        private bool _loaded;
        private readonly object _gate = new object();

        /// <summary>摘要已覆盖到第几条消息（不含）。</summary>
        public int Covered { get; private set; }
        public string SummaryText { get; private set; }

        public MemoryStore(string dataDir)
        {
            _dir = Path.Combine(dataDir, "chat");
            Directory.CreateDirectory(_dir);
            _summaryPath = Path.Combine(_dir, "summary.json");
        }

        public int Count { get { lock (_gate) { EnsureLoaded(); return _cache.Count; } } }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            ReadSummary();
            ReadAll();
        }

        // ── 写 ──
        public void Append(string role, string content)
        {
            if (content == null) return;
            content = content.Trim();
            if (content.Length == 0) return;

            lock (_gate)
            {
                EnsureLoaded();
                ChatMessage msg = new ChatMessage { role = role, content = content, ts = NowMs() };
                _cache.Add(msg);
                try
                {
                    string file = Path.Combine(_dir, DateTime.Now.ToString("yyyy-MM-dd") + ".jsonl");
                    string line = Json.Serialize(msg.ToMap());
                    File.AppendAllText(file, line + Environment.NewLine, Utf8);
                }
                catch (Exception ex)
                {
                    Trace.Log("记忆写入失败: " + ex.Message);
                }
            }
        }

        public void SaveSummary(string text, int covered)
        {
            lock (_gate)
            {
                EnsureLoaded();
                SummaryText = text ?? "";
                Covered = covered;
                Dictionary<string, object> m = new Dictionary<string, object>();
                m["text"] = SummaryText;
                m["covered"] = Covered;
                m["updated"] = NowMs();
                try
                {
                    string tmp = _summaryPath + ".tmp";
                    File.WriteAllText(tmp, Json.Serialize(m), Utf8);
                    if (File.Exists(_summaryPath)) File.Delete(_summaryPath);
                    File.Move(tmp, _summaryPath);
                }
                catch (Exception ex)
                {
                    Trace.Log("摘要写入失败: " + ex.Message);
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _cache.Clear();
                _loaded = true;
                SummaryText = "";
                Covered = 0;
                try
                {
                    if (Directory.Exists(_dir))
                        foreach (string f in Directory.GetFiles(_dir, "*.jsonl*"))
                            File.Delete(f);
                    if (File.Exists(_summaryPath)) File.Delete(_summaryPath);
                }
                catch (Exception ex)
                {
                    Trace.Log("清空记忆失败: " + ex.Message);
                }
            }
        }

        // ── 读 ──
        private void ReadSummary()
        {
            if (!File.Exists(_summaryPath)) return;
            try
            {
                Dictionary<string, object> m =
                    Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(_summaryPath, Encoding.UTF8));
                if (m == null) return;
                SummaryText = m.ContainsKey("text") ? Convert.ToString(m["text"]) : "";
                Covered = m.ContainsKey("covered") ? Convert.ToInt32(m["covered"]) : 0;
            }
            catch (Exception ex)
            {
                Trace.Log("摘要读取失败: " + ex.Message);
            }
        }

        private void ReadAll()
        {
            try
            {
                string[] files = Directory.GetFiles(_dir, "*.jsonl");
                Array.Sort(files);
                foreach (string f in files)
                {
                    foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            Dictionary<string, object> m =
                                Json.Deserialize<Dictionary<string, object>>(line);
                            if (m == null || !m.ContainsKey("content")) continue;
                            _cache.Add(new ChatMessage
                            {
                                role = m.ContainsKey("role") ? Convert.ToString(m["role"]) : "user",
                                content = Convert.ToString(m["content"]),
                                ts = m.ContainsKey("ts") ? Convert.ToInt64(m["ts"]) : 0
                            });
                        }
                        catch { /* 单行坏了不该让整份记忆读不出来 */ }
                    }
                }
                _cache.Sort(delegate(ChatMessage a, ChatMessage b) { return a.ts.CompareTo(b.ts); });
            }
            catch (Exception ex)
            {
                Trace.Log("记忆读取失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 组装给模型的历史：更早的用摘要，最近 keepMessages 条用原文。
        /// </summary>
        public List<ChatMessage> ContextFor(int keepMessages)
        {
            lock (_gate)
            {
                EnsureLoaded();
                List<ChatMessage> result = new List<ChatMessage>();
                int count = _cache.Count;
                int from = count - keepMessages;
                if (from < 0) from = 0;
                // Covered 之后还没被摘要的部分，从 Covered 开始逐条补
                int start = Covered > from ? Covered : from;
                for (int i = start; i < count; i++) result.Add(_cache[i]);
                return result;
            }
        }

        /// <summary>这次调用该不该触发一次滚动摘要。</summary>
        public bool ShouldSummarize(int threshold, int keepMessages)
        {
            lock (_gate)
            {
                EnsureLoaded();
                int pending = _cache.Count - Covered - keepMessages;
                return pending >= Math.Max(4, threshold / 2);
            }
        }

        /// <summary>待摘要的消息段（不含已摘要、不含要保留的最近几轮）。</summary>
        public List<ChatMessage> PendingForSummary(int keepMessages)
        {
            lock (_gate)
            {
                EnsureLoaded();
                int end = _cache.Count - keepMessages;
                if (end <= Covered) return new List<ChatMessage>();
                List<ChatMessage> slice = new List<ChatMessage>();
                for (int i = Covered; i < end; i++) slice.Add(_cache[i]);
                return slice;
            }
        }

        public List<ChatMessage> Tail(int n)
        {
            lock (_gate)
            {
                EnsureLoaded();
                List<ChatMessage> result = new List<ChatMessage>();
                int from = _cache.Count - n;
                if (from < 0) from = 0;
                for (int i = from; i < _cache.Count; i++) result.Add(_cache[i]);
                return result;
            }
        }

        public long TotalChars()
        {
            lock (_gate)
            {
                EnsureLoaded();
                long total = 0;
                foreach (ChatMessage m in _cache) total += m.content.Length;
                return total;
            }
        }

        private static long NowMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        }
    }
}
