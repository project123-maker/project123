using System;
using System.IO;

namespace SimpleVPN.Desktop
{
    public static class Logger
    {
        static readonly string Dir  = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleVPN");
        static readonly string PathLog = System.IO.Path.Combine(Dir, "app.log");
        static readonly object Gate = new();

        static Logger()
        {
            try { Directory.CreateDirectory(Dir); } catch { }
            try
            {
                // rotate if huge
                if (File.Exists(PathLog) && new FileInfo(PathLog).Length > 2_000_000)
                {
                    File.Move(PathLog, System.IO.Path.Combine(Dir, $"app-{DateTime.Now:yyyyMMdd-HHmmss}.log"), true);
                }
            } catch { }
        }

        public static void Log(string msg)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(PathLog, $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
                }
            }
            catch { }
        }

        public static string LogPath => PathLog;
    }
}
