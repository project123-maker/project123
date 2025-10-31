using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SimpleVPN.Desktop
{
    public static class LocalState
    {
        private static readonly string StateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleVPN.Desktop");
        private static readonly string CodeFile = Path.Combine(StateDir, "last.code.txt");

        public static void SaveLastCode(string code)
        {
            Directory.CreateDirectory(StateDir);
            File.WriteAllText(CodeFile, code ?? "");
        }
        public static string? TryLoadLastCode() => File.Exists(CodeFile) ? File.ReadAllText(CodeFile).Trim() : null;

        public static string DeviceId()
        {
            try
            {
                var guid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null)?.ToString();
                if (!string.IsNullOrWhiteSpace(guid)) return "win-" + guid.ToLowerInvariant();
            }
            catch { /* ignore */ }
            // fallback stable id
            using var sha = SHA256.Create();
            var raw = $"{Environment.MachineName}|{Environment.UserName}|{Environment.OSVersion}";
            return "win-" + Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()[..16];
        }
    }
}
