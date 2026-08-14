using System.Security.Cryptography;
using System.Text;
using Brain.Abstractions.Context;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using Brain.Abstractions.Policy;
using Brain.Core.Endpoints;
using Brain.Core.Modules;

namespace Brain.Core.Activities;

internal interface IEntryOperationDispatcher
{
    Task DispatchAsync<TInput>(
        EndpointAddress endpoint,
        OperationInvocation<TInput> invocation,
        ActivityContext context,
        CancellationToken cancellationToken)
        where TInput : class;
}

[GenerateSerializer]
[Alias("brain.core.idempotency-conflict")]
public sealed class IdempotencyConflictException(string message) : InvalidOperationException(message);

public sealed class OperationTypeMismatchException(string message) : ArgumentException(message);

internal interface IIdempotencyInputCanonicalizer<in TInput>
    where TInput : class
{
    string Canonicalize(TInput input);
}

internal sealed record OperationTypeBinding(
    OperationId Operation,
    Type InputType,
    Type ResultType,
    Func<object, string> CanonicalizeInput)
{
    internal static OperationTypeBinding For<TInput, TResult>(
        OperationDescriptor operation,
        IIdempotencyInputCanonicalizer<TInput> canonicalizer)
        where TInput : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(canonicalizer);
        return new OperationTypeBinding(
            operation.Id,
            typeof(TInput),
            typeof(TResult),
            input => canonicalizer.Canonicalize((TInput)input));
    }
}

internal interface IOperationTypeBindings
{
    OperationTypeBinding Validate<TInput, TResult>(OperationDescriptor operation)
        where TInput : class
        where TResult : class;
}

// This is an explicit composition seam: modules register the typed contract implementations
// they expose. CLR types stay out of OperationDescriptor and manifest contracts.
internal sealed class OperationTypeBindings : IOperationTypeBindings
{
    private readonly IReadOnlyDictionary<OperationId, OperationTypeBinding> _bindings;

    internal OperationTypeBindings(IEnumerable<OperationTypeBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var materialized = bindings.ToArray();
        _bindings = materialized.ToDictionary(static binding => binding.Operation);
        if (_bindings.Count != materialized.Length)
        {
            throw new ArgumentException("Operation type bindings must be unique per operation.", nameof(bindings));
        }
    }

    public OperationTypeBinding Validate<TInput, TResult>(OperationDescriptor operation)
        where TInput : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!_bindings.TryGetValue(operation.Id, out var binding)
            || binding.InputType != typeof(TInput)
            || binding.ResultType != typeof(TResult))
        {
            throw new OperationTypeMismatchException(
                $"Operation '{operation.Id}' is not registered for input '{typeof(TInput).FullName}' "
                + $"and result '{typeof(TResult).FullName}'.");
        }

        return binding;
    }
}

internal sealed class OperationGateway : IOperationGateway
{
    private readonly IModuleRegistry _registry;
    private readonly IWorkspacePolicyEvaluator _policy;
    private readonly IEndpointResolver _endpoints;
    private readonly IEntryOperationDispatcher _dispatcher;
    private readonly IActivityStore _store;
    private readonly ActivityProjectionService _projections;
    private readonly IOperationTypeBindings _typeBindings;

    internal OperationGateway(
        IModuleRegistry registry,
        IWorkspacePolicyEvaluator policy,
        IEndpointResolver endpoints,
        IEntryOperationDispatcher dispatcher,
        IActivityStore store,
        ActivityProjectionService projections,
        IOperationTypeBindings typeBindings)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _projections = projections ?? throw new ArgumentNullException(nameof(projections));
        _typeBindings = typeBindings ?? throw new ArgumentNullException(nameof(typeBindings));
    }

    public async Task<OperationAccepted> InvokeAsync<TInput, TResult>(
        OperationDescriptor operation,
        TInput input,
        WorkspaceContext caller,
        IdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
        where TInput : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(caller);
        return await InvokeCoreAsync<TInput, TResult>(
            operation,
            input,
            caller,
            idempotencyKey,
            parentActivity: null,
            Delegation.Empty,
            cancellationToken);
    }

    public Task<Brain.Abstractions.Activities.ActivityView> ObserveAsync(
        BrainActivityId activity,
        WorkspaceContext caller,
        CancellationToken cancellationToken)
        => _projections.ObserveAsync(activity, caller, cancellationToken);

    internal Task<OperationAccepted> StartChildAsync<TInput, TResult>(
        BrainActivityId parentActivity,
        OperationDescriptor operation,
        TInput input,
        Delegation childPolicy,
        CancellationToken cancellationToken)
        where TInput : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(childPolicy);
        var parent = _store.Get(parentActivity);
        var delegation = parent.Delegation.Intersect(childPolicy);
        if (!delegation.Operations.Contains(operation.Id))
        {
            throw new UnauthorizedAccessException(
                $"Parent activity '{parentActivity}' cannot delegate operation '{operation.Id}'.");
        }

        var key = new IdempotencyKey($"child/{parentActivity}/{operation.Id}");
        return InvokeCoreAsync<TInput, TResult>(
            operation,
            input,
            parent.Caller,
            key,
            parentActivity,
            delegation,
            cancellationToken);
    }

    private async Task<OperationAccepted> InvokeCoreAsync<TInput, TResult>(
        OperationDescriptor operation,
        TInput input,
        WorkspaceContext caller,
        IdempotencyKey idempotencyKey,
        BrainActivityId? parentActivity,
        Delegation delegation,
        CancellationToken cancellationToken)
        where TInput : class
        where TResult : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registered = _registry.GetOperation(operation.Id);
        if (!Equivalent(registered, operation))
        {
            throw new ArgumentException(
                $"Operation descriptor '{operation.Id}' does not match the registered operation contract.",
                nameof(operation));
        }

        var binding = _typeBindings.Validate<TInput, TResult>(registered);

        var fingerprint = IdempotencyFingerprint.Create(binding.CanonicalizeInput(input));
        var identity = new ActivityIdempotencyIdentity(caller.Workspace, caller.Principal, idempotencyKey);
        var state = _store.GetOrAdd(
            identity,
            () => new BrainActivityState(
                BrainActivityId.New(),
                registered.Id,
                caller,
                idempotencyKey,
                new CorrelationId($"operation/{idempotencyKey.Value}"),
                parentActivity,
                registered.TerminalResultContract,
                delegation,
                fingerprint),
            out var created);

        if (!created)
        {
            if (state.Operation != registered.Id || !string.Equals(state.InputFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new IdempotencyConflictException(
                    "An idempotency key cannot be reused for a different operation or input.");
            }

            return new OperationAccepted(state.Activity);
        }

        var activity = new BrainActivityGrain(_store, state.Activity);
        var decision = _policy.AuthorizeOperation(caller, registered);
        if (decision == PolicyDecision.Refused)
        {
            activity.Refuse(new Brain.Abstractions.Activities.ActivityProblem(
                "policy-refused",
                "Workspace policy refused this operation."));
            return new OperationAccepted(state.Activity);
        }

        if (decision == PolicyDecision.ConfirmationRequired)
        {
            activity.AwaitConfirmation();
            return new OperationAccepted(state.Activity);
        }

        activity.MarkRunning();
        var entryRole = _registry.Get(registered.Owner).Roles.Single(role =>
            role.Id == registered.EntryRole && role.Owner == registered.Owner);
        var endpoint = _endpoints.Resolve(entryRole, caller);
        var context = new ActivityContext(
            caller.Workspace,
            caller.Principal,
            state.Activity,
            state.Correlation,
            state.Delegation);
        await _dispatcher.DispatchAsync(
            endpoint,
            new OperationInvocation<TInput>(registered, input, caller, idempotencyKey),
            context,
            cancellationToken);
        return new OperationAccepted(state.Activity);
    }

    private static bool Equivalent(OperationDescriptor left, OperationDescriptor right)
        => left.Id == right.Id
            && left.InputContract == right.InputContract
            && left.TerminalResultContract == right.TerminalResultContract
            && left.EntryRole == right.EntryRole
            && left.Owner == right.Owner
            && left.Version == right.Version;
}

internal static class IdempotencyFingerprint
{
    internal static string Create(string canonicalMaterial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalMaterial);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalMaterial)));
    }
}
