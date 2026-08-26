namespace ServiceLib.Tests.Manager;

public class MaxSpeedVpnSessionStateMachineTests
{
    [Test]
    public async Task NewSession_IsDisconnected()
    {
        var session = new MaxSpeedVpnSessionStateMachine();

        await session.State.Should().BeEqualTo(EMaxSpeedVpnSessionState.Disconnected);
    }

    [Test]
    public async Task ConnectFlow_ReachesConnected()
    {
        var session = new MaxSpeedVpnSessionStateMachine();

        await session.BeginConnect().Should().BeTrue();
        await session.MarkPrepared().Should().BeTrue();
        await session.MarkConnected().Should().BeTrue();
        await session.State.Should().BeEqualTo(EMaxSpeedVpnSessionState.Connected);
    }

    [Test]
    public async Task Disconnect_IsIdempotentWhileDisconnected()
    {
        var session = new MaxSpeedVpnSessionStateMachine();

        await session.BeginDisconnect().Should().BeFalse();
        await session.State.Should().BeEqualTo(EMaxSpeedVpnSessionState.Disconnected);
    }

    [Test]
    public async Task DisconnectDuringConnect_AlwaysAllowsCleanup()
    {
        var session = new MaxSpeedVpnSessionStateMachine();

        await session.BeginConnect().Should().BeTrue();
        await session.BeginDisconnect().Should().BeTrue();
        await session.MarkDisconnected().Should().BeTrue();
        await session.State.Should().BeEqualTo(EMaxSpeedVpnSessionState.Disconnected);
    }

    [Test]
    public async Task Error_CanOnlyRecoverThroughDisconnectCleanup()
    {
        var session = new MaxSpeedVpnSessionStateMachine();

        await session.BeginConnect().Should().BeTrue();
        await session.MarkError().Should().BeTrue();
        await session.BeginConnect().Should().BeFalse();
        await session.BeginDisconnect().Should().BeTrue();
        await session.MarkDisconnected().Should().BeTrue();
        await session.State.Should().BeEqualTo(EMaxSpeedVpnSessionState.Disconnected);
    }
}
