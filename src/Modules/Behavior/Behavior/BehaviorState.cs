using DigitalBrain.Core;
namespace DigitalBrain.Behavior;

[GenerateSerializer]
[Alias("db.behavior-state")]
internal sealed class BehaviorState
{
    [Id(0)]
    public List<StoredRun> Runs { get; set; } = [];
}
