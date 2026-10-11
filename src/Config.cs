using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ButangPet
{
    /// <summary>
    /// 桌宠配置。分两个文件存：
    ///   config.json   —— 可以随便改、可以进版本库的外观与功能开关
    ///   secret.json   —— 只放 API Key，不要提交到版本库
    /// 拆开的唯一理由：config.json 需要用户随时编辑，而 Key 不能跟着一起发出去。
    /// </summary>
    internal class Config
    {
        // ── 外观 ──
        public string body = "full";       // 用哪个模型：full 全身 / half 半身
        public double scale = 0.35;         // 默认小尺寸，大小面板可调整为 20%～150%
        public double opacity = 1.0;        // 0.35 ~ 1.0
        public bool topMost = true;
        public bool clickThrough = false;   // 整窗点击穿透
        public bool tightClip = true;       // 用模型 alpha 裁窗口形状
        public string lastX = null;         // 上次位置（屏幕坐标，字符串便于跨 DPI）
        public string lastY = null;

        // ── 行为 ──
        public bool wander = false;         // 默认固定在用户放置的位置，设置中可开启溜达
        public bool inputReactions = false; // 开启后响应操作时间和手柄按钮，不采集输入文字
        public bool climb = false;
        public string visionModel = "";
        public bool speakOnClick = true;    // 点击冒话
        public bool hourlyChime = false;    // 整点报时
        public int sedentaryMinutes = 45;   // 久坐提醒阈值（分钟），0 = 关
        public int pomodoroMinutes = 25;    // 番茄钟时长，0 = 关

        // ── AI ──
        public string baseUrl = "https://api.minimaxi.com/v1";
        public string model = "MiniMax-M2.1";
        public string persona = "不糖酱";
        public string systemPrompt =
            "你是「不糖酱」，一个住在用户电脑桌面上的双马尾少女。\n" +
            "性格：活泼、黏人、偶尔傲娇，会撒一点娇但更关心对方有没有好好休息。\n" +
            "说话方式：中文，短句为主（每句尽量不超过 25 字），偶尔用颜文字，\n" +
            "不要长篇大论、不要说教、不要每句都加称呼。\n" +
            "你可以关心对方在做什么、催对方休息、陪对方吐槽，但不要编造自己做不到的事。";
        public double temperature = 0.9;
        public int maxTokens = 800;

        // ── 记忆 ──
        public int recentTurns = 12;        // 每次带进上下文最近几轮
        public int summarizeAt = 40;        // 超过多少条就做一次滚动摘要

        /// <summary>API Key 单独存，永远不写回 config.json。</summary>
        public string apiKey = "";

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public string ConfigPath { get; private set; }
        public string SecretPath { get; private set; }

        public static Config Load(string root)
        {
            Config c = new Config();
            c.ConfigPath = Path.Combine(root, "config.json");
            c.SecretPath = Path.Combine(root, "secret.json");

            c.ReadFile(c.ConfigPath);
            c.ReadFile(c.SecretPath);   // secret 只认识 apiKey，读不读得到都行
            return c;
        }

        private void ReadFile(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                using (StreamReader sr = new StreamReader(path, Encoding.UTF8))
                {
                    Dictionary<string, object> map =
                        Json.Deserialize<Dictionary<string, object>>(sr.ReadToEnd());
                    if (map == null) return;
                    foreach (KeyValuePair<string, object> kv in map)
                        Apply(kv.Key, kv.Value);
                }
            }
            catch (Exception ex)
            {
                Trace.Log("配置读取失败 " + path + ": " + ex.Message);
            }
        }

        private void Apply(string key, object v)
        {
            if (key == null || v == null) return;
            switch (key)
            {
                case "body": body = S(key, v); break;
                case "scale": scale = D(key, v, scale, 0.2, 1.5); break;
                case "opacity": opacity = D(key, v, opacity, 0.3, 1.0); break;
                case "topMost": topMost = B(key, v, topMost); break;
                case "clickThrough": clickThrough = B(key, v, clickThrough); break;
                case "tightClip": tightClip = B(key, v, tightClip); break;
                case "lastX": lastX = S(key, v); break;
                case "lastY": lastY = S(key, v); break;
                case "wander": wander = B(key, v, wander); break;
                case "inputReactions": inputReactions = B(key, v, inputReactions); break;
                case "climb": climb = B(key, v, climb); break;
                case "visionModel": visionModel = S(key, v); break;
                case "speakOnClick": speakOnClick = B(key, v, speakOnClick); break;
                case "hourlyChime": hourlyChime = B(key, v, hourlyChime); break;
                case "sedentaryMinutes": sedentaryMinutes = (int)D(key, v, sedentaryMinutes, 0, 600); break;
                case "pomodoroMinutes": pomodoroMinutes = (int)D(key, v, pomodoroMinutes, 0, 180); break;
                case "baseUrl": baseUrl = S(key, v); break;
                case "model": model = S(key, v); break;
                case "persona": persona = S(key, v); break;
                case "systemPrompt": systemPrompt = S(key, v); break;
                case "temperature": temperature = D(key, v, temperature, 0, 2); break;
                case "maxTokens": maxTokens = (int)D(key, v, maxTokens, 64, 32768); break;
                case "recentTurns": recentTurns = (int)D(key, v, recentTurns, 2, 200); break;
                case "summarizeAt": summarizeAt = (int)D(key, v, summarizeAt, 8, 10000); break;
                case "apiKey": apiKey = S(key, v); break;
            }
        }

        private static double D(string key, object v, double fallback, double lo, double hi)
        {
            try
            {
                double d = Convert.ToDouble(v);
                return d < lo ? lo : (d > hi ? hi : d);
            }
            catch { Trace.Log("配置项不是数字: " + key); return fallback; }
        }

        private static bool B(string key, object v, bool fallback)
        {
            try { return Convert.ToBoolean(v); }
            catch { Trace.Log("配置项不是布尔: " + key); return fallback; }
        }

        private static string S(string key, object v)
        {
            string s = v as string;
            if (s == null) { Trace.Log("配置项不是字符串: " + key); return null; }
            return s;
        }

        public void Save()
        {
            Dictionary<string, object> map = new Dictionary<string, object>();
            map["body"] = body ?? "full";
            map["scale"] = scale;
            map["opacity"] = opacity;
            map["topMost"] = topMost;
            map["clickThrough"] = clickThrough;
            map["tightClip"] = tightClip;
            map["lastX"] = lastX ?? "";
            map["lastY"] = lastY ?? "";
            map["wander"] = wander;
            map["inputReactions"] = inputReactions;
            map["climb"] = climb; map["visionModel"] = visionModel ?? "";
            map["speakOnClick"] = speakOnClick;
            map["hourlyChime"] = hourlyChime;
            map["sedentaryMinutes"] = sedentaryMinutes;
            map["pomodoroMinutes"] = pomodoroMinutes;
            map["baseUrl"] = baseUrl ?? "";
            map["model"] = model ?? "";
            map["persona"] = persona ?? "";
            map["systemPrompt"] = systemPrompt ?? "";
            map["temperature"] = temperature;
            map["maxTokens"] = maxTokens;
            map["recentTurns"] = recentTurns;
            map["summarizeAt"] = summarizeAt;

            WriteJson(ConfigPath, map);

            if (!string.IsNullOrEmpty(apiKey))
            {
                Dictionary<string, object> s = new Dictionary<string, object>();
                s["apiKey"] = apiKey;
                WriteJson(SecretPath, s);
            }
        }

        private static void WriteJson(string path, Dictionary<string, object> map)
        {
            try
            {
                // 先写临时文件再替换：断电或崩溃时不会留下半截 JSON，下次启动读不出来
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(map), Utf8);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Trace.Log("配置写入失败 " + path + ": " + ex.Message);
            }
        }

        public Dictionary<string, object> ToMapForPage()
        {
            Dictionary<string, object> map = new Dictionary<string, object>();
            map["scale"] = scale;
            map["body"] = body ?? "full";
            map["opacity"] = opacity;
            map["topMost"] = topMost;
            map["clickThrough"] = clickThrough;
            map["tightClip"] = tightClip;
            map["wander"] = wander;
            map["inputReactions"] = inputReactions;
            map["climb"] = climb; map["visionModel"] = visionModel ?? "";
            map["speakOnClick"] = speakOnClick;
            map["hourlyChime"] = hourlyChime;
            map["sedentaryMinutes"] = sedentaryMinutes;
            map["pomodoroMinutes"] = pomodoroMinutes;
            map["persona"] = persona ?? "";
            map["model"] = model ?? "";
            map["baseUrl"] = baseUrl ?? "";
            map["hasKey"] = !string.IsNullOrEmpty(apiKey);
            return map;
        }

        public void UpdateFromPage(Dictionary<string, object> map)
        {
            if (map == null) return;
            foreach (KeyValuePair<string, object> kv in map) Apply(kv.Key, kv.Value);
        }
    }
}
