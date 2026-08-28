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
    public StoredProfile ParseStored(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new FormatException("Profile URI is invalid.");
        if (string.Equals(uri.Scheme, "vless", StringComparison.OrdinalIgnoreCase))
            return StoredProfile.FromVpnProfile(Parse(value)) with { SourceUri = value };
        if (string.Equals(uri.Scheme, "naive+https", StringComparison.OrdinalIgnoreCase))
            return ParseNaive(uri, value);
        if (string.Equals(uri.Scheme, "mierus", StringComparison.OrdinalIgnoreCase))
            return ParseMieru(uri, value);
        throw new FormatException("Supported profiles: VLESS Reality, NaiveProxy and Mieru.");
    }

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

    private static StoredProfile ParseNaive(Uri uri, string source)
    {
        if (string.IsNullOrWhiteSpace(uri.UserInfo) || string.IsNullOrWhiteSpace(uri.Host) || uri.Port <= 0)
            throw new FormatException("NaiveProxy profile is missing credentials, host or port.");
        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2 || credentials.Any(string.IsNullOrWhiteSpace))
            throw new FormatException("NaiveProxy username and password are required.");
        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        if (string.IsNullOrWhiteSpace(name)) name = uri.Host;
        return new StoredProfile(
            $"naive:{uri.Host}:{uri.Port}", name, "naive", "NaiveProxy", uri.Host, uri.Port,
            RuntimeProfile: null, SourceUri: source,
            Username: Uri.UnescapeDataString(credentials[0]), Password: Uri.UnescapeDataString(credentials[1]),
            Transport: "HTTPS");
    }

    private static StoredProfile ParseMieru(Uri uri, string source)
    {
        if (string.IsNullOrWhiteSpace(uri.UserInfo) || string.IsNullOrWhiteSpace(uri.Host))
            throw new FormatException("Mieru profile is missing credentials or host.");
        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2 || credentials.Any(string.IsNullOrWhiteSpace))
            throw new FormatException("Mieru username and password are required.");
        var query = ParseQueryMulti(uri.Query);
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "profile", "port", "protocol" };
        if (query.Keys.Any(key => !supported.Contains(key)))
            throw new FormatException("Only the Mieru simple TCP subset is supported.");
        var profileNames = query.GetValueOrDefault("profile") ?? [];
        var ports = query.GetValueOrDefault("port") ?? [];
        var protocols = query.GetValueOrDefault("protocol") ?? [];
        if (profileNames.Count != 1 || string.IsNullOrWhiteSpace(profileNames[0]) || ports.Count != 1 || protocols.Count != 1)
            throw new FormatException("Only one Mieru simple TCP binding is supported.");
        if (!int.TryParse(ports[0], out var port) || port is < 1 or > 65535)
            throw new FormatException("Mieru port must be a single valid port.");
        if (!string.Equals(protocols[0], "TCP", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Only Mieru simple TCP is supported.");
        return new StoredProfile(
            $"mieru:{uri.Host}:{port}", profileNames[0], "mieru", "Mieru simple TCP", uri.Host, port,
            RuntimeProfile: null, SourceUri: source,
            Username: Uri.UnescapeDataString(credentials[0]), Password: Uri.UnescapeDataString(credentials[1]),
            Transport: "TCP");
    }

    private static Dictionary<string, string> ParseQuery(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, itemValue) in ParseQueryItems(value)) result[key] = itemValue;
        return result;
    }

    private static Dictionary<string, List<string>> ParseQueryMulti(string value)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, itemValue) in ParseQueryItems(value))
        {
            if (!result.TryGetValue(key, out var values)) result[key] = values = [];
            values.Add(itemValue);
        }
        return result;
    }

    private static IEnumerable<(string Key, string Value)> ParseQueryItems(string value)
    {
        foreach (var item in value.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            yield return (Uri.UnescapeDataString(pair[0]), pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty);
        }
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

    public string Write(StoredProfile profile, int localPort, bool enableTun)
    {
        object outbound = profile.Protocol switch
        {
            "vless" when profile.RuntimeProfile is not null => CreateVlessOutbound(profile.RuntimeProfile),
            "naive" => new
            {
                type = "naive", tag = "vpn", server = profile.Host, server_port = profile.Port,
                username = profile.Username, password = profile.Password,
                tls = new { enabled = true, server_name = profile.Host }
            },
            _ => throw new NotSupportedException($"{profile.ProtocolLabel} requires its native runtime.")
        };
        var inbounds = new List<object>
        {
            new { type = "mixed", tag = "local-proxy", listen = "127.0.0.1", listen_port = localPort }
        };
        if (enableTun)
            inbounds.Add(new { type = "tun", tag = "tun-in", interface_name = "maxspeed0", address = new[] { "172.19.0.1/30" }, auto_route = true, strict_route = true, stack = "system" });
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            log = new { level = "info", timestamp = true },
            inbounds,
            outbounds = new object[] { outbound, new { type = "direct", tag = "direct" } },
            route = new { final = "vpn", auto_detect_interface = true }
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private static object CreateVlessOutbound(VpnProfile profile) => new
    {
        type = "vless", tag = "vpn", server = profile.Host, server_port = profile.Port, uuid = profile.UserId,
        tls = new
        {
            enabled = true, server_name = profile.ServerName,
            utls = new { enabled = true, fingerprint = profile.Fingerprint },
            reality = new { enabled = profile.Security == "reality", public_key = profile.PublicKey, short_id = profile.ShortId }
        }
    };
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

public sealed class StoredSingBoxConfigWriter(StoredProfile profile, bool enableTun = false) : ICoreConfigWriter
{
    private readonly SingBoxConfigWriter _writer = new();
    public string Write(VpnProfile ignored, int localPort) => _writer.Write(profile, localPort, enableTun);
    public void AddRunArguments(System.Diagnostics.ProcessStartInfo startInfo, string configPath) => _writer.AddRunArguments(startInfo, configPath);
}

public sealed class StoredSingBoxRuntime : ExternalCoreRuntime
{
    private readonly VpnProfile _placeholder;
    public StoredSingBoxRuntime(string executable, string runtimeDirectory, StoredProfile profile, bool enableTun = false, int localPort = 10808, TimeSpan? startupTimeout = null)
        : base(executable, runtimeDirectory, new StoredSingBoxConfigWriter(profile, enableTun), "sing-box", localPort, startupTimeout)
    {
        _placeholder = profile.RuntimeProfile ?? new VpnProfile(profile.Id, profile.Name, profile.Host, profile.Port, "00000000-0000-0000-0000-000000000000", "none", profile.Host, "chrome", "", "");
    }
    public Task StartAsync(CancellationToken cancellationToken = default) => base.StartAsync(_placeholder, cancellationToken);
}

public sealed class XrayRuntime : ExternalCoreRuntime
{
    public XrayRuntime(string executable, string runtimeDirectory, XrayConfigWriter configWriter, int localPort = 10808, TimeSpan? startupTimeout = null)
        : base(executable, runtimeDirectory, configWriter, "Xray", localPort, startupTimeout) { }
}

public static class MieruRuntimeAdapter
{
    public static MieruRuntimeSpec Create(string executable, StoredProfile profile, string runtimeDirectory, int localPort)
    {
        if (profile.Protocol != "mieru") throw new NotSupportedException("Profile is not Mieru.");
        if (!File.Exists(executable)) throw new FileNotFoundException("Mieru client is not installed.", executable);
        var configPath = Path.Combine(runtimeDirectory, "mieru-client.json");
        var addressField = System.Net.IPAddress.TryParse(profile.Host, out _) ? "ipAddress" : "domainName";
        var server = new Dictionary<string, object>
        {
            [addressField] = profile.Host,
            ["portBindings"] = new object[] { new { port = profile.Port, protocol = profile.Transport } }
        };
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            profiles = new object[]
            {
                new
                {
                    profileName = profile.Name,
                    user = new { name = profile.Username, password = profile.Password },
                    servers = new object[] { server }
                }
            },
            activeProfile = profile.Name,
            rpcPort = localPort + 1,
            socks5Port = localPort,
            loggingLevel = "INFO",
            socks5ListenLAN = false
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return new MieruRuntimeSpec(executable, configPath, json);
    }
}

public sealed record MieruRuntimeSpec(string Executable, string ConfigPath, string ConfigJson);

public sealed record StoredProfile(
    string Id,
    string Name,
    string Protocol,
    string ProtocolLabel,
    string Host,
    int Port,
    VpnProfile? RuntimeProfile,
    string SourceUri = "",
    string Username = "",
    string Password = "",
    string Transport = "TCP")
{
    public static StoredProfile FromVpnProfile(VpnProfile profile) => new(
        profile.Id,
        profile.Name,
        "vless",
        string.Equals(profile.Security, "reality", StringComparison.OrdinalIgnoreCase) ? "VLESS Reality" : "VLESS",
        profile.Host,
        profile.Port,
        profile);
}

public sealed class ProfileStore
{
    private readonly string _directory;
    private readonly string _path;
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ProfileStore(string directory)
    {
        _directory = directory;
        _path = Path.Combine(directory, "profiles.json");
    }

    public async Task<IReadOnlyList<StoredProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return Array.Empty<StoredProfile>();
        await using var stream = File.OpenRead(_path);
        var profiles = await System.Text.Json.JsonSerializer.DeserializeAsync<List<StoredProfile>>(stream, JsonOptions, cancellationToken);
        return profiles ?? new List<StoredProfile>();
    }

    public async Task UpsertAsync(StoredProfile profile, CancellationToken cancellationToken = default)
    {
        EnsurePrivateDirectory(_directory);
        var profiles = (await LoadAsync(cancellationToken)).ToList();
        var existing = profiles.FindIndex(item => string.Equals(item.Id, profile.Id, StringComparison.Ordinal));
        if (existing >= 0) profiles[existing] = profile;
        else profiles.Add(profile);
        var tempPath = _path + ".tmp";
        if (File.Exists(tempPath)) File.Delete(tempPath);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var stream = new FileStream(tempPath, options))
            await System.Text.Json.JsonSerializer.SerializeAsync(stream, profiles, JsonOptions, cancellationToken);
        File.Move(tempPath, _path, true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void EnsurePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}

public sealed record LatencyResult(bool IsReachable, int? Milliseconds, DateTimeOffset CheckedAt);

public interface ILatencyProbe
{
    Task<LatencyResult> MeasureAsync(string host, int port, CancellationToken cancellationToken = default);
}

public sealed class TcpLatencyProbe : ILatencyProbe
{
    private readonly TimeSpan _timeout;
    public TcpLatencyProbe(TimeSpan? timeout = null) => _timeout = timeout ?? TimeSpan.FromSeconds(3);

    public async Task<LatencyResult> MeasureAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
            started.Stop();
            return new LatencyResult(true, (int)Math.Max(1, started.ElapsedMilliseconds), DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or OperationCanceledException)
        {
            return new LatencyResult(false, null, DateTimeOffset.UtcNow);
        }
    }
}

public sealed class LiveLatencyMonitor : IAsyncDisposable
{
    private readonly ILatencyProbe _probe;
    private readonly TimeSpan _interval;
    private CancellationTokenSource? _loopCancellation;
    private Task? _loop;
    public event Action<IReadOnlyDictionary<string, LatencyResult>>? Updated;

    public LiveLatencyMonitor(ILatencyProbe probe, TimeSpan? interval = null)
    {
        _probe = probe;
        _interval = interval ?? TimeSpan.FromSeconds(5);
    }

    public async Task<IReadOnlyDictionary<string, LatencyResult>> RefreshAsync(IEnumerable<StoredProfile> profiles, CancellationToken cancellationToken = default)
    {
        var snapshot = profiles.ToArray();
        var measurements = await Task.WhenAll(snapshot.Select(async profile =>
            (profile.Id, Result: await _probe.MeasureAsync(profile.Host, profile.Port, cancellationToken))));
        var result = measurements.ToDictionary(item => item.Id, item => item.Result, StringComparer.Ordinal);
        Updated?.Invoke(result);
        return result;
    }

    public Task StartAsync(Func<IReadOnlyList<StoredProfile>> profiles)
    {
        if (_loop is not null) return Task.CompletedTask;
        _loopCancellation = new CancellationTokenSource();
        var token = _loopCancellation.Token;
        _loop = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await RefreshAsync(profiles(), token);
                    await Task.Delay(_interval, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        }, token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var loop = _loop;
        var cancellation = _loopCancellation;
        _loop = null;
        _loopCancellation = null;
        if (loop is null || cancellation is null) return;
        cancellation.Cancel();
        try { await loop; }
        catch (OperationCanceledException) { }
        cancellation.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}

public sealed record TunRequest(string InterfaceName, string Address, string ServerAddress, int ServerPort, int RoutingTable, int FirewallMark)
{
    public static TunRequest Create(string serverAddress, int serverPort)
    {
        if (!System.Net.IPAddress.TryParse(serverAddress, out _))
            throw new ArgumentException("TUN endpoint must be a resolved IP address.", nameof(serverAddress));
        if (serverPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(serverPort));
        return new TunRequest("maxspeed0", "172.19.0.1/30", serverAddress, serverPort, 20298, 0x4D58);
    }
}

public interface ITunExecutor
{
    Task AddTableAsync(TunRequest request, CancellationToken cancellationToken);
    Task AddRuleAsync(TunRequest request, CancellationToken cancellationToken);
    Task AddRouteAsync(TunRequest request, CancellationToken cancellationToken);
    Task DeleteRouteAsync(TunRequest request, CancellationToken cancellationToken);
    Task DeleteRuleAsync(TunRequest request, CancellationToken cancellationToken);
    Task DeleteTableAsync(TunRequest request, CancellationToken cancellationToken);
}

public sealed class TunTransaction : IAsyncDisposable
{
    private readonly ITunExecutor _executor;
    private readonly TunRequest _request;
    private readonly List<Func<CancellationToken, Task>> _rollback = [];
    private bool _disposed;

    private TunTransaction(ITunExecutor executor, TunRequest request)
    {
        _executor = executor;
        _request = request;
    }

    public static async Task<TunTransaction> ApplyAsync(ITunExecutor executor, TunRequest request, CancellationToken cancellationToken = default)
    {
        var transaction = new TunTransaction(executor, request);
        try
        {
            await executor.AddTableAsync(request, cancellationToken);
            transaction._rollback.Add(token => executor.DeleteTableAsync(request, token));
            await executor.AddRuleAsync(request, cancellationToken);
            transaction._rollback.Add(token => executor.DeleteRuleAsync(request, token));
            await executor.AddRouteAsync(request, cancellationToken);
            transaction._rollback.Add(token => executor.DeleteRouteAsync(request, token));
            return transaction;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await RollbackAsync(CancellationToken.None);
    }

    private async Task RollbackAsync(CancellationToken cancellationToken)
    {
        List<Exception>? errors = null;
        for (var index = _rollback.Count - 1; index >= 0; index--)
        {
            try { await _rollback[index](cancellationToken); }
            catch (Exception exception) { (errors ??= []).Add(exception); }
        }
        _rollback.Clear();
        if (errors is not null) throw new AggregateException("TUN rollback failed.", errors);
    }
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
