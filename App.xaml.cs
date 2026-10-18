using System.Windows;

namespace OneClickConverter;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var startInTray = e.Args.Any(argument => string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(startInTray);
        MainWindow = window;
        window.Show();
    }
}
