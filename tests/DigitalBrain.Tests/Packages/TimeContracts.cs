using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.Boundary;
using DigitalBrain.Time;
using Xunit;

namespace DigitalBrain.Tests.Packages;

public sealed class TimeContracts
{
    private static readonly string TimeNamespace =
        typeof(ICountdown).Namespace
        ?? throw new InvalidOperationException($"{nameof(ICountdown)} has no namespace.");

    private static readonly string TimeRuntime =
        typeof(TimeModule).Assembly.GetName().Name
        ?? throw new InvalidOperationException($"{nameof(TimeModule)} assembly has no name.");

    private static readonly string TimeContractsPackage =
        typeof(ICountdown).Assembly.GetName().Name
        ?? throw new InvalidOperationException($"{nameof(ICountdown)} assembly has no name.");

    private static readonly string Kernel =
        typeof(Neuron).Assembly.GetName().Name
        ?? throw new InvalidOperationException($"{nameof(Neuron)} assembly has no name.");

    private static readonly string Abstractions =
        typeof(NeuronId).Assembly.GetName().Name
        ?? throw new InvalidOperationException($"{nameof(NeuronId)} assembly has no name.");

    [Fact(DisplayName = "Time.Contracts public vocabulary is Countdown only — IReminder remains absent")]
    public void CountdownIsTheOnlyTimeNeuronCapability()
    {
        var contracts = typeof(ICountdown).Assembly;

        var vocabulary = contracts
            .GetExportedTypes()
            .Where(type => type.Namespace == TimeNamespace)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(CancelCountdown),
                nameof(CountdownElapsed),
                nameof(CountdownResolution),
                nameof(CountdownSnapshot),
                nameof(CountdownStatus),
                nameof(ICountdown),
                nameof(RescheduleCountdown),
                nameof(RestartCountdown),
                nameof(StartCountdown),
            ],
            vocabulary);

        Assert.Null(contracts.GetType($"{TimeNamespace}.IReminder"));
        Assert.Null(typeof(TimeModule).Assembly.GetType($"{TimeNamespace}.IReminder"));
        Assert.DoesNotContain(
            contracts.GetExportedTypes().Concat(typeof(TimeModule).Assembly.GetExportedTypes()),
            type => type.Name is "IReminder" or "Reminder" or "ITimer" or "IRecurringSchedule");
    }

    [Fact(DisplayName = "ICountdown methods are unsuffixed, aliased, and return CountdownSnapshot")]
    public void CountdownMethodsAreUnsuffixedAndAliased()
    {
        var methods = typeof(ICountdown)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.All(methods, method =>
        {
            Assert.DoesNotContain("Async", method.Name, StringComparison.Ordinal);
            Assert.Equal(
                method.Name,
                method.GetCustomAttribute<AliasAttribute>()?.Alias);
            Assert.Equal(typeof(Task<CountdownSnapshot>), method.ReturnType);
        });

        Assert.Equal(
            [
                nameof(ICountdown.Cancel),
                nameof(ICountdown.Read),
                nameof(ICountdown.Reschedule),
                nameof(ICountdown.Restart),
                nameof(ICountdown.Start),
            ],
            methods.Select(method => method.Name).Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName = "Time runtime public surface is TimeModule only — no product IReminder neuron")]
    public void RuntimePublicSurfaceIsModuleMarkerOnly()
    {
        var exported = typeof(TimeModule).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(TimeModule)], exported);
        Assert.DoesNotContain(
            typeof(TimeModule).Assembly.GetExportedTypes(),
            type => type.Name.Contains("Reminder", StringComparison.Ordinal)
                || type.Name is "ITimer" or "IRecurringSchedule");
    }

    [Fact(DisplayName = "Time runtime compile graph is Kernel + Time.Contracts only")]
    public void RuntimeCompileGraphIsKernelAndContractsOnly()
    {
        Assert.Equal(
            new[] { Kernel, TimeContractsPackage }.Order(StringComparer.Ordinal),
            PackageBoundarySupport.DirectCompileProjectReferencesOf(TimeRuntime)
                .Order(StringComparer.Ordinal));

        Assert.Equal(
            new[] { Abstractions, Kernel, TimeContractsPackage }.Order(StringComparer.Ordinal),
            PackageBoundarySupport.CompileProjectsReachableFrom(TimeRuntime)
                .Order(StringComparer.Ordinal));

        var projects = PackageBoundarySupport.CompileProjectsReachableFrom(TimeRuntime);
        Assert.DoesNotContain(
            projects,
            project => project.StartsWith(PackageInventory.ModulesAi, StringComparison.Ordinal)
                || project.StartsWith(PackageInventory.ModulesTasks, StringComparison.Ordinal)
                || project.StartsWith(PackageInventory.ModulesGoogle, StringComparison.Ordinal)
                || project.StartsWith(PackageInventory.ModulesSalesforce, StringComparison.Ordinal)
                || project.StartsWith(PackageInventory.ModulesFlutter, StringComparison.Ordinal)
                || project.StartsWith(PackageInventory.IntegrationsPrefix, StringComparison.Ordinal)
                || PackageInventory.IsUiFamilyProject(project));
    }
}
