using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Behavior;

[Alias("behavior")]
public interface IBehavior : IGrainWithStringKey
{
    [ReadOnly, Alias("read")]
    Task<BehaviorSnapshot> Read();

    [Alias("write")]
    Task<BehaviorSnapshot> Write(BehaviorSnapshot snapshot, long? expectedVersion);

    [ReadOnly, Alias("read-run")]
    Task<BehaviorRunSnapshot?> ReadRun(string runId);

    [Alias("record-run")]
    Task<BehaviorRunSnapshot> RecordRun(BehaviorRunSnapshot run);
}
