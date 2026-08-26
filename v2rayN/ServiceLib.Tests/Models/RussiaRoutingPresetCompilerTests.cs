namespace ServiceLib.Tests.Models;

public class RussiaRoutingPresetCompilerTests
{
    [Test]
    [Arguments(EMaxSpeedVpnRussiaPreset.Optimized, EMaxSpeedVpnRouteTarget.Proxy)]
    [Arguments(EMaxSpeedVpnRussiaPreset.BlockedOnly, EMaxSpeedVpnRouteTarget.Direct)]
    [Arguments(EMaxSpeedVpnRussiaPreset.FullVpn, EMaxSpeedVpnRouteTarget.Proxy)]
    public async Task FinalTarget_MatchesPreset(
        EMaxSpeedVpnRussiaPreset preset,
        EMaxSpeedVpnRouteTarget expected)
    {
        var rules = RussiaRoutingPresetCompiler.Compile(new(preset));

        await rules[^1].Id.Should().BeEqualTo("final");
        await rules[^1].Target.Should().BeEqualTo(expected);
    }

    [Test]
    public async Task Optimized_PrioritizesBlockedRussiaBeforeBroadRussiaDirect()
    {
        var rules = RussiaRoutingPresetCompiler.Compile(new(EMaxSpeedVpnRussiaPreset.Optimized));

        await IndexOf(rules, "ru.blocked.domain").Should().BeLessThan(IndexOf(rules, "ru.domain"));
        await IndexOf(rules, "ru.blocked.ip").Should().BeLessThan(IndexOf(rules, "ru.ip"));
    }

    [Test]
    public async Task UserRules_HaveDocumentedPriority()
    {
        var options = new MaxSpeedVpnRussiaRoutingOptions(EMaxSpeedVpnRussiaPreset.Optimized)
        {
            UserBlockRules = ["ads.example"],
            UserDirectRules = ["direct.example"],
            UserProxyRules = ["proxy.example"]
        };

        var rules = RussiaRoutingPresetCompiler.Compile(options);

        await IndexOf(rules, "user.block").Should().BeLessThan(IndexOf(rules, "compat.bank-gov"));
        await IndexOf(rules, "user.direct").Should().BeLessThan(IndexOf(rules, "compat.bank-gov"));
        await IndexOf(rules, "user.proxy").Should().BeLessThan(IndexOf(rules, "compat.bank-gov"));
    }

    [Test]
    public async Task FullVpn_WithoutCompatibility_DoesNotBypassBankAndGovernment()
    {
        var rules = RussiaRoutingPresetCompiler.Compile(new(EMaxSpeedVpnRussiaPreset.FullVpn)
        {
            DirectBankAndGovernment = false
        });

        await rules.Any(x => x.Id == "compat.bank-gov").Should().BeFalse();
    }

    [Test]
    public async Task LanDisabled_IsBlockedInsteadOfProxied()
    {
        var rules = RussiaRoutingPresetCompiler.Compile(new(EMaxSpeedVpnRussiaPreset.Optimized)
        {
            AllowLan = false
        });
        var lan = rules.Single(x => x.Id == "lan");

        await lan.Target.Should().BeEqualTo(EMaxSpeedVpnRouteTarget.Block);
    }

    private static int IndexOf(IReadOnlyList<MaxSpeedVpnLogicalRouteRule> rules, string id)
        => rules.Select((rule, index) => (rule, index)).Single(x => x.rule.Id == id).index;
}
