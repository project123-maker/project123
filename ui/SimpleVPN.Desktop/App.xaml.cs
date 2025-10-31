using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows; // WPF only
using SimpleVPN.Desktop; // your namespace

namespace SimpleVPN.Desktop
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
                {
                    Logger.Log("UNHANDLED: " + ex.ExceptionObject?.ToString());
                    System.Windows.MessageBox.Show("App crashed. See log:\n" + Logger.LogPath, "SimpleVPN",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                };

                DispatcherUnhandledException += (s, ex) =>
                {
                    Logger.Log("UI UNHANDLED: " + ex.Exception);
                    System.Windows.MessageBox.Show("UI error. See log:\n" + Logger.LogPath, "SimpleVPN",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    ex.Handled = true;
                };

                Logger.Log("==== SimpleVPN starting ====");

                if (!IsElevated())
                {
                    Logger.Log("Not elevated → relaunching with runas");
                    try
                    {
                        var exe = Process.GetCurrentProcess().MainModule!.FileName!;
                        var psi = new ProcessStartInfo(exe) { Verb = "runas", UseShellExecute = true };
                        Process.Start(psi);
                    }
                    catch (Exception rex)
                    {
                        Logger.Log("runas failed: " + rex.Message);
                        System.Windows.MessageBox.Show("Need Administrator permission to create VPN adapter.\n" + Logger.LogPath,
                            "SimpleVPN", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    Current.Shutdown();
                    return;
                }

                Logger.Log("Elevated OK");
            }
            catch (Exception boot)
            {
                Logger.Log("Startup exception: " + boot);
                System.Windows.MessageBox.Show("Startup failed. See log:\n" + Logger.LogPath, "SimpleVPN",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Current.Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        static bool IsElevated()
        {
            using var id = WindowsIdentity.GetCurrent();
            var p = new WindowsPrincipal(id);
            return p.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
