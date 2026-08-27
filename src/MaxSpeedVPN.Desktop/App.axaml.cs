using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MaxSpeedVPN.Desktop;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private bool _exitRequested;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void Tray_Clicked(object? sender, EventArgs e) => _mainWindow?.ShowFromTray();
    private void TrayOpen_Click(object? sender, EventArgs e) => _mainWindow?.ShowFromTray();

    public void DisposeTrayIcon() => TrayIcon.SetIcons(this, null);

    private async void TrayExit_Click(object? sender, EventArgs e)
    {
        if (_exitRequested) return;
        _exitRequested = true;
        if (_mainWindow is not null) await _mainWindow.ShutdownAsync();
        DisposeTrayIcon();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
