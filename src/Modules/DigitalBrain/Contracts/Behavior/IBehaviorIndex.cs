using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Behavior;

[Alias("behavior-index")]
public interface IBehaviorIndex : IGrainWithStringKey
{
    [ReadOnly, Alias("list")]
    Task<IReadOnlyList<string>> List();

    [Alias("remember")]
    Task Remember(string id);
}
