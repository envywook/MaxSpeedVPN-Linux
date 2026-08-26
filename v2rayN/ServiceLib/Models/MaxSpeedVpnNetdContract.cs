namespace ServiceLib.Models;

public static partial class MaxSpeedVpnNetdContract
{
    public const string BusName = "com.maxspeedvpn.Netd1";
    public const string ObjectPath = "/com/maxspeedvpn/Netd1";
    public const string NftTable = "maxspeedvpn";
    public const string TunName = "msvpn0";
    public const int RoutingTable = 20128;
    public const int RoutingRulePriority = 20128;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileIdRegex();

    public static bool IsValidProfileId(string? profileId) =>
        profileId is not null && ProfileIdRegex().IsMatch(profileId);
}
