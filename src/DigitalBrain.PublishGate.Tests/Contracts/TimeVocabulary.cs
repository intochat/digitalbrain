using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Time;
using Xunit;

namespace DigitalBrain.Tests.Contracts;

public sealed class TimeVocabulary
{
    private static readonly string TimeNamespace =
        typeof(ICountdown).Namespace
        ?? throw new InvalidOperationException($"{nameof(ICountdown)} has no namespace.");

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
}
