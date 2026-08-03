using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors.Host;

internal static class BehaviorProgramLoader
{
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Host execution maps any program failure to a typed outcome for the caller.")]
    public static async ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        LegacyBehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var loadContext = CreateCollectibleContext(request.Metadata.Execution);
        try
        {
            using var stream = new MemoryStream(request.ArtifactBytes.ToArray());
            var assembly = loadContext.LoadFromStream(stream);
            var programType = ResolveProgramType(assembly);

            if (programType is null)
            {
                return new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.ContractMismatch);
            }

            var programInterface = programType.GetInterfaces()
                .First(contract =>
                    contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IBehaviorProgram<>));
            var triggerType = programInterface.GetGenericArguments()[0];
            if (!string.Equals(triggerType.FullName, request.TriggerTypeName, StringComparison.Ordinal)
                && !string.Equals(triggerType.Name, request.TriggerTypeName, StringComparison.Ordinal))
            {
                return new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.ContractMismatch);
            }

            var trigger = JsonSerializer.Deserialize(request.TriggerJson, triggerType)
                ?? throw new InvalidOperationException(BehaviorExecutionCodes.ContractMismatch);

            var program = Activator.CreateInstance(programType)!;
            using var attempt = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();
            var context = new HostBehaviorContext(
                request.Metadata,
                request.Capabilities,
                request.Time,
                attempt.Token);
            var execute = programInterface.GetMethod(nameof(IBehaviorProgram<Synapse>.ExecuteAsync))
                ?? throw new InvalidOperationException(BehaviorExecutionCodes.ContractMismatch);
            var task = (ValueTask)execute.Invoke(program, [trigger, context, attempt.Token])!;
            await task.ConfigureAwait(false);

            return new BehaviorExecutionOutcome(true, BehaviorExecutionCodes.Succeeded);
        }
        catch (OperationCanceledException)
        {
            return new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.Cancelled);
        }
        catch (Exception exception)
        {
            _ = exception;
            return new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.Exception);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Host execution maps any program failure to a typed outcome for the caller.")]
    public static async ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorExecutionRequest request,
        ReadOnlyMemory<byte> triggerJson,
        IBehaviorSynapseBroker broker,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(broker);
        cancellationToken.ThrowIfCancellationRequested();

        var loadContext = CreateCollectibleContext(request.Metadata.Execution);
        object? brain = null;
        try
        {
            using var stream = new MemoryStream(request.ArtifactBytes.ToArray());
            var assembly = loadContext.LoadFromStream(stream);
            if (TryResolveSingleFileEntry(assembly) is not { } entry)
            {
                return await ExecuteBoundProgramAsync(
                    assembly,
                    request,
                    triggerJson,
                    broker,
                    cancellationToken).ConfigureAwait(false);
            }

            var brainType = entry.GetParameters()[0].ParameterType;
            var triggerType = brainType.GetGenericArguments()[0];
            if (!string.Equals(triggerType.FullName, request.TriggerTypeName, StringComparison.Ordinal)
                && !string.Equals(triggerType.Name, request.TriggerTypeName, StringComparison.Ordinal))
            {
                return new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.ContractMismatch);
            }

            // The rail stores trigger payloads through BehaviorPayloadJson, so the entry must be
            // handed its trigger through the same codec or every property binds to null.
            var trigger = BehaviorPayloadJson.Deserialize(triggerJson.Span, triggerType)
                ?? throw new InvalidOperationException(BehaviorExecutionCodes.ContractMismatch);

            using var attempt = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();

            var triggerWrapperType = typeof(BehaviorTrigger<>).MakeGenericType(triggerType);
            var triggerWrapper = Activator.CreateInstance(triggerWrapperType, trigger, attempt.Token)
                ?? throw new InvalidOperationException(BehaviorExecutionCodes.ContractMismatch);

            var create = brainType.GetMethod(
                    nameof(BehaviorBrain<Synapse>.Create),
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                    binder: null,
                    types: [triggerWrapperType, typeof(IBehaviorSynapseBroker)],
                    modifiers: null)
                ?? throw new InvalidOperationException(BehaviorExecutionCodes.ContractMismatch);
            brain = create.Invoke(null, [triggerWrapper, broker])
                ?? throw new InvalidOperationException(BehaviorExecutionCodes.ContractMismatch);

            var result = entry.Invoke(null, [brain]);
            await AwaitEntryResultAsync(result, entry.ReturnType).ConfigureAwait(false);

            if (brain is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                brain = null;
            }

            return new BehaviorExecutionOutcome(true, BehaviorExecutionCodes.Succeeded);
        }
        catch (OperationCanceledException)
        {
            if (brain is IAsyncDisposable cancelledDisposable)
            {
                try
                {
                    await cancelledDisposable.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            return new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.Cancelled);
        }
        catch (Exception exception)
        {
            var userAction = UnwrapUserActionRequired(exception);
            if (brain is IAsyncDisposable asyncDisposable)
            {
                try
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            if (userAction is not null)
            {
                return new BehaviorExecutionOutcome(
                    false,
                    BehaviorExecutionCodes.UserActionRequired,
                    BehaviorUserActionSurface.FromRequirement(userAction));
            }

            return new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.Exception);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static UserActionRequired? UnwrapUserActionRequired(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is BehaviorUserActionRequiredException userAction
                && userAction.Requirement is { } requirement)
            {
                return requirement;
            }
        }

        return null;
    }

    private static AssemblyLoadContext CreateCollectibleContext(BehaviorExecutionId execution)
    {
        var loadContext = new AssemblyLoadContext(
            $"behavior-host-{execution.Value:N}",
            isCollectible: true);
        loadContext.Resolving += static (_, name) =>
        {
            try
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyName(name);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (FileLoadException)
            {
                return null;
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        };

        return loadContext;
    }

    // An IBehaviorProgram artifact is the only shape whose subscription and emit grants the
    // compiler can derive, so the bound attempt must be able to drive it with the same broker
    // the single-file entry gets — that broker is what carries the attempt identity.
    private static async ValueTask<BehaviorExecutionOutcome> ExecuteBoundProgramAsync(
        Assembly assembly,
        BehaviorExecutionRequest request,
        ReadOnlyMemory<byte> triggerJson,
        IBehaviorSynapseBroker broker,
        CancellationToken cancellationToken)
    {
        if (ResolveProgramType(assembly) is not { } programType)
        {
            return new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.ContractMismatch);
        }

        var programInterface = programType.GetInterfaces()
            .First(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IBehaviorProgram<>));
        var triggerType = programInterface.GetGenericArguments()[0];
        if (!string.Equals(triggerType.FullName, request.TriggerTypeName, StringComparison.Ordinal)
            && !string.Equals(triggerType.Name, request.TriggerTypeName, StringComparison.Ordinal))
        {
            return new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.ContractMismatch);
        }

        // The rail stores trigger payloads through BehaviorPayloadJson, so the program must be
        // handed its trigger through the same codec or every property binds to null.
        var trigger = BehaviorPayloadJson.Deserialize(triggerJson.Span, triggerType)
            ?? throw new InvalidOperationException(BehaviorExecutionCodes.ContractMismatch);

        var program = Activator.CreateInstance(programType)!;
        using var attempt = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        var context = new HostBehaviorContext(
            request.Metadata,
            BrokerBoundCapabilities.Instance,
            TimeProvider.System,
            attempt.Token,
            broker);
        var execute = programInterface.GetMethod(nameof(IBehaviorProgram<Synapse>.ExecuteAsync))
            ?? throw new InvalidOperationException(BehaviorExecutionCodes.ContractMismatch);
        await ((ValueTask)execute.Invoke(program, [trigger, context, attempt.Token])!).ConfigureAwait(false);

        return new BehaviorExecutionOutcome(true, BehaviorExecutionCodes.Succeeded);
    }

    private static Type? ResolveProgramType(Assembly assembly)
        => assembly.GetExportedTypes()
            .FirstOrDefault(type =>
                !type.IsAbstract
                && type.GetInterfaces().Any(contract =>
                    contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IBehaviorProgram<>))
                && type.GetConstructor(Type.EmptyTypes) is not null);

    // Directed edges are derived only from BehaviorBrain.Get, and the compiler rejects any
    // context-rooted lookalike, so no compiled artifact can reach this resolver.
    private sealed class BrokerBoundCapabilities : IBehaviorCapabilityResolver
    {
        public static BrokerBoundCapabilities Instance { get; } = new();

        public TContract Get<TContract>(string name)
            where TContract : class, INeuron
            => throw new NotSupportedException(
                "Directed capabilities on a bound attempt are reached through BehaviorBrain.Get.");
    }

    private static MethodInfo? TryResolveSingleFileEntry(Assembly assembly)
    {
        var candidates = assembly.GetExportedTypes()
            .SelectMany(static type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(static method =>
            {
                if (!string.Equals(method.Name, "RunAsync", StringComparison.Ordinal)
                    || method.GetParameters() is not [{ } parameter])
                {
                    return false;
                }

                var parameterType = parameter.ParameterType;
                return parameterType.IsGenericType
                    && parameterType.GetGenericTypeDefinition() == typeof(BehaviorBrain<>);
            })
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        if (candidates.Length > 1)
        {
            throw new InvalidOperationException(
                "Compiled artifact has ambiguous public static RunAsync(BehaviorBrain<TTrigger>) entries.");
        }

        return candidates[0];
    }

    private static async ValueTask AwaitEntryResultAsync(object? result, Type returnType)
    {
        if (returnType == typeof(Task))
        {
            await ((Task)result!).ConfigureAwait(false);
            return;
        }

        if (returnType == typeof(ValueTask))
        {
            await ((ValueTask)result!).ConfigureAwait(false);
            return;
        }

        if (returnType.IsGenericType)
        {
            var definition = returnType.GetGenericTypeDefinition();
            if (definition == typeof(Task<>))
            {
                await ((Task)result!).ConfigureAwait(false);
                return;
            }

            if (definition == typeof(ValueTask<>))
            {
                var asTask = returnType.GetMethod(nameof(ValueTask.AsTask), Type.EmptyTypes)
                    ?? throw new InvalidOperationException("ValueTask.AsTask was not found.");
                await ((Task)asTask.Invoke(result, null)!).ConfigureAwait(false);
                return;
            }
        }

        throw new InvalidOperationException(
            $"RunAsync return type '{returnType}' is not a supported Task or ValueTask shape.");
    }
}
