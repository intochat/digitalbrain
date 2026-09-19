using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans;

namespace DigitalBrain.Tests;

[GenerateSerializer]
public sealed record Number([property: Id(0)] int Value) : Signal;
[GenerateSerializer]
public sealed record Text([property: Id(0)] string Value) : Signal;
public interface ITestEmitter : INeuron
{
    Task Emit(int value);
    Task EmitText(string value);
    Task Deactivate();
}
public interface IOtherEmitter : INeuron { Task Emit(int value); }
[GrainType("test-emitter")]
public sealed class TestEmitter : Neuron, ITestEmitter
{
    public Task Emit(int value) => PublishAsync(new Number(value));
    public Task EmitText(string value) => PublishAsync(new Text(value));
    public Task Deactivate() { DeactivateOnIdle(); return Task.CompletedTask; }
}
[GrainType("other-emitter")]
public sealed class OtherEmitter : Neuron, IOtherEmitter
{
    public Task Emit(int value) => PublishAsync(new Number(value));
}
