using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop
{
    public sealed class SingBox
    {
        Process _p;
        public event Action<string> OutputReceived;

        public async Task<bool> StartAsync(CancellationToken ct)
        {
            try
            {
                var root = Path.Combine(AppContext.BaseDirectory, "sing-box");
                var exe  = Path.Combine(root, "sing-box.exe");
                var cfg  = Path.Combine(root, "config.json");
                if (!File.Exists(exe) || !File.Exists(cfg)) { Output("sing-box assets missing"); return false; }

                if (!IsElevated()) { Output("Admin required to create Wintun adapter."); return false; }

                Stop(); // clean start

                var psi = new ProcessStartInfo(exe, "run -c config.json")
                {
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                psi.Environment["ENABLE_DEPRECATED_SPECIAL_OUTBOUNDS"] = "true";

                _p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _p.OutputDataReceived += (_, e) => { if (e.Data != null) Output(e.Data); };
                _p.ErrorDataReceived  += (_, e) => { if (e.Data != null) Output("[err] " + e.Data); };
                _p.Exited += (_, __) => Output("sing-box exited.");

                if (!_p.Start()) { Output("failed to start process"); return false; }
                _p.BeginOutputReadLine();
                _p.BeginErrorReadLine();

                // Wait a bit; if it dies, fail.
                var ok = await WaitHealthy(4000, ct);
                if (!ok) { Stop(); return false; }
                Output("sing-box running.");
                return true;
            }
            catch (Exception ex)
            {
                Output("start exception: " + ex.Message);
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            try
            {
                if (_p == null) return;
                if (!_p.HasExited) { _p.Kill(true); _p.WaitForExit(1000); }
            }
            catch { }
            finally { _p = null; }
        }

        static bool IsElevated()
        {
            using var id = WindowsIdentity.GetCurrent();
            var p = new WindowsPrincipal(id);
            return p.IsInRole(WindowsBuiltInRole.Administrator);
        }

        async Task<bool> WaitHealthy(int ms, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (_p == null || _p.HasExited) return false;
                await Task.Delay(150, ct);
            }
            return true;
        }

        void Output(string s) => OutputReceived?.Invoke(s);
    }
}

