using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MaxSpeedVPN.Core;

namespace MaxSpeedVPN.Desktop;

public partial class MainWindow : Window
{
    private ConnectionController? _controller;
    private IAsyncDisposable? _activeRuntime;
    private readonly ProfileStore _profileStore;
    private readonly LiveLatencyMonitor _latencyMonitor;
    private readonly string _enginePath;
    private readonly string _mieruPath;
    private readonly ObservableCollection<ServerRow> _servers = [];
    private IReadOnlyList<StoredProfile> _profiles = [];
    private StoredProfile? _selected;
    private bool _shutdownStarted;

    public MainWindow()
    {
        InitializeComponent();
        _enginePath = ResolveBundled("sing-box");
        _mieruPath = ResolveBundled("mieru");
        _profileStore = new ProfileStore(Path.Combine(AppPaths.DataDirectory(), "profiles"));
        _latencyMonitor = new LiveLatencyMonitor(new TcpLatencyProbe(), TimeSpan.FromSeconds(5));
        _latencyMonitor.Updated += snapshot => Avalonia.Threading.Dispatcher.UIThread.Post(() => RenderLatencies(snapshot));
        ServersList.ItemsSource = _servers;
        Opened += OnOpened;
        Closing += OnClosing;
        RenderEngineStatus();
    }

    private static string ResolveBundled(string name)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "bin", name);
        if (File.Exists(bundled)) return bundled;
        return name == "sing-box" ? "/usr/bin/sing-box" : "/usr/bin/mieru";
    }


    private async void OnOpened(object? sender, EventArgs e)
    {
        await ReloadProfilesAsync();
        await _latencyMonitor.StartAsync(() => _profiles);
        LivePingToggle.IsChecked = true;
    }

    private void RenderEngineStatus()
    {
        var singBox = File.Exists(_enginePath);
        var mieru = File.Exists(_mieruPath);
        EngineStatusText.Text = singBox ? (mieru ? "sing-box готов · Mieru установлен" : "sing-box готов") : "sing-box не найден";
        EngineStatusText.Foreground = Brush.Parse(singBox ? "#A9EBC9" : "#FF9AAB");
        EngineDot.Fill = Brush.Parse(singBox ? "#42D791" : "#FF6378");
        UpdateConnectAvailability();
    }

    private async Task ReloadProfilesAsync(string? selectId = null)
    {
        _profiles = await _profileStore.LoadAsync();
        var current = selectId ?? _selected?.Id;
        _servers.Clear();
        foreach (var profile in _profiles) _servers.Add(ServerRow.From(profile));
        ServerCountText.Text = $"{_profiles.Count} сохранено";
        if (_profiles.Count == 0)
        {
            _selected = null;
            SelectedProtocolText.Text = "—";
            SelectedLatencyText.Text = "—";
            return;
        }
        var index = Math.Max(0, _profiles.ToList().FindIndex(profile => profile.Id == current));
        ServersList.SelectedIndex = index;
    }

    private async void Connect_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_controller?.State == ConnectionState.Connected)
            {
                await _controller.DisconnectAsync();
                return;
            }
            if (_selected is null) return;
            if (_selected.Protocol == "mieru")
            {
                EventText.Text = "Mieru импортирован, но нативный runtime в этом alpha пока не запускается: нужен отдельный lifecycle smoke.";
                return;
            }
            if (_activeRuntime is not null) await _activeRuntime.DisposeAsync();
            var runtime = new StoredSingBoxRuntime(_enginePath, Path.Combine(AppPaths.DataDirectory(), "runtime"), _selected);
            _activeRuntime = runtime;
            _controller = new ConnectionController(runtime);
            _controller.StateChanged += state => Avalonia.Threading.Dispatcher.UIThread.Post(() => RenderState(state));
            await runtime.StartAsync();
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

    private async void ConfirmImport_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var profile = new ProfileParser().ParseStored(ProfileUriTextBox.Text?.Trim() ?? string.Empty);
            await _profileStore.UpsertAsync(profile);
            await ReloadProfilesAsync(profile.Id);
            EventText.Text = $"{profile.Name} сохранён как {profile.ProtocolLabel}, без ярлыка Custom.";
            ProfileUriTextBox.Text = string.Empty;
            ImportOverlay.IsVisible = false;
            await PingAllAsync();
        }
        catch (Exception exception) when (exception is FormatException or IOException)
        {
            ImportErrorText.Text = exception.Message;
            ImportErrorText.IsVisible = true;
        }
    }

    private void ServersList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ServersList.SelectedIndex < 0 || ServersList.SelectedIndex >= _profiles.Count) return;
        _selected = _profiles[ServersList.SelectedIndex];
        SelectedProtocolText.Text = _selected.ProtocolLabel;
        ModeBadgeText.Text = _selected.Protocol == "mieru" ? "NATIVE CORE" : "LOCAL PROXY";
        ConnectHint.Text = _selected.Protocol switch
        {
            "vless" or "naive" => "Запустить локальный SOCKS/HTTP proxy",
            "mieru" => "Mieru connect появится после lifecycle smoke",
            _ => "Протокол недоступен"
        };
        var row = _servers.FirstOrDefault(item => item.Id == _selected.Id);
        SelectedLatencyText.Text = row?.LatencyText ?? "—";
        UpdateConnectAvailability();
    }

    private async void PingAll_Click(object? sender, RoutedEventArgs e) => await PingAllAsync();

    private async Task PingAllAsync()
    {
        PingAllButton.IsEnabled = false;
        try
        {
            EventText.Text = "Проверяем TCP-доступность всех endpoint…";
            await _latencyMonitor.RefreshAsync(_profiles);
            EventText.Text = "TCP-проверка всех endpoint обновлена.";
        }
        finally { PingAllButton.IsEnabled = true; }
    }

    private async void LivePingToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (LivePingToggle.IsChecked == true)
        {
            await _latencyMonitor.StartAsync(() => _profiles);
            EventText.Text = "Live TCP check включён: проверка endpoint каждые 5 секунд.";
        }
        else
        {
            await _latencyMonitor.StopAsync();
            EventText.Text = "Live TCP check остановлен.";
        }
    }

    private void RenderLatencies(IReadOnlyDictionary<string, LatencyResult> snapshot)
    {
        foreach (var row in _servers)
            if (snapshot.TryGetValue(row.Id, out var latency)) row.Update(latency);
        if (_selected is not null && snapshot.TryGetValue(_selected.Id, out var selected))
            SelectedLatencyText.Text = selected.IsReachable ? $"{selected.Milliseconds} ms" : "timeout";
    }

    private void RenderState(ConnectionState state)
    {
        StatusText.Text = state switch
        {
            ConnectionState.Preparing => "Подготовка",
            ConnectionState.Connecting => "Запуск прокси…",
            ConnectionState.Connected => "Прокси активен",
            ConnectionState.Disconnecting => "Отключение…",
            ConnectionState.Error => "Ошибка движка",
            _ => "Не подключено"
        };
        ConnectHint.Text = state == ConnectionState.Connected ? "Остановить прокси" : "Запустить локальный прокси";
        UpdateConnectAvailability();
        EventText.Text = state switch
        {
            ConnectionState.Connected => "SOCKS/HTTP proxy слушает 127.0.0.1:10808. TUN не включается без проверенного privileged helper.",
            ConnectionState.Disconnected => "Прокси остановлен, дочерний процесс завершён.",
            ConnectionState.Error => _controller?.ErrorMessage ?? "sing-box неожиданно остановился.",
            _ => "Запускаем sing-box и проверяем listener…"
        };
    }

    private void UpdateConnectAvailability()
    {
        var busy = _controller?.State is ConnectionState.Preparing or ConnectionState.Connecting or ConnectionState.Disconnecting;
        ConnectButton.IsEnabled = !busy && _selected is not null
            && _selected.Protocol is "vless" or "naive"
            && File.Exists(_enginePath);
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_shutdownStarted)
        {
            e.Cancel = true;
            Hide();
            EventText.Text = "MaxSpeedVPN продолжает работать в трее.";
        }
    }

    public async Task ShutdownAsync()
    {
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        await _latencyMonitor.DisposeAsync();
        if (_activeRuntime is not null) await _activeRuntime.DisposeAsync();
        Closing -= OnClosing;
        Close();
    }
}

public sealed class ServerRow : System.ComponentModel.INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Endpoint { get; init; }
    public required string ProtocolLabel { get; init; }
    public string ProtocolCode => ProtocolLabel switch { "VLESS Reality" => "VR", "NaiveProxy" => "NP", "Mieru" => "MR", _ => "PX" };
    private string _latencyText = "—";
    private string _checkedText = "не проверен";
    public string LatencyText { get => _latencyText; private set { _latencyText = value; PropertyChanged?.Invoke(this, new(nameof(LatencyText))); } }
    public string CheckedText { get => _checkedText; private set { _checkedText = value; PropertyChanged?.Invoke(this, new(nameof(CheckedText))); } }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public static ServerRow From(StoredProfile profile) => new() { Id = profile.Id, Name = profile.Name, Endpoint = $"{profile.Host}:{profile.Port}", ProtocolLabel = profile.ProtocolLabel };
    public void Update(LatencyResult latency)
    {
        LatencyText = latency.IsReachable ? $"{latency.Milliseconds} ms" : "timeout";
        CheckedText = latency.CheckedAt.ToLocalTime().ToString("HH:mm:ss");
    }
}
