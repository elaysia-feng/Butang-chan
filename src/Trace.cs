using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ButangPet
{
    /// <summary>
    /// 极简日志。桌宠是常驻程序，出问题时用户不会去翻控制台，
    /// 所以写文件而不是 Console，并且自己控制大小。
    /// </summary>
    internal static class Trace
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object Gate = new object();
        private static string _path;

        public static void Init(string path)
        {
            _path = path;
            try
            {
                // 必须先建目录：目录不存在时 AppendAllText 抛异常，而这里 catch 掉，
                // 结果就是"程序跑着但一条日志都没有"，比不写还难查。
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                Roll();
                // 立刻写一条：证明日志通道本身是通的
                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 日志通道就绪" + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                _path = null;
                Console.Error.WriteLine("日志初始化失败：" + ex.Message);
            }
        }

        public static void Log(string message)
        {
            string path = _path;
            if (path == null) return;
            try
            {
                lock (Gate)
                {
                    if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) Roll();
                    File.AppendAllText(path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }

        private static void Roll()
        {
            if (_path == null || !File.Exists(_path)) return;
            try { File.Delete(_path + ".1"); } catch { }
            try { File.Move(_path, _path + ".1"); } catch { }
        }
    }
}
