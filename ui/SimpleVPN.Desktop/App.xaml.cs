using System;
using System.Windows;
using SimpleVPN.Desktop.Services;

namespace SimpleVPN.Desktop
{
    public partial class App : Application
    {
        private void App_OnStartup(object sender, StartupEventArgs e)
        {
            try
            {
                Logger.Log("App_OnStartup()");
                var w = new Views.MainWindow();
                w.Show();
                Logger.Log("MainWindow shown.");
            }
            catch (Exception ex)
            {
                Logger.Log("Startup failed: " + ex);
                MessageBox.Show(ex.ToString(), "Startup failed");
            }
        }
    }
}
