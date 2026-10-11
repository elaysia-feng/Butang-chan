using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ButangPet
{
    /// <summary>本地提醒、任务反馈和插件事件；由窗口线程修改并持久化。</summary>
    internal sealed class CompanionFeatures : IDisposable
    {
        public sealed class Reminder
        {
            public string Id, Text, Status = "waiting", PluginId;
            public DateTime Due;
            public int IntervalMinutes;
        }
        public sealed class PetTask
        {
            public string Id, Title, Status, Detail;
            public int Progress;
            public DateTime Updated;
        }
        public sealed class Plugin
        {
            public string id = "", name, description, text, motion;
            public int intervalMinutes;
            public bool enabled;
        }
        public sealed class State
        {
            public List<Reminder> Reminders = new List<Reminder>();
            public List<PetTask> Tasks = new List<PetTask>();
            public List<string> EnabledPlugins = new List<string>();
        }
        private readonly string _stateFile, _pluginDirectory;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Action<Action> _dispatch;
        public State Data { get; private set; } = new State();
        public List<Plugin> Plugins { get; private set; } = new List<Plugin>();
        public event Action<string, string> Notify;
        public event Action Changed;
        public const string PipeName = "ButangPet.Events";

        public CompanionFeatures(string root, Action<Action> dispatch, string stateRoot = null)
        {
            _dispatch = dispatch;
            _stateFile = Path.Combine(stateRoot ?? root, "data", "companion.json");
            _pluginDirectory = Path.Combine(root, "plugins");
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFile));
            if (File.Exists(_stateFile))
                try { Data = new JavaScriptSerializer().Deserialize<State>(File.ReadAllText(_stateFile, Encoding.UTF8)) ?? new State(); }
                catch (Exception ex) { Trace.Log("提醒记录读取失败：" + ex.Message); }
            Data.Reminders = Data.Reminders ?? new List<Reminder>(); Data.Tasks = Data.Tasks ?? new List<PetTask>();
            Data.EnabledPlugins = Data.EnabledPlugins ?? new List<string>();
            // 重启前仍在执行的任务不能假装继续运行，等待外部程序再次报告。
            foreach (PetTask task in Data.Tasks.Where(t => t.Status == "running")) task.Status = "paused";
            LoadPlugins();
        }

        public void Start() { Task.Run(() => Listen()); }

        /// <summary>离线期间到期的提醒补发一次；重复提醒跳过错过的轮次。</summary>
        public void Tick(DateTime now)
        {
            bool changed = false;
            foreach (Reminder item in Data.Reminders.Where(r => r.Status == "waiting" && r.Due <= now).ToArray())
            {
                item.Status = "due"; changed = true;
                if (item.IntervalMinutes > 0)
                {
                    double missed = Math.Floor((now - item.Due).TotalMinutes / item.IntervalMinutes) + 1;
                    item.Due = item.Due.AddMinutes(missed * item.IntervalMinutes);
                }
                string motion = Plugins.Find(p => p.id == item.PluginId)?.motion ?? "wave";
                Notify?.Invoke(item.Text, motion);
            }
            if (changed) Save();
        }

        public void AddReminder(string text, DateTime due, int intervalMinutes = 0, string pluginId = null)
        {
            text = Clean(text, 500);
            if (text.Length == 0) throw new ArgumentException("请填写提醒内容");
            if (Data.Reminders.Count >= 100) throw new InvalidOperationException("提醒已达上限，请先删除旧提醒");
            Data.Reminders.Add(new Reminder { Id = Guid.NewGuid().ToString("N"), Text = text, Due = due.ToUniversalTime(),
                IntervalMinutes = Math.Max(0, Math.Min(10080, intervalMinutes)), PluginId = pluginId });
            Save();
        }

        public void ChangeReminder(string id, string action)
        {
            Reminder item = Data.Reminders.Find(r => r.Id == id);
            if (item == null) return;
            if (action == "delete") Data.Reminders.Remove(item);
            else if (action == "snooze") { item.Due = DateTime.UtcNow.AddMinutes(5); item.Status = "waiting"; }
            else if (action == "ack") item.Status = item.IntervalMinutes > 0 ? "waiting" : "done";
            else throw new ArgumentException("未知提醒操作");
            Save();
        }

        /// <summary>任务状态只接受明确事件，完成与失败不由模型文本推测。</summary>
        public void UpdateTask(string id, string title, string status, int progress, string detail)
        {
            if (!new[] { "waiting", "running", "success", "error", "paused", "cancelled" }.Contains(status))
                throw new ArgumentException("任务状态无效");
            id = Clean(id, 80); title = Clean(title, 160);
            if (id.Length == 0 || title.Length == 0) throw new ArgumentException("任务标识和标题不能为空");
            PetTask item = Data.Tasks.Find(t => t.Id == id);
            bool announce = item == null || item.Status != status;
            if (item == null) { item = new PetTask { Id = id }; Data.Tasks.Add(item); }
            item.Title = title; item.Status = status; item.Progress = Math.Max(0, Math.Min(100, progress));
            if (status == "success") item.Progress = 100;
            item.Detail = Clean(detail, 2000); item.Updated = DateTime.UtcNow;
            while (Data.Tasks.Count > 50) Data.Tasks.RemoveAt(0);
            if (announce) Notify?.Invoke(title + "：" + StatusText(status), status == "success" ? "cheer" : status == "error" ? "shy" : "blink");
            Save();
        }

        public void DeleteTask(string id) { Data.Tasks.RemoveAll(t => t.Id == id); Save(); }

        /// <summary>加载声明式插件；插件可以提供动作和重复提醒，不执行任意本地代码。</summary>
        public void LoadPlugins()
        {
            Directory.CreateDirectory(_pluginDirectory);
            var loaded = new List<Plugin>();
            foreach (string file in Directory.GetFiles(_pluginDirectory, "*.json").Take(50))
            {
                try
                {
                    if (new FileInfo(file).Length > 65536) throw new InvalidDataException("插件文件过大");
                    Plugin plugin = new JavaScriptSerializer().Deserialize<Plugin>(File.ReadAllText(file, Encoding.UTF8));
                    if (plugin == null || !System.Text.RegularExpressions.Regex.IsMatch(plugin.id ?? "", "^[a-z0-9-]{1,48}$") ||
                        string.IsNullOrWhiteSpace(plugin.name) || loaded.Any(p => p.id == plugin.id)) throw new InvalidDataException("插件标识或名称无效");
                    plugin.name = Clean(plugin.name, 80); plugin.description = Clean(plugin.description, 300);
                    plugin.text = Clean(plugin.text, 500); plugin.motion = ValidMotion(plugin.motion);
                    plugin.intervalMinutes = Math.Max(0, Math.Min(10080, plugin.intervalMinutes));
                    plugin.enabled = Data.EnabledPlugins.Contains(plugin.id); loaded.Add(plugin);
                }
                catch (Exception ex) { Trace.Log("插件未加载 " + Path.GetFileName(file) + "：" + ex.Message); }
            }
            Plugins = loaded;
            Data.EnabledPlugins.RemoveAll(id => !Plugins.Any(p => p.id == id));
            // 删除或改名的插件不会留下无人管理的定时提醒。
            Data.Reminders.RemoveAll(r => r.PluginId != null && r.PluginId != "pomodoro" && !Plugins.Any(p => p.id == r.PluginId && p.enabled && p.intervalMinutes > 0));
            foreach (Plugin plugin in Plugins.Where(p => p.enabled && p.intervalMinutes > 0))
            {
                Reminder reminder = Data.Reminders.Find(r => r.PluginId == plugin.id);
                if (reminder != null) { reminder.Text = plugin.text; reminder.IntervalMinutes = plugin.intervalMinutes; }
                else if (Data.Reminders.Count < 100) Data.Reminders.Add(new Reminder { Id = Guid.NewGuid().ToString("N"),
                    Text = plugin.text, Due = DateTime.UtcNow.AddMinutes(plugin.intervalMinutes), IntervalMinutes = plugin.intervalMinutes, PluginId = plugin.id });
            }
            Save();
        }

        public void TogglePlugin(string id, bool enabled)
        {
            Plugin plugin = Plugins.Find(p => p.id == id);
            if (plugin == null) throw new ArgumentException("找不到插件");
            plugin.enabled = enabled; Data.EnabledPlugins.Remove(id);
            Data.Reminders.RemoveAll(r => r.PluginId == id);
            if (enabled)
            {
                Data.EnabledPlugins.Add(id);
                if (plugin.intervalMinutes > 0) AddReminder(plugin.text, DateTime.UtcNow.AddMinutes(plugin.intervalMinutes), plugin.intervalMinutes, id);
            }
            Save();
        }

        public void RunPlugin(string id)
        {
            Plugin plugin = Plugins.Find(p => p.id == id && p.enabled);
            if (plugin == null) throw new ArgumentException("请先启用这个插件");
            Notify?.Invoke(plugin.text, plugin.motion);
        }

        public object Snapshot()
        {
            return new { reminders = Data.Reminders.Select(r => new { id = r.Id, text = r.Text, status = r.Status,
                due = r.Due.ToString("o"), repeat = r.IntervalMinutes, plugin = r.PluginId }).ToArray(),
                tasks = Data.Tasks.AsEnumerable().Reverse().Select(t => new { id = t.Id, title = t.Title, status = t.Status,
                    progress = t.Progress, detail = t.Detail }).ToArray(), plugins = Plugins };
        }

        private void Save()
        {
            string temp = _stateFile + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(Data), new UTF8Encoding(false));
            if (File.Exists(_stateFile)) File.Replace(temp, _stateFile, null); else File.Move(temp, _stateFile);
            Changed?.Invoke();
        }

        /// <summary>仅当前 Windows 用户能连接；单行事件限长并有读超时。</summary>
        private async Task Listen()
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    PipeSecurity security = new PipeSecurity();
                    security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous, 4096, 4096, security))
                    using (_stop.Token.Register(() => pipe.Dispose()))
                    {
                        await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                        Task<string> read = ReadEvent(pipe);
                        if (await Task.WhenAny(read, Task.Delay(3000, _stop.Token)).ConfigureAwait(false) != read) continue;
                        string line = await read.ConfigureAwait(false);
                        var completion = new TaskCompletionSource<string>();
                        _dispatch(() => {
                            try { ApplyEvent(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(line)); completion.SetResult("{\"ok\":true}"); }
                            catch (Exception ex) { completion.SetResult(new JavaScriptSerializer().Serialize(new { ok = false, error = ex.Message })); }
                        });
                        if (await Task.WhenAny(completion.Task, Task.Delay(3000, _stop.Token)).ConfigureAwait(false) != completion.Task) continue;
                        byte[] reply = Encoding.UTF8.GetBytes(await completion.Task.ConfigureAwait(false) + "\n");
                        await pipe.WriteAsync(reply, 0, reply.Length, _stop.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { if (_stop.IsCancellationRequested) break; }
                catch (Exception ex) { Trace.Log("本机事件接口：" + ex.Message); await Task.Delay(1000).ConfigureAwait(false); }
            }
        }

        private static async Task<string> ReadEvent(Stream pipe)
        {
            using (var bytes = new MemoryStream())
            {
                byte[] buffer = new byte[1];
                while (bytes.Length < 16384)
                {
                    if (await pipe.ReadAsync(buffer, 0, 1).ConfigureAwait(false) == 0) throw new IOException("事件未发送完整");
                    if (buffer[0] == 10) return Encoding.UTF8.GetString(bytes.ToArray());
                    bytes.WriteByte(buffer[0]);
                }
            }
            throw new InvalidDataException("事件过大");
        }

        private void ApplyEvent(Dictionary<string, object> map)
        {
            string type = Value(map, "type");
            if (type == "task") UpdateTask(Value(map, "id"), Value(map, "title"), Value(map, "status"), Number(map, "progress"), Value(map, "detail"));
            else if (type == "notify") Notify?.Invoke(Clean(Value(map, "text"), 500), ValidMotion(Value(map, "motion")));
            else if (type == "action") Notify?.Invoke("", ValidMotion(Value(map, "motion")));
            else if (type == "reminder") AddReminder(Value(map, "text"), DateTime.Parse(Value(map, "due"), null, System.Globalization.DateTimeStyles.RoundtripKind), Number(map, "intervalMinutes"));
            else throw new ArgumentException("支持 task、notify、action、reminder 事件");
        }
        private static string Value(Dictionary<string, object> map, string key) { return map != null && map.TryGetValue(key, out object value) ? Convert.ToString(value) : ""; }
        private static int Number(Dictionary<string, object> map, string key) { return int.TryParse(Value(map, key), out int value) ? value : 0; }
        private static string Clean(string text, int limit) { text = (text ?? "").Trim(); return text.Length <= limit ? text : text.Substring(0, limit); }
        private static string ValidMotion(string name) { return new[] { "idle", "blink", "wave", "cheer", "shy", "sleepy" }.Contains(name) ? name : "wave"; }
        private static string StatusText(string status)
        {
            switch (status) { case "running": return "正在进行"; case "success": return "完成啦"; case "error": return "遇到了问题";
                case "paused": return "等待重新连接"; case "cancelled": return "已取消"; default: return "等待开始"; }
        }
        public void Dispose() { _stop.Cancel(); }
    }
}
