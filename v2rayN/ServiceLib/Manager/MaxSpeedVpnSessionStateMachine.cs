namespace ServiceLib.Manager;

public enum EMaxSpeedVpnSessionState
{
    Disconnected,
    Preparing,
    Connecting,
    Connected,
    Disconnecting,
    Error
}

public sealed class MaxSpeedVpnSessionStateMachine
{
    private readonly Lock _gate = new();

    public EMaxSpeedVpnSessionState State { get; private set; } = EMaxSpeedVpnSessionState.Disconnected;

    public bool BeginConnect()
    {
        lock (_gate)
        {
            if (State != EMaxSpeedVpnSessionState.Disconnected)
            {
                return false;
            }

            State = EMaxSpeedVpnSessionState.Preparing;
            return true;
        }
    }

    public bool MarkPrepared() => Transition(EMaxSpeedVpnSessionState.Preparing, EMaxSpeedVpnSessionState.Connecting);

    public bool MarkConnected() => Transition(EMaxSpeedVpnSessionState.Connecting, EMaxSpeedVpnSessionState.Connected);

    public bool BeginDisconnect()
    {
        lock (_gate)
        {
            if (State is EMaxSpeedVpnSessionState.Disconnected or EMaxSpeedVpnSessionState.Disconnecting)
            {
                return false;
            }

            State = EMaxSpeedVpnSessionState.Disconnecting;
            return true;
        }
    }

    public bool MarkDisconnected() => Transition(EMaxSpeedVpnSessionState.Disconnecting, EMaxSpeedVpnSessionState.Disconnected);

    public bool MarkError()
    {
        lock (_gate)
        {
            if (State is EMaxSpeedVpnSessionState.Disconnected or EMaxSpeedVpnSessionState.Disconnecting)
            {
                return false;
            }

            State = EMaxSpeedVpnSessionState.Error;
            return true;
        }
    }

    public bool MarkCleanupFailed()
    {
        lock (_gate)
        {
            if (State != EMaxSpeedVpnSessionState.Disconnecting)
            {
                return false;
            }

            State = EMaxSpeedVpnSessionState.Error;
            return true;
        }
    }

    private bool Transition(EMaxSpeedVpnSessionState expected, EMaxSpeedVpnSessionState next)
    {
        lock (_gate)
        {
            if (State != expected)
            {
                return false;
            }

            State = next;
            return true;
        }
    }
}
