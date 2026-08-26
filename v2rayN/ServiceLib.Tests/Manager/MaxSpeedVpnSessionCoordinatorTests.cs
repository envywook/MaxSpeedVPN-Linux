namespace ServiceLib.Tests.Manager;

public class MaxSpeedVpnSessionCoordinatorTests
{
    [Test]
    public async Task Connect_TransitionsAndWritesDashboardLog()
    {
        var runtime = new RecordingRuntime();
        var logs = new List<string>();
        var coordinator = new MaxSpeedVpnSessionCoordinator(runtime, message =>
        {
            logs.Add(message);
            return Task.CompletedTask;
        });

        var connected = await coordinator.Connect();

        await connected.Should().BeTrue();
        await coordinator.State.Should().BeEqualTo(EMaxSpeedVpnSessionState.Connected);
        await runtime.Calls.Should().BeEquivalentTo(["prepare", "start", "ready"]);
        await logs.Should().Contain(x => x.Contains("Подключено"));
    }

    [Test]
    public async Task ConnectFailure_CleansUpAndEntersError()
    {
        var runtime = new RecordingRuntime { ThrowOnStart = true };
        var coordinator = new MaxSpeedVpnSessionCoordinator(runtime, _ => Task.CompletedTask);

        var connected = await coordinator.Connect();

        await connected.Should().BeFalse();
        await coordinator.State.Should().BeEqualTo(EMaxSpeedVpnSessionState.Error);
        await runtime.Calls.Should().BeEquivalentTo(["prepare", "start", "stop", "restore"]);
    }

    [Test]
    public async Task Disconnect_ContinuesCleanupAfterStopFailure()
    {
        var runtime = new RecordingRuntime { ThrowOnStop = true };
        var coordinator = new MaxSpeedVpnSessionCoordinator(runtime, _ => Task.CompletedTask);
        await coordinator.Connect();

        var disconnected = await coordinator.Disconnect();

        await disconnected.Should().BeFalse();
        await coordinator.State.Should().BeEqualTo(EMaxSpeedVpnSessionState.Error);
        await runtime.Calls.Should().Contain("restore");
    }

    [Test]
    public async Task ConcurrentConnect_StartsCoreOnce()
    {
        var runtime = new RecordingRuntime();
        var coordinator = new MaxSpeedVpnSessionCoordinator(runtime, _ => Task.CompletedTask);

        var results = await Task.WhenAll(coordinator.Connect(), coordinator.Connect());

        await results.All(x => x).Should().BeTrue();
        await runtime.Calls.Count(x => x == "start").Should().BeEqualTo(1);
    }

    private sealed class RecordingRuntime : IMaxSpeedVpnCoreRuntime
    {
        public List<string> Calls { get; } = [];
        public bool ThrowOnStart { get; init; }
        public bool ThrowOnStop { get; init; }

        public Task Prepare(CancellationToken cancellationToken)
        {
            Calls.Add("prepare");
            return Task.CompletedTask;
        }

        public Task Start(CancellationToken cancellationToken)
        {
            Calls.Add("start");
            return ThrowOnStart ? Task.FromException(new InvalidOperationException("start failed")) : Task.CompletedTask;
        }

        public Task WaitUntilReady(CancellationToken cancellationToken)
        {
            Calls.Add("ready");
            return Task.CompletedTask;
        }

        public Task Stop(CancellationToken cancellationToken)
        {
            Calls.Add("stop");
            return ThrowOnStop ? Task.FromException(new InvalidOperationException("stop failed")) : Task.CompletedTask;
        }

        public Task RestoreNetwork(CancellationToken cancellationToken)
        {
            Calls.Add("restore");
            return Task.CompletedTask;
        }
    }
}