using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class BehaviorLifecycleTests
{
    [Fact]
    public async Task Admission_stores_only_current_source_and_same_source_readmission_gets_a_new_revision()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var behaviors = simulation.Brain.Get<IBehaviors>();
        var query = simulation.Grains.GetGrain<IBehaviorsKernel>(behaviors.Id.ToGrainId());
        var cancellation = TestContext.Current.CancellationToken;
        await behaviors.SendAsync(new AdmitBehavior("review", "return 1;"), cancellation);
        var first = Assert.Single(await query.ReadCurrent());
        await behaviors.SendAsync(new AdmitBehavior("review", "return 1;"), cancellation);
        var second = Assert.Single(await query.ReadCurrent());
        Assert.NotEqual(first.Revision, second.Revision);
        Assert.Equal(BehaviorStatus.Admitted, second.Status);

        var typed = await behaviors.RequestAsync(new ReadBehaviors(), cancellation);
        var read = Assert.Single(typed.Behaviors);
        Assert.Equal(second.Revision, read.Revision);
        Assert.Equal(second.Source, read.Source);
        Assert.Equal(second.Status, read.Status);
    }

    [Fact]
    public async Task Old_revision_reports_cannot_overwrite_replacement_or_restore_removed_behavior()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var behaviors = simulation.Brain.Get<IBehaviors>();
        var query = simulation.Grains.GetGrain<IBehaviorsKernel>(behaviors.Id.ToGrainId());
        var cancellation = TestContext.Current.CancellationToken;
        await behaviors.SendAsync(new AdmitBehavior("review", "broken"), cancellation);
        var first = Assert.Single(await query.ReadCurrent());
        await behaviors.SendAsync(new AdmitBehavior("review", "return 2;"), cancellation);
        var second = Assert.Single(await query.ReadCurrent());
        await behaviors.SendAsync(new ReportBehaviorStatus(
            first.Name, first.Revision, BehaviorStatus.Failed, "Compilation failed.", ["CS1002"]), cancellation);
        Assert.Equal(second.Revision, Assert.Single(await query.ReadCurrent()).Revision);
        Assert.Equal(BehaviorStatus.Admitted, Assert.Single(await query.ReadCurrent()).Status);

        await behaviors.SendAsync(new ReportBehaviorStatus(
            second.Name, second.Revision, BehaviorStatus.Failed, "Compilation failed.", ["CS0103"]), cancellation);
        Assert.Equal("CS0103", Assert.Single(Assert.Single(await query.ReadCurrent()).Diagnostics));
        await behaviors.SendAsync(new RemoveBehavior("review"), cancellation);
        await behaviors.SendAsync(new ReportBehaviorStatus(
            second.Name, second.Revision, BehaviorStatus.Completed, "late completion", []), cancellation);
        Assert.Empty(await query.ReadCurrent());
    }

    [Fact]
    public async Task Admission_remembers_the_authenticated_delivery_principal()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var behaviors = simulation.Brain.Get<IBehaviors>();
        var principal = PrincipalId.New();
        using (VerifiedActor.Enter(new ActorContext(principal, "alice")))
        {
            await behaviors.SendAsync(new AdmitBehavior("review", "return 1;"), TestContext.Current.CancellationToken);
        }
        var query = simulation.Grains.GetGrain<IBehaviorsKernel>(behaviors.Id.ToGrainId());
        Assert.Equal(principal, Assert.Single(await query.ReadCurrent()).Principal);
    }
}
