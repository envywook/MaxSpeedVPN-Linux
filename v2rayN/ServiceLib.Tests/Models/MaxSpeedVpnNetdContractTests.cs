namespace ServiceLib.Tests.Models;

public class MaxSpeedVpnNetdContractTests
{
    [Test]
    public async Task ConnectRequest_RejectsPathsAndArbitraryArguments()
    {
        await MaxSpeedVpnNetdContract.IsValidProfileId("profile-01").Should().BeTrue();
        await MaxSpeedVpnNetdContract.IsValidProfileId("../../etc/shadow").Should().BeFalse();
        await MaxSpeedVpnNetdContract.IsValidProfileId("profile --config /tmp/x").Should().BeFalse();
    }

    [Test]
    public async Task ObjectNames_AreProductScoped()
    {
        await MaxSpeedVpnNetdContract.BusName.Should().BeEqualTo("com.maxspeedvpn.Netd1");
        await MaxSpeedVpnNetdContract.NftTable.Should().BeEqualTo("maxspeedvpn");
        await MaxSpeedVpnNetdContract.TunName.Should().BeEqualTo("msvpn0");
    }
}
