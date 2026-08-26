namespace ServiceLib.Tests.Models;

public class MaxSpeedVpnDashboardStatusTests
{
    [Test]
    public async Task ConnectedState_UsesConnectedLabelAndAccent()
    {
        var status = MaxSpeedVpnDashboardStatus.From(EMaxSpeedVpnSessionState.Connected);

        await status.Label.Should().BeEqualTo("Подключено");
        await status.Accent.Should().BeEqualTo("#4ADE80");
        await status.IsConnected.Should().BeTrue();
    }

    [Test]
    public async Task ErrorState_IsNeverReportedAsConnected()
    {
        var status = MaxSpeedVpnDashboardStatus.From(EMaxSpeedVpnSessionState.Error);

        await status.Label.Should().BeEqualTo("Ошибка");
        await status.IsConnected.Should().BeFalse();
    }
}
