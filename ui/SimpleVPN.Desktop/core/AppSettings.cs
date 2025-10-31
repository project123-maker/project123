using System.IO;
using System.Text.Json;
using System;


namespace SimpleVPN.Desktop
{
    public sealed class AppSettings
    {
        public string ProjectId { get; set; } = "";
        public string CodesCollection { get; set; } = "codes";
        public string Platform { get; set; } = "windows";
        public string SingBoxDir { get; set; } = @".\bin\sing-box";   // contains sing-box.exe & wintun.dll
        public string ConfigPath => Path.Combine(SingBoxDir, "config.json");

        public static AppSettings Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings()
                : new AppSettings();
        }
    }
}
