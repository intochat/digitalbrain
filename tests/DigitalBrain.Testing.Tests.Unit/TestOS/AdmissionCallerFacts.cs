using System.Collections.Concurrent;
using DigitalBrain.Capabilities;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

public interface IClaimingSink
{
    Task SubscribeClaiming(NeuronId source, string principal);
}

[Neuron("rx.claiming")]
public sealed class ClaimingSink : Neuron, IClaimingSink
{
    public Task SubscribeClaiming(NeuronId source, string principal)
    {
        _ = new CapabilityCaller(principal, "acct-forged", Id.Brain.Value); // named in the body; nothing reads it
        return Context.Synapses.Ensure<Number>(source);
    }
}

[Neuron("rx.scoped")]
public sealed class ScopedSource : Neuron<Tally>, INumberSource
{
    public static readonly ConcurrentDictionary<string, CapabilityCaller?> Seen = new();

    public Task Emit(int value) => Save(State with { Total = value }, new Number(value));

    public Task EmitOther() => Save(State, new Other());

    protected override async Task AdmitSynapse(SynapseRequest request)
    {
        Seen[request.Subscriber.ToString()] = request.Caller;
        await base.AdmitSynapse(request);
        if (request.Caller?.PrincipalId != "alice") { throw new UnauthorizedAccessException("not alice"); }
    }
}

// 04-19: TestOS puts its Caller on admission; a caller in the subscriber's call body is never read.
public sealed class AdmissionCallerFacts
{
    private static readonly NeuronId Source = NeuronId.Parse("adm/rx.scoped/s");

    [Fact]
    public async Task Admission_sees_the_caller_the_test_set_and_ignores_one_in_the_body()
    {
        await using var os = OS.Local().Register<ScopedSource>().Register<ClaimingSink>();
        var forged = NeuronId.Parse("adm/rx.claiming/forged");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => os.Get<IClaimingSink>(forged).SubscribeClaiming(Source, "alice"));
        Assert.Null(ScopedSource.Seen[forged.ToString()]);
        Assert.Empty(os.SynapsesOf(forged));

        os.Caller = new CapabilityCaller("alice", "acct-1", "adm", "mod.tables");
        var stamped = NeuronId.Parse("adm/rx.claiming/stamped");
        await os.Get<IClaimingSink>(stamped).SubscribeClaiming(Source, "mallory");
        Assert.Equal(os.Caller, ScopedSource.Seen[stamped.ToString()]);
        Assert.Single(os.SynapsesOf(stamped));
    }

    [Fact]
    public async Task The_legacy_admission_overload_still_decides_when_only_it_is_overridden()
    {
        await using var os = OS.Local().Register<ClosedSource>().Register<ClaimingSink>();
        os.Caller = new CapabilityCaller("alice", "acct-1", "adm");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => os.Get<IClaimingSink>(NeuronId.Parse("adm/rx.claiming/k")).SubscribeClaiming(NeuronId.Parse("adm/rx.closed/s"), "alice"));
    }
}
