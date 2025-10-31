using System;
using System.IO;

namespace SimpleVPN.Desktop
{
    public static class DeviceId
    {
        public static string LoadOrCreate()
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "bin", "sing-box");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "device.id");

            if (File.Exists(path)) return File.ReadAllText(path).Trim();
            var id = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, id);
            return id;
        }
    }
}
