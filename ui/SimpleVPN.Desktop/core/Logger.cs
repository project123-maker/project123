using System;
using System.Diagnostics;
using System.IO;

namespace SimpleVPN.Desktop
{
    public static class Logger
    {
        private static readonly string LogPath =
            Path.Combine(AppContext.BaseDirectory, "svpn.log");

        public static void Info(string msg) => Write("INFO", msg);
        public static void Warn(string msg) => Write("WARN", msg);
        public static void Error(string msg) => Write("ERROR", msg);

        private static void Write(string level, string msg)
        {
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {level} {msg}";
            Debug.WriteLine(line);
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
    }
}
