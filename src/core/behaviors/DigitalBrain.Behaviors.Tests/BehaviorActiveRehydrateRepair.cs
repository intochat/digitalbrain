using Xunit;
using DigitalBrain.Behaviors.Runtime;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorActiveRehydrateRepair
{
    [Fact(DisplayName = "Active + Idle + closed gate (pre-Stop fields default) rehydrates as Running with open gate")]
    public void ActiveIdleClosedGateRepairsToRunningOpen()
    {
        var legacy = new BehaviorNeuron.BehaviorData
        {
            Status = BehaviorRevisionStatus.Active,
            ActiveArtifactHash = "legacy-active-hash",
            RunState = BehaviorRunState.Idle,
            ActivationGateOpen = false,
            ActiveTaskIds = [],
            Receipts = new Dictionary<Guid, BehaviorSnapshot>(),
        };

        var repaired = BehaviorNeuron.RepairActiveRehydrate(legacy);

        Assert.Equal(BehaviorRunState.Running, repaired.RunState);
        Assert.True(repaired.ActivationGateOpen);
        Assert.Equal(BehaviorRevisionStatus.Active, repaired.Status);
        Assert.Equal("legacy-active-hash", repaired.ActiveArtifactHash);
    }

    [Fact(DisplayName = "Active + Stopped with closed gate is not repaired (operator Stop is durable)")]
    public void ActiveStoppedClosedGateIsNotRepaired()
    {
        var stopped = new BehaviorNeuron.BehaviorData
        {
            Status = BehaviorRevisionStatus.Active,
            ActiveArtifactHash = "stopped-hash",
            RunState = BehaviorRunState.Stopped,
            ActivationGateOpen = false,
            ActiveTaskIds = [],
            Receipts = new Dictionary<Guid, BehaviorSnapshot>(),
        };

        var repaired = BehaviorNeuron.RepairActiveRehydrate(stopped);

        Assert.Equal(BehaviorRunState.Stopped, repaired.RunState);
        Assert.False(repaired.ActivationGateOpen);
    }

    [Fact(DisplayName = "Empty status Idle defaults are not promoted to Running")]
    public void EmptyIdleIsNotPromoted()
    {
        var empty = BehaviorNeuron.BehaviorData.Empty;
        var repaired = BehaviorNeuron.RepairActiveRehydrate(empty);

        Assert.Equal(BehaviorRunState.Idle, repaired.RunState);
        Assert.False(repaired.ActivationGateOpen);
        Assert.Equal(BehaviorRevisionStatus.Empty, repaired.Status);
    }
}
