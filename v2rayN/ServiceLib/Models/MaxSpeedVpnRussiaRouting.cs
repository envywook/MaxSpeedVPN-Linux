namespace ServiceLib.Models;

public enum EMaxSpeedVpnRussiaPreset
{
    Optimized,
    BlockedOnly,
    FullVpn
}

public enum EMaxSpeedVpnRouteTarget
{
    Direct,
    Proxy,
    Block
}

public sealed record MaxSpeedVpnRussiaRoutingOptions(EMaxSpeedVpnRussiaPreset Preset)
{
    public bool AllowLan { get; init; } = true;
    public bool DirectBankAndGovernment { get; init; } = true;
    public bool EnableAdBlocking { get; init; }
    public IReadOnlyList<string> UserBlockRules { get; init; } = [];
    public IReadOnlyList<string> UserDirectRules { get; init; } = [];
    public IReadOnlyList<string> UserProxyRules { get; init; } = [];
}

public sealed record MaxSpeedVpnLogicalRouteRule(
    string Id,
    EMaxSpeedVpnRouteTarget Target,
    IReadOnlyList<string> Sources);

public static class RussiaRoutingPresetCompiler
{
    public static IReadOnlyList<MaxSpeedVpnLogicalRouteRule> Compile(MaxSpeedVpnRussiaRoutingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var rules = new List<MaxSpeedVpnLogicalRouteRule>
        {
            new("service-bypass", EMaxSpeedVpnRouteTarget.Direct, ["active-endpoint", "bootstrap-dns"]),
            new("lan", options.AllowLan ? EMaxSpeedVpnRouteTarget.Direct : EMaxSpeedVpnRouteTarget.Block,
                ["connected-networks", "private", "link-local", "multicast"])
        };

        AddUserRule(rules, "user.block", EMaxSpeedVpnRouteTarget.Block, options.UserBlockRules);
        AddUserRule(rules, "user.direct", EMaxSpeedVpnRouteTarget.Direct, options.UserDirectRules);
        AddUserRule(rules, "user.proxy", EMaxSpeedVpnRouteTarget.Proxy, options.UserProxyRules);

        if (options.DirectBankAndGovernment)
        {
            rules.Add(new("compat.bank-gov", EMaxSpeedVpnRouteTarget.Direct,
                ["category-bank-ru", "category-gov-ru"]));
        }

        if (options.EnableAdBlocking)
        {
            rules.Add(new("ads", EMaxSpeedVpnRouteTarget.Block, ["category-ads"]));
        }

        if (options.Preset is EMaxSpeedVpnRussiaPreset.Optimized or EMaxSpeedVpnRussiaPreset.BlockedOnly)
        {
            rules.Add(new("ru.blocked.domain", EMaxSpeedVpnRouteTarget.Proxy, ["ru-blocked"]));
            rules.Add(new("ru.blocked.ip", EMaxSpeedVpnRouteTarget.Proxy, ["ru-blocked-ip"]));
        }

        if (options.Preset == EMaxSpeedVpnRussiaPreset.Optimized)
        {
            rules.Add(new("ru.inside-only", EMaxSpeedVpnRouteTarget.Direct, ["ru-available-only-inside"]));
            rules.Add(new("ru.domain", EMaxSpeedVpnRouteTarget.Direct, ["category-ru"]));
            rules.Add(new("ru.ip", EMaxSpeedVpnRouteTarget.Direct, ["geoip-ru"]));
        }

        rules.Add(new("final", options.Preset == EMaxSpeedVpnRussiaPreset.BlockedOnly
            ? EMaxSpeedVpnRouteTarget.Direct
            : EMaxSpeedVpnRouteTarget.Proxy, []));
        return rules;
    }

    private static void AddUserRule(
        List<MaxSpeedVpnLogicalRouteRule> rules,
        string id,
        EMaxSpeedVpnRouteTarget target,
        IReadOnlyList<string> sources)
    {
        if (sources.Count > 0)
        {
            rules.Add(new(id, target, sources));
        }
    }
}
