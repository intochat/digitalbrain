using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Time;
using Xunit;

namespace DigitalBrain.Tests.Packages;

public sealed class TimeContracts
{
    [Fact]
    public void CountdownIsTheOnlyTimeNeuronCapability()
    {
        var contracts = typeof(ICountdown).Assembly;
        var vocabulary = contracts
            .GetExportedTypes()
            .Where(type => type.Namespace == "DigitalBrain.Time")
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
        Assert.Null(contracts.GetType("DigitalBrain.Time.IReminder"));
    }

    [Fact]
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
}
