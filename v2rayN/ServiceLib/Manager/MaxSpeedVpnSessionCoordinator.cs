namespace ServiceLib.Manager;

public interface IMaxSpeedVpnCoreRuntime
{
    Task Prepare(CancellationToken cancellationToken);
    Task Start(CancellationToken cancellationToken);
    Task WaitUntilReady(CancellationToken cancellationToken);
    Task Stop(CancellationToken cancellationToken);
    Task RestoreNetwork(CancellationToken cancellationToken);
}

public sealed class MaxSpeedVpnSessionCoordinator(
    IMaxSpeedVpnCoreRuntime runtime,
    Func<string, Task> writeLog)
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly MaxSpeedVpnSessionStateMachine _state = new();

    public EMaxSpeedVpnSessionState State => _state.State;

    public async Task<bool> Connect(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            if (!_state.BeginConnect())
            {
                return State == EMaxSpeedVpnSessionState.Connected;
            }

            await writeLog("[MaxSpeedVPN] Подготовка подключения");
            try
            {
                await runtime.Prepare(cancellationToken);
                if (!_state.MarkPrepared())
                {
                    return false;
                }

                await runtime.Start(cancellationToken);
                await runtime.WaitUntilReady(cancellationToken);
                if (!_state.MarkConnected())
                {
                    return false;
                }

                await writeLog("[MaxSpeedVPN] Подключено");
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await CleanupAfterFailedConnect(cancellationToken);
                _state.MarkError();
                await writeLog($"[MaxSpeedVPN] Ошибка подключения: {ex.Message}");
                return false;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<bool> Disconnect(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            if (State == EMaxSpeedVpnSessionState.Disconnected)
            {
                return true;
            }
            if (!_state.BeginDisconnect())
            {
                return State == EMaxSpeedVpnSessionState.Disconnected;
            }

            await writeLog("[MaxSpeedVPN] Отключение");
            var failures = new List<Exception>();
            await TryCleanupStep(() => runtime.Stop(cancellationToken), failures);
            await TryCleanupStep(() => runtime.RestoreNetwork(cancellationToken), failures);

            if (failures.Count > 0)
            {
                _state.MarkCleanupFailed();
                await writeLog($"[MaxSpeedVPN] Ошибка отключения: {failures[0].Message}");
                return false;
            }

            _state.MarkDisconnected();
            await writeLog("[MaxSpeedVPN] Отключено");
            return true;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task CleanupAfterFailedConnect(CancellationToken cancellationToken)
    {
        var ignored = new List<Exception>();
        await TryCleanupStep(() => runtime.Stop(cancellationToken), ignored);
        await TryCleanupStep(() => runtime.RestoreNetwork(cancellationToken), ignored);
    }

    private static async Task TryCleanupStep(Func<Task> step, List<Exception> failures)
    {
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
    }
}
