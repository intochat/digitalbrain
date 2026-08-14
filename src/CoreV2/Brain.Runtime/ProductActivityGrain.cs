using System.Security.Cryptography;
using System.Text;
using Brain.Runtime.Abstractions;
using Orleans.Runtime;

namespace Brain.Runtime;

public sealed class ProductActivityGrain(
    [PersistentState("activity", "Default")]
    IPersistentState<ProductActivityState> state,
    IEnumerable<IRuntimeProductModule> modules) : Grain, IProductActivityGrain
{
    private readonly IPersistentState<ProductActivityState> _state = state;
    private readonly IReadOnlyDictionary<string, IRuntimeProductModule> _operations = modules
        .SelectMany(module => module.Operations.Select(operation => (operation.Id, Module: module)))
        .ToDictionary(static binding => binding.Id, static binding => binding.Module, StringComparer.Ordinal);

    public async Task<RuntimeActivityReceipt> StartAsync(RuntimeInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var activity = this.GetPrimaryKey();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(invocation.InputJson)));
        if (_state.State.Initialized)
        {
            if (!Matches(invocation, fingerprint))
            {
                throw new InvalidOperationException(
                    "An idempotency key cannot be reused for a different operation or input.");
            }

            if (_state.State.Status is RuntimeActivityStatus.Accepted or RuntimeActivityStatus.Running)
            {
                await ExecuteAsync();
            }

            return new RuntimeActivityReceipt(activity, _state.State.OperationId);
        }

        if (!_operations.ContainsKey(invocation.OperationId))
        {
            throw new KeyNotFoundException($"Operation '{invocation.OperationId}' is not installed.");
        }

        _state.State.Initialized = true;
        _state.State.OperationId = invocation.OperationId;
        _state.State.InputJson = invocation.InputJson;
        _state.State.InputHash = fingerprint;
        _state.State.Workspace = invocation.Workspace;
        _state.State.Principal = invocation.Principal;
        _state.State.IdempotencyKey = invocation.IdempotencyKey;
        _state.State.Status = RuntimeActivityStatus.Accepted;
        _state.State.Sequence = 1;
        await _state.WriteStateAsync();
        await ExecuteAsync();
        return new RuntimeActivityReceipt(activity, invocation.OperationId);
    }

    public Task<RuntimeActivitySnapshot?> GetAsync(string workspace)
    {
        if (!_state.State.Initialized
            || !string.Equals(_state.State.Workspace, workspace, StringComparison.Ordinal))
        {
            return Task.FromResult<RuntimeActivitySnapshot?>(null);
        }

        return Task.FromResult<RuntimeActivitySnapshot?>(new RuntimeActivitySnapshot(
            this.GetPrimaryKey(),
            _state.State.OperationId,
            _state.State.Workspace,
            _state.State.Status,
            _state.State.Sequence,
            _state.State.ResultJson,
            _state.State.Problem));
    }

    private async Task ExecuteAsync()
    {
        _state.State.Status = RuntimeActivityStatus.Running;
        _state.State.Sequence++;
        await _state.WriteStateAsync();
        try
        {
            _state.State.ResultJson = await _operations[_state.State.OperationId]
                .ExecuteAsync(_state.State.OperationId, _state.State.InputJson, CancellationToken.None);
            _state.State.Status = RuntimeActivityStatus.Completed;
            _state.State.Problem = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _state.State.Status = RuntimeActivityStatus.Failed;
            _state.State.Problem = exception.Message;
            _state.State.ResultJson = null;
        }

        _state.State.Sequence++;
        await _state.WriteStateAsync();
    }

    private bool Matches(RuntimeInvocation invocation, string fingerprint)
        => string.Equals(_state.State.OperationId, invocation.OperationId, StringComparison.Ordinal)
            && string.Equals(_state.State.InputHash, fingerprint, StringComparison.Ordinal)
            && string.Equals(_state.State.Workspace, invocation.Workspace, StringComparison.Ordinal)
            && string.Equals(_state.State.Principal, invocation.Principal, StringComparison.Ordinal)
            && string.Equals(_state.State.IdempotencyKey, invocation.IdempotencyKey, StringComparison.Ordinal);
}
