namespace MaxSpeedVPN.Core;

public enum ConnectionState
{
    Disconnected,
    Preparing,
    Connecting,
    Connected,
    Disconnecting,
    Error
}

public sealed record VpnProfile(
    string Id,
    string Name,
    string Host,
    int Port,
    string UserId,
    string Security,
    string ServerName,
    string Fingerprint,
    string PublicKey,
    string ShortId);

public sealed class ProfileParser
{
    public VpnProfile Parse(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "vless")
            throw new FormatException("Only vless:// profiles are supported.");
        if (string.IsNullOrWhiteSpace(uri.UserInfo) || string.IsNullOrWhiteSpace(uri.Host) || uri.Port <= 0)
            throw new FormatException("Profile is missing user, host or port.");

        var query = ParseQuery(uri.Query);
        if (!Guid.TryParse(Uri.UnescapeDataString(uri.UserInfo), out _))
            throw new FormatException("VLESS user must be a UUID.");
        if (!string.Equals(query.GetValueOrDefault("security"), "reality", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Only VLESS Reality profiles are supported.");
        if (!string.Equals(query.GetValueOrDefault("type", "tcp"), "tcp", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Only VLESS Reality over TCP is supported.");
        if (query.TryGetValue("flow", out var flow) && !string.IsNullOrWhiteSpace(flow))
            throw new FormatException("VLESS flow is not supported yet.");
        if (!IsBase64Url(query.GetValueOrDefault("pbk"), 43))
            throw new FormatException("Reality public key is invalid.");
        var shortId = query.GetValueOrDefault("sid", string.Empty);
        if (shortId.Length > 16 || shortId.Length % 2 != 0 || !shortId.All(Uri.IsHexDigit))
            throw new FormatException("Reality short id is invalid.");
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "security", "type", "sni", "fp", "pbk", "sid", "flow" };
        if (query.Keys.Any(key => !supported.Contains(key)))
            throw new FormatException("Profile contains unsupported VLESS parameters.");

        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        if (string.IsNullOrWhiteSpace(name)) name = uri.Host;

        return new VpnProfile(
            Id: $"{uri.Host}:{uri.Port}",
            Name: name,
            Host: uri.Host,
            Port: uri.Port,
            UserId: Uri.UnescapeDataString(uri.UserInfo),
            Security: query.GetValueOrDefault("security", "tls"),
            ServerName: query.GetValueOrDefault("sni", uri.Host),
            Fingerprint: query.GetValueOrDefault("fp", "chrome"),
            PublicKey: query.GetValueOrDefault("pbk", string.Empty),
            ShortId: query.GetValueOrDefault("sid", string.Empty));
    }

    private static Dictionary<string, string> ParseQuery(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in value.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            result[Uri.UnescapeDataString(pair[0])] = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
        }
        return result;
    }

    private static bool IsBase64Url(string? value, int minimumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length >= minimumLength &&
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
}

public interface ICoreConfigWriter
{
    string Write(VpnProfile profile, int localPort);
    void AddRunArguments(System.Diagnostics.ProcessStartInfo startInfo, string configPath);
}

public sealed class SingBoxConfigWriter : ICoreConfigWriter
{
    public string Write(VpnProfile profile, int localPort) => System.Text.Json.JsonSerializer.Serialize(new
    {
        log = new { level = "info", timestamp = true },
        inbounds = new[]
        {
            new { type = "mixed", tag = "local-proxy", listen = "127.0.0.1", listen_port = localPort }
        },
        outbounds = new object[]
        {
            new
            {
                type = "vless",
                tag = "vpn",
                server = profile.Host,
                server_port = profile.Port,
                uuid = profile.UserId,
                tls = new
                {
                    enabled = true,
                    server_name = profile.ServerName,
                    utls = new { enabled = true, fingerprint = profile.Fingerprint },
                    reality = new { enabled = profile.Security == "reality", public_key = profile.PublicKey, short_id = profile.ShortId }
                }
            },
            new { type = "direct", tag = "direct" }
        },
        route = new { final = "vpn", auto_detect_interface = true }
    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    public void AddRunArguments(System.Diagnostics.ProcessStartInfo startInfo, string configPath)
    {
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configPath);
    }
}

public sealed class XrayConfigWriter : ICoreConfigWriter
{
    public string Write(VpnProfile profile, int localPort) => System.Text.Json.JsonSerializer.Serialize(new
    {
        log = new { loglevel = "warning" },
        inbounds = new object[]
        {
            new { tag = "local-socks", listen = "127.0.0.1", port = localPort, protocol = "socks", settings = new { udp = true } }
        },
        outbounds = new object[]
        {
            new
            {
                tag = "vpn",
                protocol = "vless",
                settings = new
                {
                    vnext = new object[]
                    {
                        new { address = profile.Host, port = profile.Port, users = new object[] { new { id = profile.UserId, encryption = "none" } } }
                    }
                },
                streamSettings = new
                {
                    network = "tcp",
                    security = "reality",
                    realitySettings = new
                    {
                        serverName = profile.ServerName,
                        fingerprint = profile.Fingerprint,
                        publicKey = profile.PublicKey,
                        shortId = profile.ShortId
                    }
                }
            },
            new { tag = "direct", protocol = "freedom" }
        },
        routing = new { domainStrategy = "AsIs", rules = Array.Empty<object>() }
    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    public void AddRunArguments(System.Diagnostics.ProcessStartInfo startInfo, string configPath)
    {
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configPath);
    }
}

public interface IProxyRuntime
{
    Task StartAsync(VpnProfile profile, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    event Action<string>? ExitedUnexpectedly;
}

public sealed class ConnectionController
{
    private readonly IProxyRuntime _runtime;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ConnectionController(IProxyRuntime runtime)
    {
        _runtime = runtime;
        _runtime.ExitedUnexpectedly += message =>
        {
            ErrorMessage = message;
            SetState(ConnectionState.Error);
        };
    }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string? ErrorMessage { get; private set; }
    public event Action<ConnectionState>? StateChanged;

    public async Task ConnectAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (State is not ConnectionState.Disconnected and not ConnectionState.Error) return;
            ErrorMessage = null;
            SetState(ConnectionState.Preparing);
            SetState(ConnectionState.Connecting);
            try
            {
                await _runtime.StartAsync(profile, cancellationToken);
                SetState(ConnectionState.Connected);
            }
            catch (Exception exception)
            {
                ErrorMessage = exception.Message;
                SetState(ConnectionState.Error);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (State == ConnectionState.Disconnected) return;
            SetState(ConnectionState.Disconnecting);
            await _runtime.StopAsync(cancellationToken);
            SetState(ConnectionState.Disconnected);
        }
        finally { _gate.Release(); }
    }

    private void SetState(ConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}

public class ExternalCoreRuntime : IProxyRuntime, IAsyncDisposable
{
    private readonly string _executable;
    private readonly string _runtimeDirectory;
    private readonly ICoreConfigWriter _configWriter;
    private readonly string _coreName;
    private readonly int _localPort;
    private readonly TimeSpan _startupTimeout;
    private readonly object _sync = new();
    private System.Diagnostics.Process? _process;
    private bool _stopping;

    public ExternalCoreRuntime(
        string executable,
        string runtimeDirectory,
        ICoreConfigWriter configWriter,
        string coreName,
        int localPort = 10808,
        TimeSpan? startupTimeout = null)
    {
        _executable = executable;
        _runtimeDirectory = runtimeDirectory;
        _configWriter = configWriter;
        _coreName = coreName;
        _localPort = localPort;
        _startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(5);
    }

    public event Action<string>? ExitedUnexpectedly;
    public bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        if (!File.Exists(_executable)) throw new FileNotFoundException($"{_coreName} executable was not found", _executable);

        Directory.CreateDirectory(_runtimeDirectory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_runtimeDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var configPath = Path.Combine(_runtimeDirectory, "runtime.json");
        var temporaryPath = Path.Combine(_runtimeDirectory, $"runtime-{Guid.NewGuid():N}.tmp");
        await WritePrivateFileAsync(temporaryPath, _configWriter.Write(profile, _localPort), cancellationToken);
        File.Move(temporaryPath, configPath, overwrite: true);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = _executable,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = _runtimeDirectory
        };
        _configWriter.AddRunArguments(startInfo, configPath);

        var process = System.Diagnostics.Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {_coreName}.");
        lock (_sync)
        {
            _stopping = false;
            _process = process;
        }

        try
        {
            await WaitForListenerAsync(process, cancellationToken);
            _ = ObserveExitAsync(process);
        }
        catch
        {
            await StopProcessAsync(process);
            lock (_sync)
            {
                if (ReferenceEquals(_process, process)) _process = null;
            }
            DeleteConfig();
            process.Dispose();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Process? process;
        lock (_sync)
        {
            _stopping = true;
            process = _process;
            _process = null;
        }

        if (process is not null)
        {
            await StopProcessAsync(process);
            process.Dispose();
        }
        DeleteConfig();
    }

    private async Task WaitForListenerAsync(System.Diagnostics.Process process, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_startupTimeout);
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new InvalidOperationException($"{_coreName} exited during startup with code {process.ExitCode}.");
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                await client.ConnectAsync(System.Net.IPAddress.Loopback, _localPort, timeout.Token);
                return;
            }
            catch (System.Net.Sockets.SocketException)
            {
                await Task.Delay(50, timeout.Token);
            }
        }
    }

    private async Task ObserveExitAsync(System.Diagnostics.Process process)
    {
        await process.WaitForExitAsync();
        var exitCode = process.ExitCode;
        bool unexpected;
        lock (_sync)
        {
            unexpected = !_stopping && ReferenceEquals(_process, process);
            if (ReferenceEquals(_process, process)) _process = null;
        }
        DeleteConfig();
        if (unexpected)
            ExitedUnexpectedly?.Invoke($"{_coreName} unexpectedly exited with code {exitCode}.");
    }


    private static async Task StopProcessAsync(System.Diagnostics.Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(cleanupTimeout.Token); }
            catch (OperationCanceledException) { }
        }
    }

    private static async Task WritePrivateFileAsync(string path, string content, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var stream = new FileStream(path, options);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private void DeleteConfig()
    {
        var configPath = Path.Combine(_runtimeDirectory, "runtime.json");
        if (File.Exists(configPath)) File.Delete(configPath);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}

public sealed class SingBoxRuntime : ExternalCoreRuntime
{
    public SingBoxRuntime(string executable, string runtimeDirectory, SingBoxConfigWriter configWriter, int localPort = 10808, TimeSpan? startupTimeout = null)
        : base(executable, runtimeDirectory, configWriter, "sing-box", localPort, startupTimeout) { }
}

public sealed class XrayRuntime : ExternalCoreRuntime
{
    public XrayRuntime(string executable, string runtimeDirectory, XrayConfigWriter configWriter, int localPort = 10808, TimeSpan? startupTimeout = null)
        : base(executable, runtimeDirectory, configWriter, "Xray", localPort, startupTimeout) { }
}

public static class AppPaths
{
    public static string DataDirectory()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var root = !string.IsNullOrWhiteSpace(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(root, "maxspeedvpn");
    }
}
