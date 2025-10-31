using System;
using System.IO;

namespace SimpleVPN.Desktop.Services
{
    public static class Logger
    {
        private static readonly string BaseDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleVPN");
        private static readonly string LogFile = Path.Combine(BaseDir, "svpn.log");

        static Logger()
        {
            try { Directory.CreateDirectory(BaseDir); } catch { }
        }

        public static string LogPath => LogFile;

        public static void Log(string message)
        {
            try
            {
                var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z  {message}";
                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
            catch { /* swallow */ }
        }

        public static void Info(string message) => Log(message);
    }
}
