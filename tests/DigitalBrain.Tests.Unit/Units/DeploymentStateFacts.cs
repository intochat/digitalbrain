using DigitalBrain.Units;
using Xunit;

namespace DigitalBrain.Tests.Unit.Units;

public sealed class DeploymentStateFacts
{
    private static StoredBuild MakeBuild(string sourceKey = "Me.Test", string hash = "h1", IReadOnlyList<string>? aliases = null, IReadOnlyDictionary<string, string>? needs = null)
    {
        aliases ??= ["test.alias"];
        needs ??= aliases.ToDictionary(a => a, _ => "needs1", StringComparer.Ordinal);
        var image = new BuildImage(new BuildRef(sourceKey, hash), "TestAssembly", [1, 2, 3], [4, 5, 6]);
        return new StoredBuild(image, new Dictionary<string, string> { ["Test.cs"] = "// code" }, null, aliases, needs);
    }

    [Fact]
    public void Propose_NewBuild_NoNeedsDiff_BecomesReadyToPromote()
    {
        var initial = new DeploymentState();
        var build = MakeBuild();

        var state = initial.Propose(build, []);

        Assert.NotNull(state.Staged);
        Assert.Same(build, state.Staged.Build);
        Assert.Empty(state.Staged.Waiting);
        Assert.True(state.ReadyToPromote);
    }

    [Fact]
    public void Propose_NewBuild_WithNeedsDiff_StagedWithWaiting()
    {
        var initial = new DeploymentState();
        var build = MakeBuild(aliases: ["alias1", "alias2"]);

        var state = initial.Propose(build, ["alias1", "alias2"]);

        Assert.NotNull(state.Staged);
        Assert.Equal(2, state.Staged.Waiting.Count);
        Assert.Contains("alias1", state.Staged.Waiting.Keys);
        Assert.Contains("alias2", state.Staged.Waiting.Keys);
        Assert.False(state.ReadyToPromote);
    }

    [Fact]
    public void Propose_WhileStaged_SupersedesOldStage()
    {
        var build1 = MakeBuild(hash: "h1");
        var build2 = MakeBuild(hash: "h2");

        var state = new DeploymentState().Propose(build1, ["test.alias"]);
        Assert.Equal("h1", state.Staged!.Build.Ref.Hash);

        state = state.Propose(build2, []);
        Assert.Equal("h2", state.Staged!.Build.Ref.Hash);
        Assert.True(state.ReadyToPromote);
    }

    [Fact]
    public void Asked_UpdatesWaitingPlanIds()
    {
        var build = MakeBuild(aliases: ["a1", "a2"]);
        var state = new DeploymentState().Propose(build, ["a1", "a2"]);

        var plans = new Dictionary<string, string> { ["a1"] = "plan-1", ["a2"] = "plan-2" };
        var asked = state.Asked(plans);

        Assert.Equal("plan-1", asked.Staged!.Waiting["a1"]);
        Assert.Equal("plan-2", asked.Staged!.Waiting["a2"]);
        Assert.False(asked.ReadyToPromote);
    }

    [Fact]
    public void Asked_ArbitraryAlias_NotAddedToWaiting()
    {
        var build = MakeBuild(aliases: ["a1"]);
        var state = new DeploymentState().Propose(build, ["a1"]);

        var plans = new Dictionary<string, string> { ["a1"] = "plan-1", ["unrelated.alias"] = "plan-2" };
        var asked = state.Asked(plans);

        Assert.Equal("plan-1", asked.Staged!.Waiting["a1"]);
        Assert.False(asked.Staged.Waiting.ContainsKey("unrelated.alias"));
    }

    [Fact]
    public void Approve_RemovesAliasFromWaiting()
    {
        var build = MakeBuild(aliases: ["a1", "a2"]);
        var state = new DeploymentState().Propose(build, ["a1", "a2"]).Asked(new Dictionary<string, string> { ["a1"] = "p1", ["a2"] = "p2" });

        var afterA1 = state.Approve("a1", "needs1");

        Assert.False(afterA1.Staged!.Waiting.ContainsKey("a1"));
        Assert.True(afterA1.Staged!.Waiting.ContainsKey("a2"));
        Assert.False(afterA1.ReadyToPromote);
    }

    [Fact]
    public void Approve_LastWaitingAlias_BecomesReadyToPromote()
    {
        var build = MakeBuild(aliases: ["a1"]);
        var state = new DeploymentState().Propose(build, ["a1"]).Asked(new Dictionary<string, string> { ["a1"] = "p1" });

        var approved = state.Approve("a1", "needs1");

        Assert.Empty(approved.Staged!.Waiting);
        Assert.True(approved.ReadyToPromote);
    }

    [Fact]
    public void Approve_WrongNeedsHash_Ignored()
    {
        var build = MakeBuild(aliases: ["a1"]);
        var state = new DeploymentState().Propose(build, ["a1"]).Asked(new Dictionary<string, string> { ["a1"] = "p1" });

        var unchanged = state.Approve("a1", "wrong-hash");

        Assert.Same(state, unchanged);
        Assert.False(unchanged.ReadyToPromote);
    }

    [Fact]
    public void Deny_DropsStagedBuild()
    {
        var build = MakeBuild();
        var state = new DeploymentState().Propose(build, ["test.alias"]).Asked(new Dictionary<string, string> { ["test.alias"] = "plan-1" });

        var denied = state.Deny("test.alias", "plan-1");

        Assert.Null(denied.Staged);
        Assert.False(denied.ReadyToPromote);
    }

    [Fact]
    public void Deny_WrongPlanId_Ignored()
    {
        var build = MakeBuild();
        var state = new DeploymentState().Propose(build, ["test.alias"]).Asked(new Dictionary<string, string> { ["test.alias"] = "plan-1" });

        var unchanged = state.Deny("test.alias", "wrong-plan");

        Assert.Same(state, unchanged);
        Assert.NotNull(unchanged.Staged);
    }

    [Fact]
    public void Deny_UnrelatedAlias_Ignored()
    {
        var build = MakeBuild();
        var state = new DeploymentState().Propose(build, ["test.alias"]).Asked(new Dictionary<string, string> { ["test.alias"] = "plan-1" });

        var unchanged = state.Deny("other.alias", "plan-1");

        Assert.Same(state, unchanged);
        Assert.NotNull(unchanged.Staged);
    }

    [Fact]
    public void Supersede_DropsStagedBuild()
    {
        var build = MakeBuild();
        var state = new DeploymentState().Propose(build, ["test.alias"]);

        var superseded = state.Supersede();

        Assert.Null(superseded.Staged);
    }

    [Fact]
    public void Promote_MovesStagedToLive_ClearsStaged_SetsTimestamp()
    {
        var build = MakeBuild();
        var state = new DeploymentState().Propose(build, []);
        var now = DateTimeOffset.UtcNow;

        var promoted = state.Promote(now);

        Assert.Null(promoted.Staged);
        Assert.NotNull(promoted.Live);
        Assert.Same(build.Image, promoted.Live.Image);
        Assert.Equal(now, promoted.At);
    }

    [Fact]
    public void Promote_WhenNoStage_Throws()
    {
        var state = new DeploymentState();
        Assert.Throws<InvalidOperationException>(() => state.Promote(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Refuse_LiveBuild_SetsRebuildReason()
    {
        var build = MakeBuild(hash: "h1");
        var state = new DeploymentState().Propose(build, []).Promote(DateTimeOffset.UtcNow);

        var refused = state.Refuse("h1", "banned api");

        Assert.NotNull(refused.Live);
        Assert.Equal("banned api", refused.Live.RebuildReason);
    }

    [Fact]
    public void Refuse_StagedBuild_DropsStage()
    {
        var build = MakeBuild(hash: "h1");
        var state = new DeploymentState().Propose(build, ["test.alias"]);

        var refused = state.Refuse("h1", "banned api");

        Assert.Null(refused.Staged);
    }

    [Fact]
    public void Retire_ResetsStateToEmpty()
    {
        var build1 = MakeBuild(hash: "h1");
        var build2 = MakeBuild(hash: "h2");
        var state = new DeploymentState().Propose(build1, []).Promote(DateTimeOffset.UtcNow).Propose(build2, ["test.alias"]);

        var retired = state.Retire();

        Assert.Null(retired.Live);
        Assert.Null(retired.Staged);
    }

    [Fact]
    public void Ref_ReturnsCorrectView()
    {
        var liveBuild = MakeBuild(hash: "live");
        var proposedBuild = MakeBuild(hash: "prop");

        var state = new DeploymentState().Propose(liveBuild, []).Promote(DateTimeOffset.UtcNow).Propose(proposedBuild, ["test.alias"]);

        Assert.Equal("live", state.Ref(UnitView.Live)!.Value.Hash);
        Assert.Equal("prop", state.Ref(UnitView.Proposed)!.Value.Hash);
    }
}
