using System.Collections.ObjectModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Media;
using MaxSpeedVPN.Core;

namespace MaxSpeedVPN.Desktop;

public partial class MainWindow : Window
{
    private ConnectionController? _controller;
    private IAsyncDisposable? _activeRuntime;
    private readonly ProfileStore _profileStore;
    private readonly SubscriptionStore _subscriptionStore;
    private readonly SettingsStore _settingsStore;
    private readonly HwidStore _hwidStore;
    private readonly LiveLatencyMonitor _latencyMonitor;
    private readonly CorePaths _corePaths;
    private readonly CoreSelector _coreSelector = new();
    private readonly SubscriptionClient _subscriptionClient = new();
    private readonly SubscriptionParser _subscriptionParser = new();
    private readonly ObservableCollection<ServerRow> _servers = [];
    private readonly ObservableCollection<SubscriptionRow> _subscriptions = [];
    private IReadOnlyList<StoredProfile> _profiles = [];
    private IReadOnlyList<SubscriptionDefinition> _subscriptionDefinitions = [];
    private StoredProfile? _selected;
    private AppSettings _settings = AppSettings.Default;
    private bool _shutdownStarted;

    public MainWindow()
    {
        InitializeComponent();
        _corePaths = new CorePaths(ResolveCore("sing-box"), ResolveCore("xray"), ResolveCore("mieru"));
        var data = AppPaths.DataDirectory();
        _profileStore = new ProfileStore(Path.Combine(data, "profiles"));
        _subscriptionStore = new SubscriptionStore(Path.Combine(data, "subscriptions"));
        _settingsStore = new SettingsStore(Path.Combine(data, "config"));
        _hwidStore = new HwidStore(Path.Combine(data, "device"));
        _latencyMonitor = new LiveLatencyMonitor(new TcpLatencyProbe(), TimeSpan.FromSeconds(5));
        _latencyMonitor.Updated += snapshot => Avalonia.Threading.Dispatcher.UIThread.Post(() => RenderLatencies(snapshot));
        ServersList.ItemsSource = _servers;
        SubscriptionsList.ItemsSource = _subscriptions;
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private static string ResolveCore(string name)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "bin", name);
        if (File.Exists(bundled)) return bundled;
        return $"/usr/bin/{name}";
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            _settings = await _settingsStore.LoadAsync();
            if (_settings.RussiaRouting == RussiaRoutingMode.OnlyUnavailable)
            {
                _settings = _settings with { RussiaRouting = RussiaRoutingMode.AllTraffic };
                await _settingsStore.SaveAsync(_settings);
                EventText.Text = "Устаревший региональный режим отключён: используется проверенный базовый маршрут.";
            }
            ApplySettingsToUi();
            await ReloadProfilesAsync();
            await ReloadSubscriptionsAsync();
            if (_settings.AutoUpdateSubscriptions)
                foreach (var subscription in _subscriptionDefinitions) await RefreshSubscriptionAsync(subscription, quiet: true);
            LivePingToggle.IsChecked = false;
        }
        catch (Exception exception) { EventText.Text = $"Ошибка инициализации: {exception.Message}"; }
    }

    private async Task ReloadProfilesAsync(string? selectId = null)
    {
        _profiles = await _profileStore.LoadAsync();
        var current = selectId ?? _selected?.Id;
        _servers.Clear();
        foreach (var profile in _profiles) _servers.Add(ServerRow.From(profile));
        ServerCountText.Text = $"{_profiles.Count} сохранено";
        EmptyServersState.IsVisible = _profiles.Count == 0;
        ServersList.IsVisible = _profiles.Count > 0;
        if (_profiles.Count == 0)
        {
            _selected = null;
            SelectedCoreText.Text = "—";
            SelectedLatencyText.Text = "—";
            UpdateConnectAvailability();
            return;
        }
        var index = Math.Max(0, _profiles.ToList().FindIndex(profile => profile.Id == current));
        ServersList.SelectedIndex = index;
    }

    private async Task ReloadSubscriptionsAsync()
    {
        _subscriptionDefinitions = await _subscriptionStore.LoadAsync();
        _subscriptions.Clear();
        foreach (var item in _subscriptionDefinitions) _subscriptions.Add(SubscriptionRow.From(item));
        SubscriptionCountText.Text = $"{_subscriptions.Count} добавлено";
        EmptySubscriptionsState.IsVisible = _subscriptions.Count == 0;
        SubscriptionsList.IsVisible = _subscriptions.Count > 0;
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
            var selection = _coreSelector.Select(_selected, _settings.PreferredCore, _corePaths);
            if (selection.Kind == CoreKind.Mieru)
            {
                EventText.Text = "Mieru импортирован, но Connect остаётся отключён до отдельного lifecycle smoke.";
                return;
            }
            if (_activeRuntime is not null) await _activeRuntime.DisposeAsync();
            var runtimeDirectory = Path.Combine(AppPaths.DataDirectory(), "runtime");
            var routing = new RoutingOptions(_settings.RussiaRouting, PrivateNetworksDirectCheckBox.IsChecked == true);
            _activeRuntime = selection.Kind switch
            {
                CoreKind.Xray => new StoredXrayRuntime(selection.Executable, runtimeDirectory, _selected, routing),
                _ => new StoredSingBoxRuntime(selection.Executable, runtimeDirectory, _selected, routing)
            };
            _controller = new ConnectionController((IProxyRuntime)_activeRuntime);
            _controller.StateChanged += state => Avalonia.Threading.Dispatcher.UIThread.Post(() => RenderState(state));
            var runtimeProfile = _selected.RuntimeProfile ??
                new VpnProfile(_selected.Id, _selected.Name, _selected.Host, _selected.Port,
                    "00000000-0000-0000-0000-000000000000", "none", _selected.Host, "chrome", string.Empty, string.Empty);
            await _controller.ConnectAsync(runtimeProfile);
        }
        catch (Exception exception) { EventText.Text = $"Ошибка запуска: {exception.Message}"; }
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
            EventText.Text = $"{profile.Name}: {profile.ProtocolLabel}.";
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

    private void OpenSubscriptions_Click(object? sender, RoutedEventArgs e)
    {
        SubscriptionErrorText.IsVisible = false;
        SubscriptionOverlay.IsVisible = true;
        SubscriptionUrlTextBox.Focus();
    }

    private void GoToSubscriptions_Click(object? sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 1;

    private async void PasteSubscriptionUrl_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = Clipboard ?? throw new InvalidOperationException("Буфер обмена недоступен.");
            var text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Буфер обмена не содержит ссылку.");
            SubscriptionUrlTextBox.Text = text.Trim();
            SubscriptionUrlTextBox.CaretIndex = SubscriptionUrlTextBox.Text.Length;
            SubscriptionErrorText.IsVisible = false;
        }
        catch (Exception exception)
        {
            SubscriptionErrorText.Text = $"Не удалось прочитать буфер обмена: {exception.Message}";
            SubscriptionErrorText.IsVisible = true;
        }
    }

    private void CancelSubscription_Click(object? sender, RoutedEventArgs e)
    {
        SubscriptionOverlay.IsVisible = false;
        SubscriptionNameTextBox.Text = string.Empty;
        SubscriptionUrlTextBox.Text = string.Empty;
    }

    private async void ConfirmSubscription_Click(object? sender, RoutedEventArgs e)
    {
        ConfirmSubscriptionButton.IsEnabled = false;
        try
        {
            var subscription = SubscriptionDefinition.Create(SubscriptionNameTextBox.Text ?? string.Empty, SubscriptionUrlTextBox.Text ?? string.Empty);
            await RefreshSubscriptionAsync(subscription, quiet: false, throwOnFailure: true);
            CancelSubscription_Click(sender, e);
            MainTabs.SelectedIndex = 1;
        }
        catch (Exception exception)
        {
            SubscriptionErrorText.Text = UserFacingError(exception);
            SubscriptionErrorText.IsVisible = true;
        }
        finally { ConfirmSubscriptionButton.IsEnabled = true; }
    }

    private async void RefreshSubscription_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var definition = _subscriptionDefinitions.FirstOrDefault(item => item.Id == id);
        if (definition is not null) await RefreshSubscriptionAsync(definition, quiet: false);
    }

    private async Task RefreshSubscriptionAsync(SubscriptionDefinition subscription, bool quiet, bool throwOnFailure = false)
    {
        try
        {
            if (!quiet) EventText.Text = $"Обновляем {subscription.Name}…";
            var hwid = await _hwidStore.GetOrCreateAsync();
            var payload = await _subscriptionClient.DownloadAsync(subscription.Url, hwid);
            var result = await new SubscriptionRefreshService(_profileStore, _subscriptionStore, _subscriptionParser).ApplyAsync(subscription, payload);
            await ReloadProfilesAsync();
            await ReloadSubscriptionsAsync();
            if (!quiet) EventText.Text = $"{subscription.Name}: добавлено/обновлено {result.Profiles.Count}, пропущено {result.RejectedLines.Count}.";
        }
        catch (Exception exception)
        {
            EventText.Text = $"Не удалось обновить {subscription.Name}: {UserFacingError(exception)}";
            if (throwOnFailure) throw;
        }
    }

    private static string UserFacingError(Exception exception) => exception switch
    {
        TaskCanceledException => "Сервер не ответил вовремя.",
        HttpRequestException { StatusCode: not null } http => $"Сервер подписки ответил HTTP {(int)http.StatusCode.Value}.",
        HttpRequestException => "Не удалось подключиться к серверу подписки.",
        _ => exception.Message
    };

    private void ServersList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ServersList.SelectedIndex < 0 || ServersList.SelectedIndex >= _profiles.Count) return;
        _selected = _profiles[ServersList.SelectedIndex];
        var row = _servers.FirstOrDefault(item => item.Id == _selected.Id);
        SelectedLatencyText.Text = row?.LatencyText ?? "—";
        RenderSelectedCore();
        UpdateConnectAvailability();
    }

    private void RenderSelectedCore()
    {
        if (_selected is null) { SelectedCoreText.Text = "—"; return; }
        try { SelectedCoreText.Text = _coreSelector.Select(_selected, _settings.PreferredCore, _corePaths).Kind.ToString(); }
        catch { SelectedCoreText.Text = "недоступно"; }
    }

    private async void PingAll_Click(object? sender, RoutedEventArgs e) => await PingAllAsync();

    private async Task PingAllAsync()
    {
        PingAllButton.IsEnabled = false;
        try
        {
            EventText.Text = "Измеряем задержку endpoint…";
            await _latencyMonitor.RefreshAsync(_profiles);
            EventText.Text = "Пинг обновлён.";
        }
        finally { PingAllButton.IsEnabled = true; }
    }

    private async void LivePingToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (LivePingToggle.IsChecked == true)
        {
            await _latencyMonitor.StartAsync(() => _profiles);
            EventText.Text = "real-time пинг включён: интервал 5 секунд.";
        }
        else
        {
            await _latencyMonitor.StopAsync();
            EventText.Text = "real-time пинг остановлен.";
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
            ConnectionState.Connecting => "Запуск proxy…",
            ConnectionState.Connected => "Proxy активен",
            ConnectionState.Disconnecting => "Отключение…",
            ConnectionState.Error => "Ошибка ядра",
            _ => "Не подключено"
        };
        ConnectHint.Text = state == ConnectionState.Connected ? "Остановить proxy" : "Запустить локальный proxy";
        UpdateConnectAvailability();
        EventText.Text = state switch
        {
            ConnectionState.Connected => "SOCKS/HTTP proxy слушает 127.0.0.1:10808. System TUN не включён.",
            ConnectionState.Disconnected => "Proxy остановлен, дочерний процесс завершён.",
            ConnectionState.Error => _controller?.ErrorMessage ?? "Ядро неожиданно остановилось.",
            _ => "Запускаем выбранное ядро…"
        };
    }

    private void UpdateConnectAvailability()
    {
        var busy = _controller?.State is ConnectionState.Preparing or ConnectionState.Connecting or ConnectionState.Disconnecting;
        var available = false;
        if (_selected is not null && _selected.Protocol != "mieru")
        {
            try { _coreSelector.Select(_selected, _settings.PreferredCore, _corePaths); available = true; }
            catch { }
        }
        ConnectButton.IsEnabled = !busy && available;
    }


    private async void SaveSettings_Click(object? sender, RoutedEventArgs e)
    {
        var preference = CorePreferenceBox.SelectedIndex switch { 1 => CorePreference.SingBox, 2 => CorePreference.Xray, _ => CorePreference.Auto };
        _settings = new AppSettings(
            preference,
            RussiaAllTrafficRadio.IsChecked == true ? RussiaRoutingMode.AllTraffic : RussiaRoutingMode.OnlyUnavailable,
            AutoUpdateSubscriptionsCheckBox.IsChecked == true,
            false,
            false);
        await _settingsStore.SaveAsync(_settings);
        RenderSelectedCore();
        UpdateConnectAvailability();
        EventText.Text = "Настройки сохранены. Они применятся при следующем подключении.";
    }

    private void ApplySettingsToUi()
    {
        CorePreferenceBox.SelectedIndex = _settings.PreferredCore switch { CorePreference.SingBox => 1, CorePreference.Xray => 2, _ => 0 };
        RussiaAllTrafficRadio.IsChecked = true;
        RussiaOnlyUnavailableRadio.IsChecked = false;
        AutoUpdateSubscriptionsCheckBox.IsChecked = _settings.AutoUpdateSubscriptions;
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
        _subscriptionClient.Dispose();
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
    public string ProtocolCode => ProtocolLabel switch { "VLESS Reality" => "VR", "NaiveProxy" => "NP", "Mieru simple TCP" => "MR", _ => "PX" };
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

public sealed class SubscriptionRow
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string DisplayHost { get; init; }
    public required string UpdatedText { get; init; }
    public static SubscriptionRow From(SubscriptionDefinition item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        DisplayHost = Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) ? uri.Host : "некорректный URL",
        UpdatedText = item.LastUpdated is null ? "ещё не обновлялась" : $"обновлено {item.LastUpdated.Value.ToLocalTime():dd.MM.yyyy HH:mm}"
    };
}
