using System;
using System.IO;

namespace SimpleVPN.Desktop
{
    internal static class Logger
    {
        internal static string LogPath { get; private set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "SimpleVPN", "svpn.log");

        public static void Initialize(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                LogPath = path;
            }
            catch
            {
                // fallback remains default LogPath
            }
        }

        public static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}");
            }
            catch
            {
                // never throw
            }
        }
    }
}
