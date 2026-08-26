namespace ServiceLib.Models;

public sealed record MaxSpeedVpnDashboardStatus(string Label, string Accent, bool IsConnected)
{
    public static MaxSpeedVpnDashboardStatus From(EMaxSpeedVpnSessionState state) => state switch
    {
        EMaxSpeedVpnSessionState.Preparing => new("Подготовка", "#FBBF24", false),
        EMaxSpeedVpnSessionState.Connecting => new("Подключение", "#38BDF8", false),
        EMaxSpeedVpnSessionState.Connected => new("Подключено", "#4ADE80", true),
        EMaxSpeedVpnSessionState.Disconnecting => new("Отключение", "#FBBF24", false),
        EMaxSpeedVpnSessionState.Error => new("Ошибка", "#FB7185", false),
        _ => new("Отключено", "#94A3B8", false)
    };
}
