using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
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

public interface ISiloBrainListener : IGrainWithStringKey
{
    Task<bool> HubIsPresent();
    Task Listen(string sourceId);
    Task<int> Last();
}

[GrainType("silo-brain-listener")]
public sealed class SiloBrainListener : Grain, ISiloBrainListener
{
    private int _last;
    private ISignalSubscription<Number>? _subscription;

    public Task<bool> HubIsPresent() => Task.FromResult(ServiceProvider.GetService<LocalSignalHub>() is not null);

    public async Task Listen(string sourceId)
    {
        var brain = ServiceProvider.GetRequiredService<IDigitalBrain>();
        var source = brain.Get<ITestEmitter>(sourceId);
        _subscription = await brain.SubscribeAsync<Number>(source);
        _ = Drain();
    }

    public Task<int> Last() => Task.FromResult(_last);

    private async Task Drain()
    {
        if (_subscription is null) { return; }
        await foreach (var number in _subscription.ReadAllAsync())
        {
            _last = number.Value;
        }
    }
}