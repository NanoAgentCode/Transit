using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace RepoTransit;

public partial class App : Application
{
    private SingleInstanceGate? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            _singleInstance = SingleInstanceGate.TryAcquire(@"Global\RepoTransit.SingleInstance");
        }
        catch (UnauthorizedAccessException)
        {
            // Another Windows user already owns the machine-wide instance.
        }

        if (_singleInstance is null)
        {
            ActivateRunningWindow();
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void ActivateRunningWindow()
    {
        var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                if (process.Id == current.Id)
                    continue;

                try
                {
                    if (!string.Equals(process.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var window = process.MainWindowHandle;
                    if (window == IntPtr.Zero)
                        continue;

                    ShowWindowAsync(window, 9);
                    SetForegroundWindow(window);
                    return;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Other sessions may not expose their process details.
                }
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
