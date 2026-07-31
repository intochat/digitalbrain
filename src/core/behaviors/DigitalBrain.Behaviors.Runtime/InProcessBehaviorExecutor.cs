using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Behaviors;

internal sealed class InProcessBehaviorExecutor : IBehaviorExecutor
{
    public ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            new BehaviorExecutionOutcome(
                false,
                "Hardened execution requires an isolated host/broker; in-process raw execution is closed."));
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Execution seam maps any program failure to a typed outcome for journaling.")]
    public async ValueTask<BehaviorExecutionOutcome> ExecuteLegacyAsync(
        LegacyBehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var loadContext = new AssemblyLoadContext($"behavior-exec-{request.Metadata.Execution.Value:N}", isCollectible: true);
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
                return new BehaviorExecutionOutcome(false, "Compiled artifact has no public IBehaviorProgram<> implementation.");
            }

            var programInterface = programType.GetInterfaces()
                .First(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IBehaviorProgram<>));
            var triggerType = programInterface.GetGenericArguments()[0];
            if (!string.Equals(triggerType.FullName, request.TriggerTypeName, StringComparison.Ordinal)
                && !string.Equals(triggerType.Name, request.TriggerTypeName, StringComparison.Ordinal))
            {
                return new BehaviorExecutionOutcome(
                    false,
                    $"Trigger type '{request.TriggerTypeName}' does not match program trigger '{triggerType.FullName}'.");
            }

            var trigger = JsonSerializer.Deserialize(request.TriggerJson, triggerType)
                ?? throw new InvalidOperationException($"Trigger JSON could not deserialize to '{triggerType.FullName}'.");

            var program = Activator.CreateInstance(programType)!;
            using var attempt = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();
            var context = new ExecutorBehaviorContext(
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
}
