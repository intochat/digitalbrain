using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Behaviors;

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

        var loadContext = new AssemblyLoadContext(
            $"behavior-host-{request.Metadata.Execution.Value:N}",
            isCollectible: true);
        loadContext.Resolving += static (_, name) =>
        {
            try
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyName(name);
            }
            catch (Exception)
            {
                return null;
            }
        };

        try
        {
            using var stream = new MemoryStream(request.ArtifactBytes.ToArray());
            var assembly = loadContext.LoadFromStream(stream);
            var programType = assembly.GetExportedTypes()
                .FirstOrDefault(type =>
                    !type.IsAbstract
                    && type.GetInterfaces().Any(contract =>
                        contract.IsGenericType
                        && contract.GetGenericTypeDefinition() == typeof(IBehaviorProgram<>))
                    && type.GetConstructor(Type.EmptyTypes) is not null);

            if (programType is null)
            {
                return new BehaviorExecutionOutcome(
                    false,
                    "Compiled artifact has no public IBehaviorProgram<> implementation.");
            }

            var programInterface = programType.GetInterfaces()
                .First(contract =>
                    contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IBehaviorProgram<>));
            var triggerType = programInterface.GetGenericArguments()[0];
            if (!string.Equals(triggerType.FullName, request.TriggerTypeName, StringComparison.Ordinal)
                && !string.Equals(triggerType.Name, request.TriggerTypeName, StringComparison.Ordinal))
            {
                return new BehaviorExecutionOutcome(
                    false,
                    $"Trigger type '{request.TriggerTypeName}' does not match program trigger '{triggerType.FullName}'.");
            }

            var trigger = JsonSerializer.Deserialize(request.TriggerJson, triggerType)
                ?? throw new InvalidOperationException(
                    $"Trigger JSON could not deserialize to '{triggerType.FullName}'.");

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
                ?? throw new InvalidOperationException("IBehaviorProgram.ExecuteAsync was not found.");
            var task = (ValueTask)execute.Invoke(program, [trigger, context, attempt.Token])!;
            await task.ConfigureAwait(false);

            var outcome = context.LastOutcome ?? "executed";
            return new BehaviorExecutionOutcome(true, outcome);
        }
        catch (Exception exception)
        {
            return new BehaviorExecutionOutcome(false, exception.GetBaseException().Message);
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
            var entry = ResolveSingleFileEntry(assembly);
            var brainType = entry.GetParameters()[0].ParameterType;
            var triggerType = brainType.GetGenericArguments()[0];
            if (!string.Equals(triggerType.FullName, request.TriggerTypeName, StringComparison.Ordinal)
                && !string.Equals(triggerType.Name, request.TriggerTypeName, StringComparison.Ordinal))
            {
                return new BehaviorExecutionOutcome(
                    false,
                    $"Trigger type '{request.TriggerTypeName}' does not match program trigger '{triggerType.FullName}'.");
            }

            var trigger = JsonSerializer.Deserialize(triggerJson.Span, triggerType)
                ?? throw new InvalidOperationException(
                    $"Trigger JSON could not deserialize to '{triggerType.FullName}'.");

            using var attempt = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();

            var triggerWrapperType = typeof(BehaviorTrigger<>).MakeGenericType(triggerType);
            var triggerWrapper = Activator.CreateInstance(triggerWrapperType, trigger, attempt.Token)
                ?? throw new InvalidOperationException(
                    $"BehaviorTrigger<{triggerType.Name}> could not be constructed.");

            var create = brainType.GetMethod(
                    nameof(BehaviorBrain<Synapse>.Create),
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                    binder: null,
                    types: [triggerWrapperType, typeof(IBehaviorSynapseBroker)],
                    modifiers: null)
                ?? throw new InvalidOperationException(
                    $"BehaviorBrain<{triggerType.Name}>.Create was not found.");
            brain = create.Invoke(null, [triggerWrapper, broker])
                ?? throw new InvalidOperationException(
                    $"BehaviorBrain<{triggerType.Name}>.Create returned null.");

            var result = entry.Invoke(null, [brain]);
            await AwaitEntryResultAsync(result, entry.ReturnType).ConfigureAwait(false);

            if (brain is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                brain = null;
            }

            return new BehaviorExecutionOutcome(true, "executed");
        }
        catch (Exception exception)
        {
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

            return new BehaviorExecutionOutcome(false, exception.GetBaseException().Message);
        }
        finally
        {
            loadContext.Unload();
        }
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

    private static MethodInfo ResolveSingleFileEntry(Assembly assembly)
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
            throw new InvalidOperationException(
                "Compiled artifact has no public static RunAsync(BehaviorBrain<TTrigger>) entry.");
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
