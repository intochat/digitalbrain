using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using DigitalBrain.Tests.Boundary;
using Xunit;

namespace DigitalBrain.Tests.Packages;

public sealed class TasksContracts
{
    private static readonly string TasksNamespace =
        typeof(ITask).Namespace
        ?? throw new InvalidOperationException($"{nameof(ITask)} has no namespace.");

    [Fact(DisplayName = "Tasks.Contracts public vocabulary is task/worker/attempt only — never AI or schedule types")]
    public void PublicVocabularyIsTaskWorkerAndAttemptSurfaceOnly()
    {
        var contracts = typeof(ITask).Assembly;

        var vocabulary = contracts
            .GetExportedTypes()
            .Where(type => type.Namespace == TasksNamespace)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(ApprovalRequired),
                nameof(AttemptAccepted),
                nameof(AttemptCancelled),
                nameof(AttemptCursor),
                nameof(AttemptFact),
                nameof(AttemptFailed),
                nameof(AttemptId),
                nameof(AttemptOutcomeUncertain),
                nameof(AttemptProgressed),
                nameof(AttemptRequest),
                nameof(AttemptSucceeded),
                nameof(AttemptWaiting),
                nameof(BlockerId),
                nameof(CancelTask),
                nameof(DependencyPending),
                nameof(FactReference),
                nameof(Failure),
                nameof(Goal),
                nameof(ITask),
                nameof(IWorker),
                nameof(InputRequired),
                nameof(OutcomeUncertain),
                nameof(Result),
                nameof(RetryScheduled),
                nameof(StartTask),
                nameof(TaskBlocker),
                nameof(TaskPolicy),
                nameof(TaskSnapshot),
                nameof(TaskState),
            ],
            vocabulary);

        Assert.Null(contracts.GetType($"{TasksNamespace}.IReminder"));
        Assert.Null(contracts.GetType($"{TasksNamespace}.ICountdown"));
        Assert.Null(contracts.GetType($"{TasksNamespace}.ILLM"));
        Assert.Null(contracts.GetType($"{TasksNamespace}.IBehavior"));
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

        Assert.Equal(
            [
                nameof(ITask.Cancel),
                nameof(ITask.Read),
                nameof(ITask.Start),
            ],
            methods.Select(method => method.Name).Order(StringComparer.Ordinal));
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

        Assert.Equal(
            [
                nameof(IWorker.Accept),
                nameof(IWorker.Cancel),
                nameof(IWorker.Continue),
            ],
            methods.Select(method => method.Name).Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName = "Tasks runtime public surface is TasksModule only — no product IWorker, no TaskNeuron")]
    public void RuntimePublicSurfaceIsModuleMarkerOnly()
    {
        var exported = typeof(TasksModule).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(TasksModule)], exported);
        Assert.False(typeof(IWorker).IsAssignableFrom(typeof(TasksModule)));
        Assert.DoesNotContain(
            typeof(TasksModule).Assembly.GetExportedTypes(),
            type => typeof(IWorker).IsAssignableFrom(type) && type.IsClass);
    }

    [Fact(DisplayName = "Tasks has no module Aspire.Hosting package — pure grain module, no provider resources")]
    public void TasksHasNoModuleAspireHostingPackage()
    {
        var hostingPackage = Path.Combine(
            RepositoryLayout.Root,
            RepositoryLayout.Modules,
            $"{PackageInventory.ModulesTasks}.Aspire.Hosting");

        Assert.False(Directory.Exists(hostingPackage));
        Assert.DoesNotContain(
            PackageBoundarySupport.HostingPackages,
            package => package.StartsWith(PackageInventory.ModulesTasks, StringComparison.Ordinal));
    }
}
