using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit;

public interface IPinger : INeuron
{
    Task Ping(int number);

    Task<int> Double(int number, CancellationToken cancellationToken = default);

    Task<string> Caller();
}

[PlatformOnly]
public interface IPlatformOnlyPinger : INeuron
{
    Task Ping(int number);
}

[GenerateSerializer, Alias("tests.csharp.pinged")]
public sealed record Pinged([property: Id(0)] int Number) : Signal;

[GenerateSerializer, Alias("tests.csharp.ignored")]
public sealed record Ignored([property: Id(0)] int Number) : Signal;

// A trigger source that publishes the signal an armed file waits for and one it must ignore, and a
// callable contract that reports the caller context the script edge stamped.
public sealed class Pinger : Neuron, IPinger
{
    public async Task Ping(int number)
    {
        await PublishAsync(new Ignored(number));
        await PublishAsync(new Pinged(number));
    }

    public Task<int> Double(int number, CancellationToken cancellationToken = default) => Task.FromResult(number * 2);

    public Task<string> Caller() => Task.FromResult(CallerContextStamper.TryGet(out var caller)
        ? $"{caller.PrincipalId} {caller.Kind} {caller.StampedBy} {caller.AppId}"
        : "unstamped");
}
