using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop
{
    public sealed class SingBoxService
    {
        private readonly AppSettings _settings;
        private Process? _proc;

        public SingBoxService(AppSettings settings) => _settings = settings;

        public async Task StartAsync(string vless, CancellationToken ct)
        {
            Directory.CreateDirectory(_settings.SingBoxDir);
            var cfgJson = BuildConfigFromVless(vless);
            await File.WriteAllTextAsync(_settings.ConfigPath, cfgJson, ct);

            // Kill leftovers
            await StopAsync();

            var exe = Path.Combine(_settings.SingBoxDir, "sing-box.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("sing-box.exe not found", exe);

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"run -c \"{_settings.ConfigPath}\"",
                WorkingDirectory = _settings.SingBoxDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            _proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start sing-box.");
            await Task.Delay(300, ct);
        }

        public async Task StopAsync()
        {
            try
            {
                if (_proc is { HasExited: false })
                {
                    _proc.Kill(true);
                    _proc.Dispose();
                }
            }
            catch { /* ignore */ }
            finally { _proc = null; }
            await Task.CompletedTask;
        }

        private static string BuildConfigFromVless(string vless)
        {
            // Parse VLESS URI like: vless://UUID@host:port?...&sni=...&pbk=...&sid=...&flow=...&fp=chrome
            var u = new Uri(vless);
            var uuid = u.UserInfo;
            var host = u.Host;
            var port = u.Port;
            var qp = System.Web.HttpUtility.ParseQueryString(u.Query);

            // DNS goes through the proxy so it still works under blocks
            var dns = new JsonObject
            {
                ["servers"] = new JsonArray
                {
                    new JsonObject { ["tag"]="cf", ["address"]="https://1.1.1.1/dns-query", ["strategy"]="ipv4_only", ["detour"]="proxy" },
                    new JsonObject { ["tag"]="gg", ["address"]="https://dns.google/dns-query", ["strategy"]="ipv4_only", ["detour"]="proxy" }
                },
                ["strategy"] = "ipv4_only",
                ["disable_cache"] = true
            };

            var tls = new JsonObject
            {
                ["enabled"] = true,
                ["server_name"] = qp["sni"] ?? "",
                ["reality"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["public_key"] = qp["pbk"] ?? "",
                    ["short_id"] = qp["sid"] ?? ""
                }
            };
            var fp = qp["fp"]; if (!string.IsNullOrWhiteSpace(fp))
                tls["utls"] = new JsonObject { ["enabled"] = true, ["fingerprint"] = fp };

            var cfg = new JsonObject
            {
                ["log"] = new JsonObject { ["level"] = "info", ["timestamp"] = true },
                ["dns"] = dns,
                ["inbounds"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"]="tun","tag"]="tun-in","interface_name"]="SimpleVPN",
                        ["address"]= new JsonArray { "172.19.0.1/30" },
                        ["mtu"]=1400, ["stack"]="gvisor", ["auto_route"]=true, ["strict_route"]=true, ["sniff"]=true
                    }
                },
                ["outbounds"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"]="vless","tag"]="proxy",
                        ["server"]=host, ["server_port"]=port, ["uuid"]=uuid,
                        ["flow"]= qp["flow"] ?? "",
                        ["tls"]= tls
                    },
                    new JsonObject { ["type"]="direct","tag"]="direct" },
                    new JsonObject { ["type"]="block","tag"]="block" }
                },
                ["route"] = new JsonObject
                {
                    ["auto_detect_interface"] = true,
                    ["default_domain_resolver"] = "cf",
                    ["final"] = "proxy",
                    ["rules"] = new JsonArray
                    {
                        new JsonObject { ["protocol"]= new JsonArray { "dns" }, ["outbound"]="proxy" }, // send DoH via proxy
                        new JsonObject { ["ip_is_private"]=true, ["outbound"]="direct" }               // keep LAN direct
                    }
                }
            };

            return cfg.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
