using DigitalBrain.Contracts;
using DigitalBrain.Core;

namespace DigitalBrain.Tests;

public interface IPinger : INeuron
{
    Task Ping(int number);
}

[GenerateSerializer, Alias("tests.csharp.pinged")]
public sealed record Pinged([property: Id(0)] int Number) : Signal;

[GenerateSerializer, Alias("tests.csharp.ignored")]
public sealed record Ignored([property: Id(0)] int Number) : Signal;

// A trigger source: publishes the signal an armed file waits for, and one it must ignore.
public sealed class Pinger : Neuron, IPinger
{
    public async Task Ping(int number)
    {
        await PublishAsync(new Ignored(number));
        await PublishAsync(new Pinged(number));
    }
}
