using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ButangPet
{
    /// <summary>
    /// OpenAI 兼容的对话客户端。桌宠不直接调 API，原因是渲染进程里放 API Key
    /// 不安全（C 端页面任何注入都能读到），所以请求统一由宿主发起。
    ///
    /// 只依赖 /chat/completions 这一条最通用的接口，MiniMax / DeepSeek / Moonshot /
    /// 硅基流动 / 本地 Ollama 都能用，区别只在 baseUrl 和 model。
    /// </summary>
    internal class LlmClient
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static HttpClient _http;

        public string BaseUrl { get; private set; }
        public string ApiKey { get; private set; }
        public string Model { get; private set; }

        public LlmClient(string baseUrl, string apiKey, string model)
        {
            BaseUrl = (baseUrl ?? "").TrimEnd('/');
            ApiKey = apiKey ?? "";
            Model = model ?? "";
        }

        /// <summary>设置改了以后就地更新，不用重建整个客户端。</summary>
        public void Update(string baseUrl, string apiKey, string model)
        {
            BaseUrl = (baseUrl ?? "").TrimEnd('/');
            ApiKey = apiKey ?? "";
            Model = model ?? "";
        }

        public bool Ready
        {
            get { return BaseUrl.Length > 0 && Model.Length > 0 && (ApiKey.Length > 0 || (Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri uri) && uri.IsLoopback)); }
        }

        private static HttpClient Http
        {
            get
            {
                if (_http == null)
                {
                    // 桌宠要长连接跑一整天，连接池别太短
                    _http = new HttpClient(new HttpClientHandler
                    {
                        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                    });
                    _http.Timeout = TimeSpan.FromMinutes(5);
                    _http.DefaultRequestHeaders.ExpectContinue = false;
                }
                return _http;
            }
        }

        public class Result
        {
            public bool Ok;
            public string Text;
            public string Error;
        }

        /// <summary>
        /// 流式对话。每收到一段就调 onDelta，结束时 onDone。
        /// 全程在后台线程池跑，UI 线程只负责把结果推给页面。
        /// </summary>
        public async Task<Result> ChatAsync(
            string systemPrompt,
            List<ChatMessage> history,
            string userText,
            Action<string> onDelta,
            Action<Result> onDone,
            CancellationToken token,
            string imageData = null)
        {
            Result result = new Result();
            if (!Ready)
            {
                result.Error = "还没配置 API：右键菜单 →「AI 设置」填一下地址和 Key。";
                onDone(result);
                return result;
            }

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                List<Dictionary<string, object>> messages = new List<Dictionary<string, object>>();
                if (!string.IsNullOrEmpty(systemPrompt))
                    messages.Add(new Dictionary<string, object> { { "role", "system" }, { "content", systemPrompt } });
                foreach (ChatMessage m in history)
                    messages.Add(new Dictionary<string, object> { { "role", m.role }, { "content", m.content } });
                object content = userText;
                if (imageData != null)
                    content = new object[] { new { type = "text", text = userText }, new { type = "image_url", image_url = new { url = imageData } } };
                messages.Add(new Dictionary<string, object> { { "role", "user" }, { "content", content } });

                Dictionary<string, object> body = new Dictionary<string, object>();
                body["model"] = Model;
                body["messages"] = messages;
                body["stream"] = true;
                body["temperature"] = 0.9;
                body["max_tokens"] = 800;

                string url = BaseUrl + "/chat/completions";
                Trace.Log("→ " + url + " model=" + Model + " history=" + messages.Count);

                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    if (ApiKey.Length > 0) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
                    req.Content = new StringContent(new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Serialize(body), Encoding.UTF8, "application/json");

                    using (HttpResponseMessage resp = await Http
                        .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            string err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            result.Error = Explain((int)resp.StatusCode, err);
                            Trace.Log("← HTTP " + (int)resp.StatusCode + " " + Trunc(err, 400));
                            onDone(result);
                            return result;
                        }

                        using (Stream stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (token.Register(() => stream.Dispose()))
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            StringBuilder all = new StringBuilder();
                            string line;
                            while (!token.IsCancellationRequested &&
                                   (line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                            {
                                if (line.Length == 0) continue;
                                if (!line.StartsWith("data:")) continue;
                                string payload = line.Substring(5).Trim();
                                if (payload == "[DONE]") break;

                                string delta = ExtractDelta(payload);
                                if (delta.Length == 0) continue;
                                all.Append(delta);
                                if (onDelta != null) onDelta(delta);
                            }
                            result.Text = all.ToString();
                        }
                    }
                }

                result.Ok = !string.IsNullOrWhiteSpace(result.Text);
                if (!result.Ok) result.Error = "接口没有返回可显示的文本。";
                Trace.Log("← 完成 " + result.Text.Length + " 字");
            }
            catch (OperationCanceledException)
            {
                result.Error = "（已取消）";
            }
            catch (Exception ex)
            {
                result.Error = token.IsCancellationRequested ? "（已取消）" : "连接失败：" + ex.Message;
                Trace.Log(token.IsCancellationRequested ? "← 用户打断了当前回复" : "← 异常 " + ex);
            }

            onDone(result);
            return result;
        }

        /// <summary>做一次滚动摘要。只用一条 system + 拼接文本，返回摘要正文。</summary>
        public async Task<Result> SummarizeAsync(string systemPrompt, List<ChatMessage> slice,
                                                  CancellationToken token)
        {
            Result result = new Result();
            if (!Ready) { result.Error = "未配置"; return result; }

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                StringBuilder sb = new StringBuilder();
                foreach (ChatMessage m in slice)
                    sb.Append(m.role == "user" ? "用户：" : "不糖酱：").Append(m.content).Append('\n');

                List<Dictionary<string, object>> messages = new List<Dictionary<string, object>>();
                messages.Add(new Dictionary<string, object> { { "role", "system" }, { "content", systemPrompt } });
                messages.Add(new Dictionary<string, object> { { "role", "user" }, { "content", sb.ToString() } });

                Dictionary<string, object> body = new Dictionary<string, object>();
                body["model"] = Model;
                body["messages"] = messages;
                body["stream"] = false;
                body["temperature"] = 0.3;
                body["max_tokens"] = 600;

                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/chat/completions"))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
                    req.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");

                    using (HttpResponseMessage resp = await Http.SendAsync(req, token).ConfigureAwait(false))
                    {
                        string raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                        {
                            result.Error = Explain((int)resp.StatusCode, raw);
                            return result;
                        }
                        result.Text = ExtractFull(raw);
                        result.Ok = true;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            return result;
        }

        // ── SSE 解析 ──
        private static string ExtractDelta(string payload)
        {
            try
            {
                Dictionary<string, object> obj = Json.Deserialize<Dictionary<string, object>>(payload);
                if (obj == null) return "";
                // JavaScriptSerializer 的嵌套数组可能是 ArrayList，按 IList 读取。
                if (!obj.TryGetValue("choices", out object choices)) return "";
                System.Collections.IList arr = choices as System.Collections.IList;
                if (arr == null || arr.Count == 0) return "";
                if (!(arr[0] is Dictionary<string, object> first)) return "";
                first.TryGetValue("delta", out object delta);
                if (delta is Dictionary<string, object> d && d.ContainsKey("content"))
                {
                    object c = d["content"];
                    return c == null ? "" : Convert.ToString(c);
                }
                // 少数实现把内容放在 message 而不是 delta 里
                first.TryGetValue("message", out object msg);
                if (msg is Dictionary<string, object> m && m.ContainsKey("content"))
                {
                    object c = m["content"];
                    return c == null ? "" : Convert.ToString(c);
                }
                return "";
            }
            catch { return ""; }
        }

        private static string ExtractFull(string raw)
        {
            try
            {
                Dictionary<string, object> obj = Json.Deserialize<Dictionary<string, object>>(raw);
                if (obj == null) return "";
                object choices = obj["choices"];
                if (!(choices is object[] arr) || arr.Length == 0) return "";
                if (!(arr[0] is Dictionary<string, object> first)) return "";
                object msg = first["message"];
                if (msg is Dictionary<string, object> m && m.ContainsKey("content"))
                    return Convert.ToString(m["content"]);
                return "";
            }
            catch (Exception ex)
            {
                Trace.Log("完整响应解析失败: " + ex.Message + " raw=" + Trunc(raw, 300));
                return "";
            }
        }

        /// <summary>把 HTTP 错误翻译成人能看懂的话。直接把服务端原文丢给用户没有意义。</summary>
        private static string Explain(int status, string body)
        {
            string detail = ExtractErrorMessage(body);
            switch (status)
            {
                case 401:
                case 403:
                    return "API Key 被拒（HTTP " + status + "）。检查 secret.json 里的 key 有没有写对。";
                case 404:
                    return "接口地址不对（HTTP 404）：" + detail + "\nbaseUrl 一般要填到 /v1 这一层。";
                case 429:
                    return "被限流了（HTTP 429），等一会儿再说。";
                case 500:
                case 502:
                case 503:
                    return "服务端出错（HTTP " + status + "）：" + detail;
                default:
                    return "HTTP " + status + "：" + detail;
            }
        }

        private static string ExtractErrorMessage(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            try
            {
                Dictionary<string, object> obj = Json.Deserialize<Dictionary<string, object>>(body);
                if (obj != null && obj["error"] is Dictionary<string, object> e && e.ContainsKey("message"))
                    return Trunc(Convert.ToString(e["message"]), 200);
                if (obj != null && obj.ContainsKey("message"))
                    return Trunc(Convert.ToString(obj["message"]), 200);
            }
            catch { }
            return Trunc(body.Replace('\n', ' '), 200);
        }

        private static string Trunc(string s, int n)
        {
            if (s == null) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }
    }
}
