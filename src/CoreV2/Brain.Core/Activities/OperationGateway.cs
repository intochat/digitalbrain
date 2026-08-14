using System.Reflection;
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

public sealed class IdempotencyConflictException(string message) : InvalidOperationException(message);

internal sealed class OperationGateway : IOperationGateway
{
    private readonly IModuleRegistry _registry;
    private readonly IWorkspacePolicyEvaluator _policy;
    private readonly IEndpointResolver _endpoints;
    private readonly IEntryOperationDispatcher _dispatcher;
    private readonly IActivityStore _store;
    private readonly ActivityProjectionService _projections;

    internal OperationGateway(
        IModuleRegistry registry,
        IWorkspacePolicyEvaluator policy,
        IEndpointResolver endpoints,
        IEntryOperationDispatcher dispatcher,
        IActivityStore store,
        ActivityProjectionService projections)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _projections = projections ?? throw new ArgumentNullException(nameof(projections));
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

        var fingerprint = InputFingerprint.Create(input);
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

internal static class InputFingerprint
{
    internal static string Create<TInput>(TInput input)
        where TInput : class
    {
        var builder = new StringBuilder(typeof(TInput).AssemblyQualifiedName);
        Append(builder, input, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, object? value, HashSet<object> visited)
    {
        if (value is null)
        {
            builder.Append("<null>");
            return;
        }

        var type = value.GetType();
        builder.Append('[').Append(type.FullName).Append(']');
        if (value is string or Guid || type.IsPrimitive || value is decimal or DateTime or DateTimeOffset)
        {
            builder.Append(System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        if (!type.IsValueType && !visited.Add(value))
        {
            throw new ArgumentException("Operation input must not contain reference cycles.", nameof(value));
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
                     .OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            builder.Append(property.Name).Append('=');
            Append(builder, property.GetValue(value), visited);
        }
    }
}
