using System.Reflection;
using DigitalBrain.Tasks;
using Xunit;

namespace DigitalBrain.Tests.Contracts;

public sealed class TasksVocabulary
{
    private static readonly string TasksNamespace =
        typeof(ITask).Namespace
        ?? throw new InvalidOperationException($"{nameof(ITask)} has no namespace.");

    [Fact(DisplayName = "Tasks.Contracts public vocabulary is task/worker/attempt/activation/operation surface — never AI or schedule types")]
    public void PublicVocabularyIsTaskWorkerAndAttemptSurfaceOnly()
    {
        var contracts = typeof(ITask).Assembly;

        Assert.Null(contracts.GetType($"{TasksNamespace}.IReminder"));
        Assert.Null(contracts.GetType($"{TasksNamespace}.ICountdown"));
        Assert.Null(contracts.GetType($"{TasksNamespace}.ILLM"));
        Assert.Null(contracts.GetType($"{TasksNamespace}.IBehavior"));
        Assert.DoesNotContain(
            contracts.GetExportedTypes(),
            type => type.Name is "RelayWorkerAccept"
                or "RelayWorkerContinue"
                or "RelayWorkerCancel"
                or "DispatchWorkerAccept"
                or "DispatchWorkerContinue"
                or "DispatchWorkerCancel"
                or "WorkerDispatchRelay"
                || type.Name.Contains("DispatchWorker", StringComparison.Ordinal)
                || type.Name.Contains("RelayWorker", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "ITask methods are unsuffixed, aliased, and return TaskSnapshot")]
    public void TaskMethodsAreUnsuffixedAndAliased()
    {
        var methods = typeof(ITask)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.All(methods, method =>
        {
            Assert.DoesNotContain("Async", method.Name, StringComparison.Ordinal);
            Assert.Equal(
                method.Name,
                method.GetCustomAttribute<AliasAttribute>()?.Alias);
            Assert.Equal(typeof(Task<TaskSnapshot>), method.ReturnType);
        });
    }

    [Fact(DisplayName = "IWorker methods are unsuffixed and aliased — Accept/Continue/Cancel only")]
    public void WorkerMethodsAreUnsuffixedAndAliased()
    {
        var methods = typeof(IWorker)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.All(methods, method =>
        {
            Assert.DoesNotContain("Async", method.Name, StringComparison.Ordinal);
            Assert.Equal(
                method.Name,
                method.GetCustomAttribute<AliasAttribute>()?.Alias);
            Assert.Equal(typeof(Task), method.ReturnType);
        });
    }

    [Fact(DisplayName = "Tasks runtime public surface is TasksModule only — no product IWorker, no TaskNeuron")]
    public void RuntimePublicSurfaceIsModuleMarkerOnly()
    {
        Assert.False(typeof(IWorker).IsAssignableFrom(typeof(TasksModule)));
        Assert.DoesNotContain(
            typeof(TasksModule).Assembly.GetExportedTypes(),
            type => typeof(IWorker).IsAssignableFrom(type) && type.IsClass);
    }
}
