using System.Collections;
using System.Globalization;
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

public sealed class OperationTypeMismatchException(string message) : ArgumentException(message);

internal sealed record OperationTypeBinding(
    OperationId Operation,
    Type InputType,
    Type ResultType)
{
    internal static OperationTypeBinding For<TInput, TResult>(OperationDescriptor operation)
        where TInput : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(operation);
        return new OperationTypeBinding(operation.Id, typeof(TInput), typeof(TResult));
    }
}

internal interface IOperationTypeBindings
{
    void Validate<TInput, TResult>(OperationDescriptor operation)
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

    public void Validate<TInput, TResult>(OperationDescriptor operation)
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

        _typeBindings.Validate<TInput, TResult>(registered);

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

internal static class InputFingerprint
{
    internal static string Create<TInput>(TInput input)
        where TInput : class
    {
        var builder = new StringBuilder();
        Append(builder, input, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, object? value, HashSet<object> path)
    {
        if (value is null)
        {
            Token(builder, "null", string.Empty);
            return;
        }

        var type = value.GetType();
        Token(builder, "type", type.AssemblyQualifiedName ?? type.FullName ?? type.Name);
        if (value is string text)
        {
            Token(builder, "string", text);
            return;
        }

        if (value is Guid guid)
        {
            Token(builder, "guid", guid.ToString("D"));
            return;
        }

        if (value is DateTime dateTime)
        {
            Token(builder, "datetime", dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            return;
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            Token(builder, "datetime-offset", dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            return;
        }

        if (type.IsEnum)
        {
            Token(builder, "enum", value.ToString() ?? string.Empty);
            return;
        }

        if (type.IsPrimitive || value is decimal)
        {
            Token(builder, "scalar", ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
            return;
        }

        var added = false;
        if (!type.IsValueType)
        {
            if (!path.Add(value))
            {
                throw new ArgumentException("Operation input must not contain reference cycles.", nameof(value));
            }

            added = true;
        }

        try
        {
            if (TryGetDictionaryEntries(value, out var entries))
            {
                Token(builder, "dictionary", entries.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var entry in entries
                             .Select(entry => new
                             {
                                 Key = Canonicalize(entry.Key, path),
                                 Value = Canonicalize(entry.Value, path),
                             })
                             .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                             .ThenBy(static entry => entry.Value, StringComparer.Ordinal))
                {
                    Token(builder, "key", entry.Key);
                    Token(builder, "value", entry.Value);
                }

                return;
            }

            if (value is IEnumerable enumerable)
            {
                var items = enumerable.Cast<object?>()
                    .Select(item => Canonicalize(item, path))
                    .ToList();

                Token(builder, "sequence", items.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var item in items)
                {
                    Token(builder, "item", item);
                }

                return;
            }

            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
                .OrderBy(static property => property.Name, StringComparer.Ordinal)
                .ToArray();
            if (properties.Length == 0)
            {
                throw new ArgumentException(
                    $"Operation input value type '{type.FullName}' has no canonical public properties.",
                    nameof(value));
            }

            Token(builder, "record", properties.Length.ToString(CultureInfo.InvariantCulture));
            foreach (var property in properties)
            {
                Token(builder, "property", property.Name);
                Append(builder, property.GetValue(value), path);
            }
        }
        finally
        {
            if (added)
            {
                path.Remove(value);
            }
        }
    }

    private static string Canonicalize(object? value, HashSet<object> path)
    {
        var builder = new StringBuilder();
        Append(builder, value, new HashSet<object>(path, ReferenceEqualityComparer.Instance));
        return builder.ToString();
    }

    private static bool TryGetDictionaryEntries(object value, out List<(object? Key, object? Value)> entries)
    {
        var genericDictionary = value.GetType().GetInterfaces().Any(static candidate =>
            candidate.IsGenericType
            && (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                || candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
        if (genericDictionary && value is IEnumerable enumerable)
        {
            entries = enumerable.Cast<object>()
                .Select(static entry =>
                {
                    var type = entry.GetType();
                    return (
                        type.GetProperty("Key")!.GetValue(entry),
                        type.GetProperty("Value")!.GetValue(entry));
                })
                .ToList();
            return true;
        }

        if (value is IDictionary dictionary)
        {
            entries = dictionary.Cast<DictionaryEntry>()
                .Select(static entry => ((object?)entry.Key, entry.Value))
                .ToList();
            return true;
        }

        entries = [];
        return false;
    }

    private static void Token(StringBuilder builder, string kind, string? value)
    {
        var material = value ?? string.Empty;
        builder.Append(kind)
            .Append(':')
            .Append(material.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(material)
            .Append(';');
    }
}
