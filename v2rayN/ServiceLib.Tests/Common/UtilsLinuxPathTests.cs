namespace ServiceLib.Tests.Common;

public class UtilsLinuxPathTests
{
    [Test]
    public async Task StartupPath_UsesXdgDataHomeOnLinux()
    {
        if (!Utils.IsLinux())
        {
            return;
        }

        var original = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var expectedRoot = Path.Combine(Path.GetTempPath(), $"maxspeedvpn-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", expectedRoot);
            await Assert.That(Utils.StartupPath()).IsEqualTo(Path.Combine(expectedRoot, "MaxSpeedVPN"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", original);
        }
    }
}
