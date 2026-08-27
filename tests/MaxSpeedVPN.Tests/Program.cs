using MaxSpeedVPN.Core;

var tests = new (string Name, Func<Task> Run)[]
{
    ("ProfileParser parses VLESS Reality URI", ProfileParserParsesVlessReality),
    ("ProfileParser rejects unsupported URI", ProfileParserRejectsUnsupported),
    ("SingBoxConfig renders local mixed proxy", SingBoxConfigRendersLocalMixedProxy),
    ("XrayConfig renders local SOCKS proxy and Reality outbound", XrayConfigRendersLocalSocksProxy),
    ("ConnectionController connects and disconnects", ConnectionControllerConnectsAndDisconnects),
    ("ConnectionController reports startup failure", ConnectionControllerReportsStartupFailure),
    ("AppPaths follows XDG data home", AppPathsFollowsXdgDataHome),
    ("SingBoxRuntime writes config and owns process", SingBoxRuntimeWritesConfigAndOwnsProcess),
    ("ProfileParser rejects unsupported transport and invalid Reality fields", ProfileParserRejectsUnsupportedRealityVariants),
    ("SingBoxRuntime cancellation after spawn reaps process", SingBoxRuntimeCancellationAfterSpawnReapsProcess),
    ("SingBoxRuntime unexpected exit propagates to controller", SingBoxRuntimeUnexpectedExitPropagatesToController),
    ("SingBoxRuntime secures and removes runtime config", SingBoxRuntimeSecuresAndRemovesRuntimeConfig),
    ("SingBoxConfig passes real engine validation", SingBoxConfigPassesRealEngineValidation),
    ("XrayConfig passes real engine validation when available", XrayConfigPassesRealEngineValidation)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"RESULT {tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static Task ProfileParserParsesVlessReality()
{
    var parser = new ProfileParser();
    var profile = parser.Parse("vless://00000000-0000-0000-0000-000000000001@vpn.example.com:443?security=reality&sni=cdn.example.com&fp=chrome&pbk=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8&sid=ab&type=tcp#Amsterdam");
    Equal("Amsterdam", profile.Name);
    Equal("vpn.example.com", profile.Host);
    Equal(443, profile.Port);
    Equal("cdn.example.com", profile.ServerName);
    Equal("reality", profile.Security);
    Equal("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8", profile.PublicKey);
    return Task.CompletedTask;
}

static Task ProfileParserRejectsUnsupported()
{
    Throws<FormatException>(() => new ProfileParser().Parse("vmess://not-supported"));
    return Task.CompletedTask;
}

static Task ProfileParserRejectsUnsupportedRealityVariants()
{
    var parser = new ProfileParser();
    Throws<FormatException>(() => parser.Parse("vless://00000000-0000-0000-0000-000000000001@vpn.example.com:443?security=reality&type=ws&sni=cdn.example.com&pbk=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8&sid=ab"));
    Throws<FormatException>(() => parser.Parse("vless://not-a-uuid@vpn.example.com:443?security=reality&type=tcp&sni=cdn.example.com&pbk=bad&sid=xyz"));
    return Task.CompletedTask;
}

static Task SingBoxConfigRendersLocalMixedProxy()
{
    var profile = new VpnProfile("nl", "Netherlands", "server.example", 443, "00000000-0000-0000-0000-000000000001", "reality", "cdn.example", "chrome", "public-key", "ab");
    var json = new SingBoxConfigWriter().Write(profile, 10808);
    Contains("\"type\": \"mixed\"", json);
    Contains("\"listen_port\": 10808", json);
    Contains("\"server\": \"server.example\"", json);
    Contains("\"type\": \"vless\"", json);
    Contains("\"enabled\": true", json);
    return Task.CompletedTask;
}

static Task XrayConfigRendersLocalSocksProxy()
{
    var json = new XrayConfigWriter().Write(SampleProfile(), 10808);
    Contains("\"protocol\": \"socks\"", json);
    Contains("\"security\": \"reality\"", json);
    Contains("\"publicKey\": \"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8\"", json);
    return Task.CompletedTask;
}

static async Task ConnectionControllerConnectsAndDisconnects()
{
    var runtime = new FakeRuntime();
    var controller = new ConnectionController(runtime);
    var profile = SampleProfile();
    var states = new List<ConnectionState>();
    controller.StateChanged += state => states.Add(state);

    await controller.ConnectAsync(profile);
    Equal(ConnectionState.Connected, controller.State);
    Equal(1, runtime.StartCount);

    await controller.DisconnectAsync();
    Equal(ConnectionState.Disconnected, controller.State);
    Equal(1, runtime.StopCount);
    Sequence(new[] { ConnectionState.Preparing, ConnectionState.Connecting, ConnectionState.Connected, ConnectionState.Disconnecting, ConnectionState.Disconnected }, states);
}

static async Task ConnectionControllerReportsStartupFailure()
{
    var runtime = new FakeRuntime { StartError = new InvalidOperationException("core failed") };
    var controller = new ConnectionController(runtime);
    await ThrowsAsync<InvalidOperationException>(() => controller.ConnectAsync(SampleProfile()));
    Equal(ConnectionState.Error, controller.State);
    Equal("core failed", controller.ErrorMessage);
}

static Task AppPathsFollowsXdgDataHome()
{
    var old = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    try
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", "/tmp/maxspeedvpn-test-data");
        Equal("/tmp/maxspeedvpn-test-data/maxspeedvpn", AppPaths.DataDirectory());
    }
    finally
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", old);
    }
    return Task.CompletedTask;
}

static async Task SingBoxRuntimeWritesConfigAndOwnsProcess()
{
    var root = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-runtime-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var script = Path.Combine(root, "fake-sing-box");
    var argsFile = Path.Combine(root, "args.txt");
    var port = GetFreePort();
    await File.WriteAllTextAsync(script, $"#!/bin/sh\nprintf '%s' \"$*\" > '{argsFile}'\npython3 -m http.server {port} --bind 127.0.0.1 >/dev/null 2>&1 & child=$!\ntrap 'kill $child 2>/dev/null; exit 0' TERM INT\nwait $child\n");
    if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    var runtime = new SingBoxRuntime(script, root, new SingBoxConfigWriter(), localPort: port, startupTimeout: TimeSpan.FromSeconds(3));
    try
    {
        await runtime.StartAsync(SampleProfile());
        await Task.Delay(150);
        Equal(true, runtime.IsRunning);
        Equal(true, File.Exists(Path.Combine(root, "runtime.json")));
        Contains("run -c", await File.ReadAllTextAsync(argsFile));
        await runtime.StopAsync();
        Equal(false, runtime.IsRunning);
    }
    finally
    {
        await runtime.DisposeAsync();
        Directory.Delete(root, true);
    }
}

static async Task SingBoxRuntimeCancellationAfterSpawnReapsProcess()
{
    var root = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-cancel-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var script = Path.Combine(root, "fake-sing-box");
    await File.WriteAllTextAsync(script, "#!/bin/sh\nwhile :; do sleep 1; done\n");
    File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    await using var runtime = new SingBoxRuntime(script, Path.Combine(root, "runtime"), new SingBoxConfigWriter(), localPort: GetFreePort(), startupTimeout: TimeSpan.FromSeconds(5));
    using var cancellation = new CancellationTokenSource(150);
    try
    {
        await ThrowsAsync<OperationCanceledException>(() => runtime.StartAsync(SampleProfile(), cancellation.Token));
        await Task.Delay(150);
        Equal(false, runtime.IsRunning);
    }
    finally { Directory.Delete(root, true); }
}

static async Task SingBoxRuntimeUnexpectedExitPropagatesToController()
{
    var root = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-exit-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var script = Path.Combine(root, "fake-sing-box");
    var port = GetFreePort();
    await File.WriteAllTextAsync(script, $"#!/bin/sh\npython3 -m http.server {port} --bind 127.0.0.1 >/dev/null 2>&1 & child=$!\nsleep 0.4\nkill $child 2>/dev/null || true\nexit 23\n");
    File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    await using var runtime = new SingBoxRuntime(script, Path.Combine(root, "runtime"), new SingBoxConfigWriter(), localPort: port, startupTimeout: TimeSpan.FromSeconds(3));
    var controller = new ConnectionController(runtime);
    await controller.ConnectAsync(SampleProfile());
    Equal(ConnectionState.Connected, controller.State);
    for (var i = 0; i < 30 && controller.State == ConnectionState.Connected; i++) await Task.Delay(100);
    Equal(ConnectionState.Error, controller.State);
    Equal(false, runtime.IsRunning);
    Directory.Delete(root, true);
}

static async Task SingBoxRuntimeSecuresAndRemovesRuntimeConfig()
{
    var root = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-perms-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var script = Path.Combine(root, "fake-sing-box");
    var port = GetFreePort();
    await File.WriteAllTextAsync(script, $"#!/bin/sh\npython3 -m http.server {port} --bind 127.0.0.1 >/dev/null 2>&1 & child=$!\ntrap 'kill $child 2>/dev/null; exit 0' TERM INT\nwait $child\n");
    File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    var runtimeDirectory = Path.Combine(root, "runtime");
    await using var runtime = new SingBoxRuntime(script, runtimeDirectory, new SingBoxConfigWriter(), localPort: port, startupTimeout: TimeSpan.FromSeconds(3));
    await runtime.StartAsync(SampleProfile());
    var configPath = Path.Combine(runtimeDirectory, "runtime.json");
    var directoryPermissions = File.GetUnixFileMode(runtimeDirectory) & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
    var filePermissions = File.GetUnixFileMode(configPath) & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
    Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, directoryPermissions);
    Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, filePermissions);
    await runtime.StopAsync();
    Equal(false, File.Exists(configPath));
    Directory.Delete(root, true);
}

static async Task SingBoxConfigPassesRealEngineValidation()
{
    var executable = Environment.GetEnvironmentVariable("MAXSPEEDVPN_TEST_SING_BOX");
    if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
    {
        Console.WriteLine("SKIP real sing-box validation: MAXSPEEDVPN_TEST_SING_BOX is unset");
        return;
    }

    var configPath = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-config-{Guid.NewGuid():N}.json");
    await File.WriteAllTextAsync(configPath, new SingBoxConfigWriter().Write(SampleProfile(), 10808));
    try
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("check");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configPath);
        using var process = System.Diagnostics.Process.Start(startInfo) ?? throw new Exception("could not start real sing-box");
        await process.WaitForExitAsync();
        var error = await process.StandardError.ReadToEndAsync();
        if (process.ExitCode != 0) throw new Exception($"sing-box check failed: {error.Trim()}");
    }
    finally { File.Delete(configPath); }
}

static async Task XrayConfigPassesRealEngineValidation()
{
    var executable = Environment.GetEnvironmentVariable("MAXSPEEDVPN_TEST_XRAY");
    if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
    {
        Console.WriteLine("SKIP real Xray validation: MAXSPEEDVPN_TEST_XRAY is unset");
        return;
    }

    var configPath = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-xray-{Guid.NewGuid():N}.json");
    await File.WriteAllTextAsync(configPath, new XrayConfigWriter().Write(SampleProfile(), 10808));
    try
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("-test");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configPath);
        using var process = System.Diagnostics.Process.Start(startInfo) ?? throw new Exception("could not start real Xray");
        await process.WaitForExitAsync();
        var error = await process.StandardError.ReadToEndAsync();
        if (process.ExitCode != 0) throw new Exception($"Xray config test failed: {error.Trim()}");
    }
    finally { File.Delete(configPath); }
}

static VpnProfile SampleProfile() => new("nl", "Netherlands", "server.example", 443, "00000000-0000-0000-0000-000000000001", "reality", "cdn.example", "chrome", "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8", "ab");

static int GetFreePort()
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"expected '{expected}', actual '{actual}'");
}

static void Contains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
        throw new Exception($"missing '{expected}' in {actual}");
}

static void Sequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new Exception($"expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}]");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"expected {typeof(T).Name}");
}

static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception($"expected {typeof(T).Name}");
}

sealed class FakeRuntime : IProxyRuntime
{
    public event Action<string>? ExitedUnexpectedly;
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public Exception? StartError { get; init; }

    public Task StartAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        StartCount++;
        if (StartError is not null) throw StartError;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCount++;
        return Task.CompletedTask;
    }
}
