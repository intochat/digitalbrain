using Brain.Abstractions.Activities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Operations;
using Brain.Modules.Proof.Contracts;
using Brain.Testing.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace Brain.Testing;

public sealed class BrainTestHost : IAsyncDisposable, IActivityPayloadReader
{
    private readonly WorkspaceFixture _callers = new();
    private readonly InProcessTestCluster _cluster;
    private readonly IProofRuntimeGrain _runtime;

    private BrainTestHost(InProcessTestCluster cluster, IProofRuntimeGrain runtime, DeterministicTimeProvider time)
    {
        _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Time = time ?? throw new ArgumentNullException(nameof(time));
        Operations = new ClusterOperations(_runtime);
    }

    public IOperationGateway Operations { get; }

    public DeterministicTimeProvider Time { get; }

    public static async Task<BrainTestHost> StartAsync()
    {
        var time = new DeterministicTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureSilo((_, silo) =>
        {
            silo.ConfigureServices(services =>
            {
                services.AddSingleton(time);
                services.AddSingleton<ProofRuntime>();
            });
        });
        var cluster = builder.Build();
        await cluster.DeployAsync();
        var runtime = cluster.Client.GetGrain<IProofRuntimeGrain>(Guid.NewGuid().ToString("N"));
        return new BrainTestHost(cluster, runtime, time);
    }

    public WorkspaceContext Caller(string workspace, string principal) => _callers.Caller(workspace, principal);

    public async Task<T> ReadResultAsync<T>(ActivityView view, WorkspaceContext caller)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(caller);
        if (view.Result is null)
        {
            throw new InvalidOperationException("The activity has no terminal result.");
        }

        return (await ReadResultAsync<T>(view.Result, caller, CancellationToken.None)).Value;
    }

    public Task<ActivityProgress<T>> ReadProgressAsync<T>(ActivityProgressReference reference, WorkspaceContext caller, CancellationToken cancellationToken)
        where T : class => throw new NotSupportedException("The proof has no progress payload.");

    public async Task<ActivityResult<T>> ReadResultAsync<T>(ActivityResultReference reference, WorkspaceContext caller, CancellationToken cancellationToken)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        object result = typeof(T) == typeof(ProofResult)
            ? new ProofResult(await _runtime.ReadProofResultAsync(reference.Payload.Value, caller.Workspace.Value, caller.Principal.Value))
            : typeof(T) == typeof(CorrectionResult)
                ? new CorrectionResult(await _runtime.ReadCorrectionResultAsync(reference.Payload.Value, caller.Workspace.Value, caller.Principal.Value))
                : throw new InvalidOperationException("The proof runtime does not expose the requested result contract.");
        return new ActivityResult<T>((T)result);
    }

    public Task<Guid> RuntimeInstanceIdAsync() => _runtime.InstanceIdAsync();

    public Task<int> RuntimeDispatchCountAsync() => _runtime.DispatchCountAsync();

    public Task<string[]> RewireEvidenceAsync() => _runtime.RewireEvidenceAsync();

    public Task<int> ActivityCountAsync() => _runtime.ActivityCountAsync();

    public Task<int> CapabilityCallCountAsync() => _runtime.CapabilityCallCountAsync();

    public Task HoldNextDeliveryAsync() => _runtime.HoldNextDeliveryAsync();

    public Task FlushHeldDeliveriesAsync() => _runtime.FlushHeldDeliveriesAsync();

    public Task ApplyPrincipalWiringAsync(string workspace, string principal) => _runtime.ApplyPrincipalWiringAsync(workspace, principal);

    public Task<string[]> PrincipalRuntimeEvidenceAsync(string workspace, string principal) => _runtime.PrincipalRuntimeEvidenceAsync(workspace, principal);

    public ValueTask DisposeAsync() => _cluster.DisposeAsync();

    private sealed class ClusterOperations(IProofRuntimeGrain runtime) : IOperationGateway
    {
        public Task<OperationAccepted> InvokeAsync<TInput, TResult>(OperationDescriptor operation, TInput input, WorkspaceContext caller, IdempotencyKey idempotencyKey, CancellationToken cancellationToken)
            where TInput : class
            where TResult : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (operation, input) switch
            {
                (var descriptor, ProofInput proof) when descriptor == ProofContracts.Run && typeof(TResult) == typeof(ProofResult)
                    => InvokeRunAsync(proof, caller, idempotencyKey),
                (var descriptor, CorrectionInput correction) when descriptor == ProofContracts.Correct && typeof(TResult) == typeof(CorrectionResult)
                    => InvokeCorrectionAsync(correction, caller, idempotencyKey),
                (_, ProofInput) when typeof(TResult) == typeof(ProofResult)
                    => InvokeUnregisteredAsync(caller, idempotencyKey),
                _ => throw new InvalidOperationException("The cluster proof runtime does not expose the requested operation binding."),
            };
        }

        public Task<ActivityView> ObserveAsync(BrainActivityId activity, WorkspaceContext caller, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ObserveCoreAsync(activity, caller);
        }

        private async Task<OperationAccepted> InvokeRunAsync(ProofInput input, WorkspaceContext caller, IdempotencyKey key)
            => new(new BrainActivityId(Guid.Parse(await runtime.InvokeRunAsync(input.Value, caller.Workspace.Value, caller.Principal.Value, key.Value))));

        private async Task<OperationAccepted> InvokeCorrectionAsync(CorrectionInput input, WorkspaceContext caller, IdempotencyKey key)
            => new(new BrainActivityId(Guid.Parse(await runtime.InvokeCorrectionAsync(input.RequestedRoute, caller.Workspace.Value, caller.Principal.Value, key.Value))));

        private async Task<OperationAccepted> InvokeUnregisteredAsync(WorkspaceContext caller, IdempotencyKey key)
            => new(new BrainActivityId(Guid.Parse(await runtime.InvokeUnregisteredAsync(caller.Workspace.Value, caller.Principal.Value, key.Value))));

        private async Task<ActivityView> ObserveCoreAsync(BrainActivityId activity, WorkspaceContext caller)
        {
            var view = await runtime.ObserveAsync(activity.Value.ToString("N"), caller.Workspace.Value, caller.Principal.Value);
            var result = string.IsNullOrEmpty(view[3])
                ? null
                : new ActivityResultReference(new Brain.Abstractions.Contracts.ContractId(view[2]), new ActivityPayloadReference(view[3]));
            return new ActivityView(activity, new OperationId(view[0]), Enum.Parse<ActivityStatus>(view[1]), new Brain.Abstractions.Contracts.ContractId(view[2]), null, result, null);
        }
    }
}
