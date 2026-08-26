namespace ServiceLib.Tests.Models;

public class MaxSpeedVpnDesktopSettingsTests
{
    [Test]
    public async Task Defaults_AreDarkOpaqueAndShowDashboardLogs()
    {
        var settings = new MaxSpeedVpnDesktopSettings();

        await settings.DarkTheme.Should().BeTrue();
        await settings.WindowOpacityPercent.Should().BeEqualTo(100);
        await settings.ShowLogsOnDashboard.Should().BeTrue();
    }

    [Test]
    [Arguments(69, 70)]
    [Arguments(70, 70)]
    [Arguments(85, 85)]
    [Arguments(100, 100)]
    [Arguments(101, 100)]
    public async Task NormalizeOpacity_ClampsToReadableRange(int requested, int expected)
    {
        await MaxSpeedVpnDesktopSettings.NormalizeOpacity(requested).Should().BeEqualTo(expected);
    }
}
