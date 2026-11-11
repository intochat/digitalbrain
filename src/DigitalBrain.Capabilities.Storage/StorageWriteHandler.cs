using System.Globalization;
using DigitalBrain.Confirmation;
using DigitalBrain.Operations;

namespace DigitalBrain.Capabilities.Storage;

/// <summary>
/// Capability handler for executing storage <see cref="WritePlan"/> operations.
/// </summary>
public sealed class StorageWriteHandler(IStorageOwnerSource ownerSource, StorageWriteResult? writeResult = null) : IOperationHandler<WritePlan>
{
    private readonly IStorageOwnerSource _ownerSource = ownerSource ?? throw new ArgumentNullException(nameof(ownerSource));
    private readonly StorageWriteResult? _writeResult = writeResult;

    /// <inheritdoc />
    public async Task<OperationApproval?> Describe(WritePlan plan, OperationScope scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(scope);
        if (plan.DryRun)
        {
            return null;
        }

        var owner = _ownerSource.For(scope.Brain, scope.Origin);
        var resultHandler = _writeResult ?? (scope.Services.GetService(typeof(StorageWriteResult)) as StorageWriteResult) ?? DefaultResult;
        var runner = new StorageWriteRunner(owner, resultHandler);
        var question = await runner.Confirmation(plan, ct).ConfigureAwait(false);

        var policies = scope.Services.GetService(typeof(ConfirmationPolicies)) as ConfirmationPolicies;
        var policy = policies?.For(scope.Brain.Value) ?? ConfirmationPolicy.DestructiveOnly;

        if (!ConfirmationRules.Asks(policy, question))
        {
            return null;
        }

        string? guard = null;
        if (plan.Source is { } item)
        {
            guard = await Stamp(owner, item, ct).ConfigureAwait(false);
        }

        return new OperationApproval(question, guard);
    }

    /// <inheritdoc />
    public async Task<OperationResult> Run(WritePlan plan, IOperationNeuron operation, OperationScope scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(scope);

        var owner = _ownerSource.For(scope.Brain, scope.Origin);

        if (scope.Guard is { } approved && plan.Source is { } item)
        {
            var current = await Stamp(owner, item, ct).ConfigureAwait(false);
            if (!string.Equals(approved, current, StringComparison.Ordinal))
            {
                return new OperationResult.Failed("plan-invalid", "The item changed.");
            }
        }

        using var authorizer = (scope.Services.GetService(typeof(IStorageWriteAuthorizer)) as IStorageWriteAuthorizer)?.BeginApproved(scope.Brain, scope.Origin);
        var resultHandler = _writeResult ?? (scope.Services.GetService(typeof(StorageWriteResult)) as StorageWriteResult) ?? DefaultResult;
        var runner = new StorageWriteRunner(owner, resultHandler);
        var adapter = new NeuronOperationAdapter(operation, ct);
        return await runner.Run(plan, adapter).ConfigureAwait(false);
    }

    private static async Task<string?> Stamp(StorageOwner owner, ItemId item, CancellationToken ct)
    {
        try
        {
            var connection = await owner(item, ct).ConfigureAwait(false);
            var stat = await connection.Capability.Stat(item, ct).ConfigureAwait(false);
            return stat.Hash ?? stat.Modified?.UtcTicks.ToString(CultureInfo.InvariantCulture);
        }
        catch (StorageException)
        {
            return null;
        }
    }

    private static Task<OperationResult> DefaultResult(IStorage storage, OperationId id, CancellationToken ct) =>
        throw new Unsupported(null, new NotSupportedException($"Storage connection {storage.GetType().Name} has no write result handler."));

    private sealed class NeuronOperationAdapter(IOperationNeuron neuron, CancellationToken ct) : IOperation
    {
        public OperationId Id => default;

        public CancellationToken Cancellation => ct;

        public async ValueTask Report(double fraction) => await neuron.Progress(fraction).ConfigureAwait(false);

        public async ValueTask Complete(OperationResult result) => await neuron.Complete(result).ConfigureAwait(false);
    }
}
