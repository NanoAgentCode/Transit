using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace RepoTransit;

public partial class App : System.Windows.Application
{
    private const string InstanceName = @"Global\RepoTransit.SingleInstance";
    private SingleInstanceGate? _singleInstance;
    private IDisposable? _activationSubscription;
    private NotifyIcon? _trayIcon;
    private Icon? _icon;
    private bool _exitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        try { _singleInstance = SingleInstanceGate.TryAcquire(InstanceName); }
        catch (UnauthorizedAccessException) { }

        if (_singleInstance is null)
        {
            for (var attempt = 0; attempt < 10 && !SingleInstanceGate.SignalExisting(InstanceName); attempt++)
                Thread.Sleep(100);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        MainWindow = new MainWindow();
        MainWindow.Closing += MainWindow_Closing;
        MainWindow.StateChanged += MainWindow_StateChanged;
        CreateTrayIcon();
        _activationSubscription = _singleInstance.OnActivation(() => Dispatcher.BeginInvoke(ShowMainWindow));
        MainWindow.Show();
    }

    private void CreateTrayIcon()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/RepoTransit.ico"))?.Stream
            ?? throw new InvalidOperationException("应用图标资源不存在。");
        _icon = new Icon(stream);
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开仓渡", null, (_, _) => Dispatcher.BeginInvoke(ShowMainWindow));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.BeginInvoke(ExitApplication));
        _trayIcon = new NotifyIcon { Icon = _icon, Text = "仓渡 RepoTransit", ContextMenuStrip = menu, Visible = true };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowMainWindow);
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (MainWindow.WindowState == WindowState.Minimized)
            HideMainWindow();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested) return;
        e.Cancel = true;
        HideMainWindow();
    }

    private void HideMainWindow()
    {
        MainWindow.Hide();
        MainWindow.ShowInTaskbar = false;
    }

    private void ShowMainWindow()
    {
        if (_exitRequested || MainWindow is null) return;
        MainWindow.ShowInTaskbar = true;
        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        MainWindow.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationSubscription?.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
        }
        _icon?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
