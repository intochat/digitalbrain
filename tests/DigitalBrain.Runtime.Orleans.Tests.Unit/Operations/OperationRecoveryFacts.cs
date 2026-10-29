using System.Collections.Immutable;
using System.Net;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;
using DigitalBrain.Runtime.Orleans.Operations;
using DigitalBrain.Spi;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Operations;

public sealed class OperationRecoveryFacts
{
    private static readonly SiloAddress Driver = SiloAddress.New(new IPEndPoint(IPAddress.Loopback, 11111), 10);
    private static readonly SiloAddress Other = SiloAddress.New(new IPEndPoint(IPAddress.Loopback, 22222), 20);

    [Theory]
    [InlineData(SiloStatus.Active)]
    [InlineData(SiloStatus.Joining)]
    [InlineData(SiloStatus.ShuttingDown)]
    [InlineData(SiloStatus.Stopping)]
    public void ARemoteDriverIsNotInterruptedWhileItsExactIncarnationIsStillAlive(SiloStatus status)
        => Assert.False(OperationNeuron.DriverHasEnded(Driver.ToParsableString(), Snapshot((Driver, status), (Other, SiloStatus.Active))));

    [Fact]
    public void UnknownDriverAndUnrelatedLiveSilosDoNotProveInterruption()
    {
        Assert.False(OperationNeuron.DriverHasEnded(Driver.ToParsableString(), Snapshot()));
        Assert.False(OperationNeuron.DriverHasEnded(Driver.ToParsableString(), Snapshot((Other, SiloStatus.Active))));
        Assert.False(OperationNeuron.DriverHasEnded("legacy-or-invalid", Snapshot((Other, SiloStatus.Active))));
    }

    [Fact]
    public void OnlyAnExplicitDeathOrANewerActiveIncarnationProvesInterruption()
    {
        Assert.True(OperationNeuron.DriverHasEnded(Driver.ToParsableString(), Snapshot((Driver, SiloStatus.Dead), (Other, SiloStatus.Active))));
        var next = SiloAddress.New(Driver.Endpoint, Driver.Generation + 1);
        Assert.True(OperationNeuron.DriverHasEnded(Driver.ToParsableString(), Snapshot((next, SiloStatus.Active))));
        Assert.False(OperationNeuron.DriverHasEnded(Driver.ToParsableString(), Snapshot((next, SiloStatus.Joining))));
        Assert.False(OperationNeuron.DriverHasEnded(next.ToParsableString(), Snapshot((Driver, SiloStatus.Active))));
    }

    [Fact]
    public async Task ReconciliationJournalsOneFailureAndNeverTurnsItIntoSuccess()
    {
        var membership = new Membership { CurrentSnapshot = Snapshot((Driver, SiloStatus.Active)) };
        using var services = new ServiceCollection().AddSingleton<IClusterMembershipService>(membership).BuildServiceProvider();
        var context = new FakeNeuronContext(new(new("owner"), "runtime.operation", "pending"), services);
        var neuron = new OperationNeuron();
        NeuronAttachment.Attach(neuron, context);
        await neuron.Begin(new("pending", "owner", "files", "primitive", "{}", null, null, OperationPhase.Running,
            false, 0, null, context.Time.GetUtcNow(), context.Time.GetUtcNow(), Driver.ToParsableString()));
        // Ordinary entity eviction on another live silo must not affect the driver.
        var replacement = new OperationNeuron();
        NeuronAttachment.Attach(replacement, context);
        await NeuronAttachment.Activate(replacement, TestContext.Current.CancellationToken);
        Assert.Equal(OperationPhase.Running, (await replacement.Get())!.Phase);
        membership.CurrentSnapshot = Snapshot((Driver, SiloStatus.Dead));
        Assert.Equal(OperationPhase.Running, (await replacement.Get())!.Phase); // Get remains read-only.
        await replacement.Reconcile();
        await replacement.Reconcile();
        await replacement.Complete(new OperationResult.Succeeded());
        var terminal = Assert.Single(context.MemoryState.Signals.OfType<OperationDone>());
        Assert.Equal("interrupted", Assert.IsType<OperationResult.Failed>(terminal.Result).Code);
        Assert.Equal(OperationPhase.Done, (await replacement.Get())!.Phase);
    }

    private static ClusterMembershipSnapshot Snapshot(params (SiloAddress Address, SiloStatus Status)[] members)
        => new(members.ToImmutableDictionary(item => item.Address, item => new ClusterMember(item.Address, item.Status, "test")), new MembershipVersion(1));

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    public async Task ExactProcessEvidenceSurvivesEndpointChangesAndOverridesStaleMembership(bool? ended, bool membershipSaysDead, bool interrupted)
    {
        var lifetime = new Lifetime(ended);
        var membership = new Membership
        {
            CurrentSnapshot = membershipSaysDead ? Snapshot((Driver, SiloStatus.Dead)) : Snapshot((Other, SiloStatus.Active)),
        };
        using var services = new ServiceCollection().AddSingleton<IClusterMembershipService>(membership)
            .AddSingleton<IOperationDriverLifetime>(lifetime).BuildServiceProvider();
        var context = new FakeNeuronContext(new(new("owner"), "runtime.operation", "process"), services);
        var neuron = new OperationNeuron();
        NeuronAttachment.Attach(neuron, context);
        await neuron.Begin(new("process", "owner", "files", "primitive", "{}", null, null, OperationPhase.Running,
            false, 0, null, context.Time.GetUtcNow(), context.Time.GetUtcNow(), Driver.ToParsableString(), "exact-process"));

        await neuron.Reconcile();
        await neuron.Reconcile();

        Assert.Equal(interrupted ? OperationPhase.Done : OperationPhase.Running, (await neuron.Get())!.Phase);
        Assert.Equal("exact-process", lifetime.Observed);
        if (interrupted)
        {
            var done = Assert.Single(context.MemoryState.Signals.OfType<OperationDone>());
            Assert.Equal("interrupted", Assert.IsType<OperationResult.Failed>(done.Result).Code);
        }
        else { Assert.Empty(context.MemoryState.Signals); }
    }

    [Fact]
    public async Task ProvenStoppedProcessDoesNotRequireAMembershipService()
    {
        using var services = new ServiceCollection().AddSingleton<IOperationDriverLifetime>(new Lifetime(true)).BuildServiceProvider();
        var context = new FakeNeuronContext(new(new("owner"), "runtime.operation", "local"), services);
        var neuron = new OperationNeuron();
        NeuronAttachment.Attach(neuron, context);
        await neuron.Begin(new("local", "owner", "files", "primitive", "{}", null, null, OperationPhase.Running,
            false, 0, null, context.Time.GetUtcNow(), context.Time.GetUtcNow(), Driver.ToParsableString(), "exact-process"));
        await neuron.Reconcile();
        Assert.Equal("interrupted", Assert.IsType<OperationResult.Failed>((await neuron.Get())!.Result).Code);
    }

    [Fact]
    public async Task ALegacyOperationCannotBorrowEvidenceAboutAnotherProcess()
    {
        var lifetime = new Lifetime(true);
        using var services = new ServiceCollection().AddSingleton<IOperationDriverLifetime>(lifetime)
            .AddSingleton<IClusterMembershipService>(new Membership { CurrentSnapshot = Snapshot((Other, SiloStatus.Active)) }).BuildServiceProvider();
        var context = new FakeNeuronContext(new(new("owner"), "runtime.operation", "old"), services);
        var neuron = new OperationNeuron();
        NeuronAttachment.Attach(neuron, context);
        await neuron.Begin(new("old", "owner", "files", "primitive", "{}", null, null, OperationPhase.Running,
            false, 0, null, context.Time.GetUtcNow(), context.Time.GetUtcNow(), Driver.ToParsableString()));
        await neuron.Reconcile();
        Assert.Equal(OperationPhase.Running, (await neuron.Get())!.Phase);
        Assert.Null(lifetime.Observed);
    }

    private sealed class Lifetime(bool? ended) : IOperationDriverLifetime
    {
        public string? Observed { get; private set; }
        public string? Capture() => "exact-process";
        public bool? HasEnded(string token) { Observed = token; return ended; }
    }

    [Fact]
    public async Task OlderSavedOperationsWithoutADriverAreNotDeclaredInterrupted()
    {
        using var services = new ServiceCollection().AddSingleton<IClusterMembershipService>(new Membership
        { CurrentSnapshot = Snapshot((Driver, SiloStatus.Dead)) }).BuildServiceProvider();
        var context = new FakeNeuronContext(new(new("owner"), "runtime.operation", "legacy"), services);
        await context.MemoryState.Save(new OperationState("legacy", "owner", "files", "primitive", "{}", null, null,
            OperationPhase.Running, false, 0, null, context.Time.GetUtcNow(), context.Time.GetUtcNow()), []);
        var neuron = new OperationNeuron();
        NeuronAttachment.Attach(neuron, context);
        await NeuronAttachment.Activate(neuron, TestContext.Current.CancellationToken);
        await neuron.Reconcile();
        Assert.Equal(OperationPhase.Running, (await neuron.Get())!.Phase);
        Assert.Empty(context.MemoryState.Signals);
    }

    private sealed class Membership : IClusterMembershipService
    {
        public ClusterMembershipSnapshot CurrentSnapshot { get; set; } = Snapshot();
        public IAsyncEnumerable<ClusterMembershipSnapshot> MembershipUpdates => throw new NotSupportedException();
        public ValueTask Refresh(MembershipVersion minimumVersion = default, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public Task<bool> TryKill(SiloAddress siloAddress) => throw new NotSupportedException();
    }
}
