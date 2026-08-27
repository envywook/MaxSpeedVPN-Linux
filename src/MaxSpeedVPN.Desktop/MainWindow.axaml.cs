using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MaxSpeedVPN.Core;

namespace MaxSpeedVPN.Desktop;

public partial class MainWindow : Window
{
    private readonly ConnectionController _controller;
    private readonly SingBoxRuntime _runtime;
    private readonly string _enginePath;
    private VpnProfile? _profile;
    private bool _shutdownStarted;

    public MainWindow()
    {
        InitializeComponent();
        _enginePath = ResolveEnginePath();
        _runtime = CreateRuntime(_enginePath);
        _controller = new ConnectionController(_runtime);
        _controller.StateChanged += state => Avalonia.Threading.Dispatcher.UIThread.Post(() => RenderState(state));
        Closing += OnClosing;
        RenderEngineStatus();
    }

    private static string ResolveEnginePath()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "bin", "sing-box");
        return File.Exists(bundled) ? bundled : "/usr/bin/sing-box";
    }

    private static SingBoxRuntime CreateRuntime(string executable)
    {
        var runtimeDirectory = Path.Combine(AppPaths.DataDirectory(), "runtime");
        return new SingBoxRuntime(executable, runtimeDirectory, new SingBoxConfigWriter());
    }

    private void RenderEngineStatus()
    {
        var available = File.Exists(_enginePath);
        EngineStatusText.Text = available ? "Движок доступен" : "Движок не найден";
        EngineStatusText.Foreground = Brush.Parse(available ? "#A9EBC9" : "#E8A7A7");
        EngineDot.Fill = Brush.Parse(available ? "#37D17E" : "#D75A5A");
        UpdateConnectAvailability();
        if (!available)
            EventText.Text = "sing-box не найден. Переустановите полный пакет MaxSpeedVPN.";
    }

    private async void Connect_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_controller.State == ConnectionState.Connected)
                await _controller.DisconnectAsync();
            else if (_profile is not null)
                await _controller.ConnectAsync(_profile);
        }
        catch (Exception exception)
        {
            EventText.Text = exception is FileNotFoundException
                ? "sing-box не найден. Переустановите полный пакет MaxSpeedVPN."
                : $"Ошибка запуска: {exception.Message}";
        }
    }

    private void ImportProfile_Click(object? sender, RoutedEventArgs e)
    {
        ImportErrorText.IsVisible = false;
        ImportOverlay.IsVisible = true;
        ProfileUriTextBox.Focus();
    }

    private void CancelImport_Click(object? sender, RoutedEventArgs e)
    {
        ProfileUriTextBox.Text = string.Empty;
        ImportOverlay.IsVisible = false;
    }

    private void ConfirmImport_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            _profile = new ProfileParser().Parse(ProfileUriTextBox.Text?.Trim() ?? string.Empty);
            ProfileNameText.Text = _profile.Name;
            ProfileEndpointText.Text = $"{_profile.Host}:{_profile.Port} · Reality TCP";
            EventText.Text = "Профиль принят только для текущего запуска. Нажмите подключить и настройте приложение на 127.0.0.1:10808.";
            ProfileUriTextBox.Text = string.Empty;
            ImportOverlay.IsVisible = false;
            UpdateConnectAvailability();
        }
        catch (FormatException exception)
        {
            ImportErrorText.Text = exception.Message;
            ImportErrorText.IsVisible = true;
        }
    }

    private void RenderState(ConnectionState state)
    {
        StatusText.Text = state switch
        {
            ConnectionState.Preparing => "Подготовка",
            ConnectionState.Connecting => "Запуск прокси…",
            ConnectionState.Connected => "Локальный прокси активен",
            ConnectionState.Disconnecting => "Отключение…",
            ConnectionState.Error => "Ошибка движка",
            _ => "Не подключено"
        };
        ConnectHint.Text = state == ConnectionState.Connected ? "Остановить прокси" : "Запустить локальный прокси";
        UpdateConnectAvailability();
        EventText.Text = state switch
        {
            ConnectionState.Connected => "Локальный SOCKS/HTTP прокси слушает 127.0.0.1:10808. Системный трафик автоматически не перенаправляется.",
            ConnectionState.Disconnected => "Локальный прокси остановлен, дочерний процесс завершён.",
            ConnectionState.Error => _controller.ErrorMessage ?? "sing-box неожиданно остановился.",
            _ => "Запускаем внешний sing-box и проверяем локальный listener…"
        };
    }

    private void UpdateConnectAvailability()
    {
        var busy = _controller.State is ConnectionState.Preparing or ConnectionState.Connecting or ConnectionState.Disconnecting;
        ConnectButton.IsEnabled = !busy && File.Exists(_enginePath) && (_profile is not null || _controller.State == ConnectionState.Connected);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdownStarted) return;
        e.Cancel = true;
        _shutdownStarted = true;
        try { await _runtime.DisposeAsync(); }
        finally { Close(); }
    }
}
