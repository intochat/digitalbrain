using Orleans;

namespace Brain.Testing;

public interface IProofRuntimeGrain : IGrainWithStringKey
{
    Task<string> InvokeRunAsync(string value, string workspace, string principal, string key);

    Task<string> InvokeCorrectionAsync(string requestedRoute, string workspace, string principal, string key);

    Task<string[]> ObserveAsync(string activity, string workspace, string principal);

    Task<string> ReadProofResultAsync(string payload, string workspace, string principal);

    Task<string> ReadCorrectionResultAsync(string payload, string workspace, string principal);

    Task<Guid> InstanceIdAsync();

    Task<int> DispatchCountAsync();

    Task<string[]> RewireEvidenceAsync();
}

public sealed class ProofRuntimeGrain(ProofRuntime runtime) : Grain, IProofRuntimeGrain
{
    private readonly ProofRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public async Task<string> InvokeRunAsync(string value, string workspace, string principal, string key)
        => (await _runtime.InvokeRunAsync(value, workspace, principal, key)).Activity.Value.ToString("N");

    public async Task<string> InvokeCorrectionAsync(string requestedRoute, string workspace, string principal, string key)
        => (await _runtime.InvokeCorrectionAsync(requestedRoute, workspace, principal, key)).Activity.Value.ToString("N");

    public async Task<string[]> ObserveAsync(string activity, string workspace, string principal)
    {
        var view = await _runtime.ObserveAsync(activity, workspace, principal);
        return [view.Operation.Value, view.Status.ToString(), view.TerminalResultContract.Value, view.Result?.Payload.Value ?? string.Empty];
    }

    public async Task<string> ReadProofResultAsync(string payload, string workspace, string principal)
        => (await _runtime.ReadProofResultAsync(payload, workspace, principal)).Route;

    public async Task<string> ReadCorrectionResultAsync(string payload, string workspace, string principal)
        => (await _runtime.ReadCorrectionResultAsync(payload, workspace, principal)).AppliedRoute;

    public Task<Guid> InstanceIdAsync() => Task.FromResult(_runtime.InstanceId);

    public Task<int> DispatchCountAsync() => Task.FromResult(_runtime.DispatchCount);

    public Task<string[]> RewireEvidenceAsync() => Task.FromResult(_runtime.RewireEvidence.ToArray());
}
