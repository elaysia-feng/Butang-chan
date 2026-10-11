using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ButangPet
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string appRoot = AppRoot();
            // 日志要先于任何可能失败的初始化建好，否则启动期出错就什么都留不下
            Trace.Init(Path.Combine(appRoot, "data", "pet.log"));
            Trace.Log("==== 启动 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                      " appRoot=" + appRoot + " args=" + string.Join(" ", args));

            string projectRoot = ProjectRoot(appRoot);
            Trace.Log("projectRoot=" + projectRoot);

            // 单实例：桌宠同时开两个会互相盖住还都响应鼠标
            bool first;
            using (Mutex single = new Mutex(true, @"Local\ButangPetSingleton", out first))
            {
                if (!first)
                {
                    Trace.Log("已有实例在运行，本次直接退出");
                    MessageBox.Show("不糖酱已经在桌面上啦。\n（如果看不到，看一下右下角托盘）",
                        "不糖酱", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                {
                    Trace.Log("UI 线程异常: " + e.Exception);
                    try
                    {
                        MessageBox.Show("桌宠出了点问题：\n" + e.Exception.Message +
                                        "\n\n详细信息见 data\\pet.log", "不糖酱");
                    }
                    catch { }
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    Trace.Log("未捕获异常: " + (e.ExceptionObject as Exception));
                };

                try
                {
                    Config cfg = Config.Load(appRoot);
                    MemoryStore memory = new MemoryStore(Path.Combine(appRoot, "data"));
                    LlmClient llm = new LlmClient(cfg.baseUrl, cfg.apiKey, cfg.model);

                    Trace.Log("配置 OK，Key=" + (string.IsNullOrEmpty(cfg.apiKey) ? "未设置" : "已设置") +
                              " 记忆 " + memory.Count + " 条");

                    Application.Run(new PetForm(appRoot, projectRoot, cfg, memory, llm));
                }
                catch (Exception ex)
                {
                    Trace.Log("启动失败: " + ex);
                    MessageBox.Show("启动失败：\n" + ex.Message, "不糖酱",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>10_desktop_pet 自身目录（exe 所在处，不受工作目录影响）。</summary>
        private static string AppRoot()
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>
        /// 从 exe 所在目录查找原生角色资源，不再依赖上级 Live2D 项目。
        /// 写死"上一级"会随打包目录调整就失效，而用户不该为此改代码。
        /// </summary>
        private static string ProjectRoot(string appRoot)
        {
            const string MODEL = @"assets\sprites\idle.png";
            DirectoryInfo dir = new DirectoryInfo(appRoot);
            for (int i = 0; i < 5 && dir != null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, MODEL)))
                    return dir.FullName;
            }

            MessageBox.Show(
                "从 " + appRoot + " 往上找 5 层都没找到 " + MODEL + "。\n\n" +
                "请重新运行 tools\\build.ps1，补齐原生桌宠的角色素材。",
                "不糖酱", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
            return null;
        }
    }
}
