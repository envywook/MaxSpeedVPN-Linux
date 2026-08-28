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
    ("XrayConfig passes real engine validation when available", XrayConfigPassesRealEngineValidation),
    ("ProfileStore persists protocol metadata and private permissions", ProfileStorePersistsProtocolMetadataAndPrivatePermissions),
    ("ProfileStore upserts stable endpoint without Custom labels", ProfileStoreUpsertsStableEndpointWithoutCustomLabels),
    ("TcpLatencyProbe reports reachable and unreachable endpoints", TcpLatencyProbeReportsReachability),
    ("LiveLatencyMonitor refreshes all servers and stops cleanly", LiveLatencyMonitorRefreshesAllServersAndStopsCleanly),
    ("Naive profile parser preserves protocol metadata", NaiveProfileParserPreservesProtocolMetadata),
    ("Naive config passes real engine validation", NaiveConfigPassesRealEngineValidation),
    ("Mieru simple link parser preserves protocol metadata", MieruSimpleLinkParserPreservesProtocolMetadata),
    ("Mieru parser rejects lossy or unsupported bindings", MieruParserRejectsLossyBindings),
    ("Mieru runtime config disables unused RPC", MieruRuntimeConfigDisablesUnusedRpc),
    ("Mieru runtime adapter requires the native client", MieruRuntimeAdapterRequiresNativeClient),
    ("TUN request is fixed-scope and validates endpoint", TunRequestIsFixedScopeAndValidatesEndpoint),
    ("TUN transaction rolls back in reverse order", TunTransactionRollsBackInReverseOrder),
    ("TUN transaction rolls back after partial failure", TunTransactionRollsBackAfterPartialFailure),
    ("HWID store creates a stable URL-safe private identifier", HwidStoreCreatesStablePrivateIdentifier),
    ("Subscription parser imports supported lines and reports rejects", SubscriptionParserImportsSupportedLinesAndReportsRejects),
    ("Subscription store persists URL privately", SubscriptionStorePersistsUrlPrivately),
    ("Settings store persists engine and Russia routing choices", SettingsStorePersistsChoices),
    ("Core selector automatically picks a compatible installed engine", CoreSelectorPicksCompatibleInstalledEngine),
    ("Core selector rejects unsupported engine and protocol combinations", CoreSelectorRejectsUnsupportedCombination),
    ("Stored Xray writer accepts only compatible VLESS profiles", StoredXrayWriterAcceptsOnlyVlessProfiles),
    ("All-traffic routing keeps private networks direct", AllTrafficRoutingKeepsPrivateNetworksDirect),
    ("Regional routing is honestly gated without a pinned ruleset", RegionalRoutingIsGatedWithoutRuleset),
    ("Stored sing-box runtime reports controller state", StoredSingBoxRuntimeReportsControllerState),
    ("Subscription client forwards HWID without query-string leakage", SubscriptionClientForwardsHwidPrivately)
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

static async Task ProfileStorePersistsProtocolMetadataAndPrivatePermissions()
{
    var root = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-profiles-{Guid.NewGuid():N}");
    try
    {
        var store = new ProfileStore(root);
        var stored = StoredProfile.FromVpnProfile(SampleProfile());
        await store.UpsertAsync(stored);
        var loaded = await store.LoadAsync();
        Equal(1, loaded.Count);
        Equal("VLESS Reality", loaded[0].ProtocolLabel);
        Equal("vless", loaded[0].Protocol);
        Equal("Netherlands", loaded[0].Name);
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(Path.Combine(root, "profiles.json"));
            Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

static async Task ProfileStoreUpsertsStableEndpointWithoutCustomLabels()
{
    var root = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-profiles-{Guid.NewGuid():N}");
    try
    {
        var store = new ProfileStore(root);
        await store.UpsertAsync(StoredProfile.FromVpnProfile(SampleProfile()));
        await store.UpsertAsync(StoredProfile.FromVpnProfile(SampleProfile() with { Name = "NL Fast" }));
        var loaded = await store.LoadAsync();
        Equal(1, loaded.Count);
        Equal("NL Fast", loaded[0].Name);
        if (loaded[0].ProtocolLabel.Contains("Custom", StringComparison.OrdinalIgnoreCase))
            throw new Exception("protocol label must never degrade to Custom");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

static async Task TcpLatencyProbeReportsReachability()
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    var accept = listener.AcceptTcpClientAsync();
    var probe = new TcpLatencyProbe(TimeSpan.FromSeconds(2));
    var reachable = await probe.MeasureAsync("127.0.0.1", port);
    using var accepted = await accept;
    if (!reachable.IsReachable || reachable.Milliseconds is null) throw new Exception("reachable endpoint was not measured");
    listener.Stop();
    var unreachable = await probe.MeasureAsync("127.0.0.1", port);
    Equal(false, unreachable.IsReachable);
}

static async Task LiveLatencyMonitorRefreshesAllServersAndStopsCleanly()
{
    var probe = new FakeLatencyProbe();
    await using var monitor = new LiveLatencyMonitor(probe, TimeSpan.FromMilliseconds(20));
    var profiles = new[]
    {
        StoredProfile.FromVpnProfile(SampleProfile()),
        StoredProfile.FromVpnProfile(SampleProfile() with { Id = "second", Host = "second.example" })
    };
    var snapshots = new List<IReadOnlyDictionary<string, LatencyResult>>();
    monitor.Updated += value => snapshots.Add(value);
    await monitor.RefreshAsync(profiles);
    Equal(2, probe.Count);
    await monitor.StartAsync(() => profiles);
    await Task.Delay(75);
    await monitor.StopAsync();
    var stoppedAt = probe.Count;
    await Task.Delay(50);
    Equal(stoppedAt, probe.Count);
    if (snapshots.Count < 2) throw new Exception("live monitor did not publish refreshes");
}

static Task NaiveProfileParserPreservesProtocolMetadata()
{
    var profile = new ProfileParser().ParseStored("naive+https://alice:secret@naive.example:443#Naive%20Paris");
    Equal("naive", profile.Protocol);
    Equal("NaiveProxy", profile.ProtocolLabel);
    Equal("Naive Paris", profile.Name);
    Equal("naive.example", profile.Host);
    Equal(443, profile.Port);
    return Task.CompletedTask;
}

static async Task NaiveConfigPassesRealEngineValidation()
{
    var executable = Environment.GetEnvironmentVariable("MAXSPEEDVPN_TEST_SING_BOX");
    if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) throw new Exception("real sing-box is required");
    var profile = new ProfileParser().ParseStored("naive+https://alice:secret@naive.example:443#Naive");
    var configPath = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-naive-{Guid.NewGuid():N}.json");
    await File.WriteAllTextAsync(configPath, new SingBoxConfigWriter().Write(profile, 10808, enableTun: false));
    try
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo(executable) { RedirectStandardError = true, UseShellExecute = false };
        startInfo.ArgumentList.Add("check"); startInfo.ArgumentList.Add("-c"); startInfo.ArgumentList.Add(configPath);
        using var process = System.Diagnostics.Process.Start(startInfo) ?? throw new Exception("could not start sing-box");
        await process.WaitForExitAsync();
        var error = await process.StandardError.ReadToEndAsync();
        if (process.ExitCode != 0) throw new Exception($"Naive config check failed: {error.Trim()}");
    }
    finally { File.Delete(configPath); }
}

static Task MieruSimpleLinkParserPreservesProtocolMetadata()
{
    var profile = new ProfileParser().ParseStored("mierus://baozi:secret@1.2.3.4?profile=fast&port=6666&protocol=TCP");
    Equal("mieru", profile.Protocol);
    Equal("Mieru simple TCP", profile.ProtocolLabel);
    Equal("fast", profile.Name);
    Equal("1.2.3.4", profile.Host);
    Equal(6666, profile.Port);
    return Task.CompletedTask;
}

static Task MieruParserRejectsLossyBindings()
{
    var parser = new ProfileParser();
    Throws<FormatException>(() => parser.ParseStored("mierus://baozi:secret@1.2.3.4?profile=fast&port=6666&protocol=TCP&port=7777&protocol=TCP"));
    Throws<FormatException>(() => parser.ParseStored("mierus://baozi:secret@1.2.3.4?profile=fast&port=6666&protocol=UDP"));
    Throws<FormatException>(() => parser.ParseStored("mierus://baozi:secret@1.2.3.4?profile=fast&port=6666-6670&protocol=TCP"));
    Throws<FormatException>(() => parser.ParseStored("mierus://baozi:secret@1.2.3.4?profile=fast&port=6666&protocol=TCP&mtu=1400"));
    return Task.CompletedTask;
}

static Task MieruRuntimeAdapterRequiresNativeClient()
{
    var profile = new ProfileParser().ParseStored("mierus://baozi:secret@1.2.3.4?profile=fast&port=6666&protocol=TCP");
    Throws<FileNotFoundException>(() => MieruRuntimeAdapter.Create("/missing/mieru", profile, Path.GetTempPath(), 10808));
    return Task.CompletedTask;
}

static Task MieruRuntimeConfigDisablesUnusedRpc()
{
    var directory = Path.Combine(Path.GetTempPath(), $"maxspeed-mieru-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    var executable = Path.Combine(directory, "mieru");
    File.WriteAllText(executable, string.Empty);
    try
    {
        var profile = new ProfileParser().ParseStored("mierus://baozi:secret@1.2.3.4?profile=fast&port=6666&protocol=TCP");
        var spec = MieruRuntimeAdapter.Create(executable, profile, directory, 10808);
        using var json = System.Text.Json.JsonDocument.Parse(spec.ConfigJson);
        Equal(0, json.RootElement.GetProperty("rpcPort").GetInt32());
        Equal(10808, json.RootElement.GetProperty("socks5Port").GetInt32());
    }
    finally { Directory.Delete(directory, true); }
    return Task.CompletedTask;
}

static Task TunRequestIsFixedScopeAndValidatesEndpoint()
{
    var request = TunRequest.Create("1.2.3.4", 443);
    Equal("maxspeed0", request.InterfaceName);
    Equal("172.19.0.1/30", request.Address);
    Equal("1.2.3.4", request.ServerAddress);
    Throws<ArgumentException>(() => TunRequest.Create("; rm -rf /", 443));
    Throws<ArgumentOutOfRangeException>(() => TunRequest.Create("1.2.3.4", 0));
    return Task.CompletedTask;
}

static async Task TunTransactionRollsBackInReverseOrder()
{
    var executor = new RecordingTunExecutor();
    await using var transaction = await TunTransaction.ApplyAsync(executor, TunRequest.Create("1.2.3.4", 443));
    await transaction.DisposeAsync();
    Equal("AddTable,AddRule,AddRoute,DeleteRoute,DeleteRule,DeleteTable", string.Join(',', executor.Calls));
}

static async Task TunTransactionRollsBackAfterPartialFailure()
{
    var executor = new RecordingTunExecutor(failAt: "AddRoute");
    await ThrowsAsync<InvalidOperationException>(() => TunTransaction.ApplyAsync(executor, TunRequest.Create("1.2.3.4", 443)));
    Equal("AddTable,AddRule,AddRoute,DeleteRule,DeleteTable", string.Join(',', executor.Calls));
}

static async Task HwidStoreCreatesStablePrivateIdentifier()
{
    var root = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-hwid-{Guid.NewGuid():N}");
    var store = new HwidStore(root);
    var first = await store.GetOrCreateAsync();
    var second = await store.GetOrCreateAsync();
    Equal(first, second);
    if (first.Length is < 10 or > 64 || first.Any(character => !char.IsLetterOrDigit(character) && character is not '=' and not '-'))
        throw new Exception($"HWID is not URL-safe: {first}");
    if (!OperatingSystem.IsWindows())
        Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(root, "hwid")));
    Directory.Delete(root, true);
}

static Task SubscriptionParserImportsSupportedLinesAndReportsRejects()
{
    var vless = "vless://00000000-0000-0000-0000-000000000001@vpn.example.com:443?security=reality&sni=cdn.example.com&fp=chrome&pbk=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8&sid=ab&type=tcp#Amsterdam";
    var naive = "naive+https://user:pass@naive.example.com:443#Naive";
    var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{vless}\nvmess://unsupported\n{naive}\n"));
    var result = new SubscriptionParser().Parse(payload);
    Equal(2, result.Profiles.Count);
    Equal(1, result.RejectedLines.Count);
    Sequence(new[] { "VLESS Reality", "NaiveProxy" }, result.Profiles.Select(profile => profile.ProtocolLabel).ToArray());
    return Task.CompletedTask;
}

static async Task SubscriptionStorePersistsUrlPrivately()
{
    var root = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-subscriptions-{Guid.NewGuid():N}");
    var store = new SubscriptionStore(root);
    var subscription = SubscriptionDefinition.Create("Основная", "https://subscription.example/path?token=secret");
    await store.UpsertAsync(subscription);
    var loaded = await store.LoadAsync();
    Equal(1, loaded.Count);
    Equal(subscription.Url, loaded[0].Url);
    Equal("Основная", loaded[0].Name);
    if (!OperatingSystem.IsWindows())
        Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(root, "subscriptions.json")));
    Directory.Delete(root, true);
}

static async Task SettingsStorePersistsChoices()
{
    var root = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-settings-{Guid.NewGuid():N}");
    var store = new SettingsStore(root);
    var expected = new AppSettings(CorePreference.Xray, RussiaRoutingMode.OnlyUnavailable, true, false, false);
    await store.SaveAsync(expected);
    Equal(expected, await store.LoadAsync());
    Directory.Delete(root, true);
}

static Task CoreSelectorPicksCompatibleInstalledEngine()
{
    var vless = StoredProfile.FromVpnProfile(SampleProfile());
    var paths = new CorePaths("/opt/maxspeedvpn/bin/sing-box", "/opt/maxspeedvpn/bin/xray", "/opt/maxspeedvpn/bin/mieru");
    var selector = new CoreSelector(path => path != paths.Mieru);
    Equal(CoreKind.Xray, selector.Select(vless, CorePreference.Auto, paths).Kind);
    Equal(CoreKind.SingBox, selector.Select(vless, CorePreference.SingBox, paths).Kind);
    var naive = new ProfileParser().ParseStored("naive+https://user:pass@naive.example.com:443#Naive");
    Equal(CoreKind.SingBox, selector.Select(naive, CorePreference.Auto, paths).Kind);
    return Task.CompletedTask;
}

static Task CoreSelectorRejectsUnsupportedCombination()
{
    var paths = new CorePaths("/opt/maxspeedvpn/bin/sing-box", "/opt/maxspeedvpn/bin/xray", "/opt/maxspeedvpn/bin/mieru");
    var selector = new CoreSelector(_ => true);
    var naive = new ProfileParser().ParseStored("naive+https://user:pass@naive.example.com:443#Naive");
    Throws<NotSupportedException>(() => selector.Select(naive, CorePreference.Xray, paths));
    var mieru = new ProfileParser().ParseStored("mierus://user:pass@1.2.3.4?profile=fast&port=6666&protocol=TCP");
    Equal(CoreKind.Mieru, selector.Select(mieru, CorePreference.Auto, paths).Kind);
    return Task.CompletedTask;
}

static Task StoredXrayWriterAcceptsOnlyVlessProfiles()
{
    var vless = StoredProfile.FromVpnProfile(SampleProfile());
    Contains("\"protocol\": \"vless\"", new StoredXrayConfigWriter(vless).Write(SampleProfile(), 10808));
    var naive = new ProfileParser().ParseStored("naive+https://user:pass@naive.example.com:443#Naive");
    Throws<NotSupportedException>(() => new StoredXrayConfigWriter(naive).Write(SampleProfile(), 10808));
    return Task.CompletedTask;
}

static Task AllTrafficRoutingKeepsPrivateNetworksDirect()
{
    var profile = StoredProfile.FromVpnProfile(SampleProfile());
    var singBox = new StoredSingBoxConfigWriter(profile, new RoutingOptions(RussiaRoutingMode.AllTraffic, true)).Write(SampleProfile(), 10808);
    Contains("\"ip_is_private\": true", singBox);
    Contains("\"action\": \"route\"", singBox);
    Contains("\"outbound\": \"direct\"", singBox);
    var xray = new StoredXrayConfigWriter(profile, new RoutingOptions(RussiaRoutingMode.AllTraffic, true)).Write(SampleProfile(), 10808);
    Contains("geoip:private", xray);
    Contains("\"outboundTag\": \"direct\"", xray);
    return Task.CompletedTask;
}

static Task RegionalRoutingIsGatedWithoutRuleset()
{
    var profile = StoredProfile.FromVpnProfile(SampleProfile());
    Throws<NotSupportedException>(() => new StoredSingBoxConfigWriter(profile, new RoutingOptions(RussiaRoutingMode.OnlyUnavailable, true)).Write(SampleProfile(), 10808));
    Throws<NotSupportedException>(() => new StoredXrayConfigWriter(profile, new RoutingOptions(RussiaRoutingMode.OnlyUnavailable, true)).Write(SampleProfile(), 10808));
    return Task.CompletedTask;
}

static async Task StoredSingBoxRuntimeReportsControllerState()
{
    var root = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-stored-runtime-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var script = Path.Combine(root, "fake-sing-box");
    var port = GetFreePort();
    await File.WriteAllTextAsync(script, $"#!/bin/sh\npython3 -m http.server {port} --bind 127.0.0.1 >/dev/null 2>&1 & child=$!\ntrap 'kill $child 2>/dev/null; exit 0' TERM INT\nwait $child\n");
    if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    await using var runtime = new StoredSingBoxRuntime(script, root, StoredProfile.FromVpnProfile(SampleProfile()), localPort: port, startupTimeout: TimeSpan.FromSeconds(3));
    var controller = new ConnectionController(runtime);
    try
    {
        await controller.ConnectAsync(SampleProfile());
        Equal(ConnectionState.Connected, controller.State);
        await controller.DisconnectAsync();
        Equal(ConnectionState.Disconnected, controller.State);
    }
    finally { Directory.Delete(root, true); }
}

static async Task SubscriptionClientForwardsHwidPrivately()
{
    var handler = new RecordingHttpHandler("dmxlc3M6Ly9leGFtcGxl");
    using var client = new SubscriptionClient(new System.Net.Http.HttpClient(handler));
    Equal("dmxlc3M6Ly9leGFtcGxl", await client.DownloadAsync("https://subscription.example/path?token=secret", "device-id"));
    Equal("device-id", handler.Request?.Headers.GetValues("X-Device-ID").Single());
    Equal("token=secret", handler.Request?.RequestUri?.Query.TrimStart('?'));
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

sealed class RecordingTunExecutor : ITunExecutor
{
    private readonly string? _failAt;
    public List<string> Calls { get; } = [];
    public RecordingTunExecutor(string? failAt = null) => _failAt = failAt;
    private Task Record(string call)
    {
        Calls.Add(call);
        if (_failAt == call) throw new InvalidOperationException(call);
        return Task.CompletedTask;
    }
    public Task AddTableAsync(TunRequest request, CancellationToken cancellationToken) => Record("AddTable");
    public Task AddRuleAsync(TunRequest request, CancellationToken cancellationToken) => Record("AddRule");
    public Task AddRouteAsync(TunRequest request, CancellationToken cancellationToken) => Record("AddRoute");
    public Task DeleteRouteAsync(TunRequest request, CancellationToken cancellationToken) => Record("DeleteRoute");
    public Task DeleteRuleAsync(TunRequest request, CancellationToken cancellationToken) => Record("DeleteRule");
    public Task DeleteTableAsync(TunRequest request, CancellationToken cancellationToken) => Record("DeleteTable");
}

sealed class FakeLatencyProbe : ILatencyProbe
{
    public int Count { get; private set; }
    public Task<LatencyResult> MeasureAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        Count++;
        return Task.FromResult(new LatencyResult(true, 12, DateTimeOffset.UtcNow));
    }
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

sealed class RecordingHttpHandler(string response) : System.Net.Http.HttpMessageHandler
{
    public System.Net.Http.HttpRequestMessage? Request { get; private set; }
    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(response)
        });
    }
}
