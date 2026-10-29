using System.Collections.Concurrent;
using DigitalBrain.Capabilities;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

public interface IForger
{
    /// <summary>Asks for a synapse while naming a caller in its own arguments, as a forging module would.</summary>
    Task FollowClaiming(string source, string principal, string brain);
}

/// <summary>A subscriber that puts a caller in the call body; the runtime must not read it.</summary>
[Neuron("probe.forger")]
public sealed class ForgerNeuron : Neuron, IForger
{
    public static readonly ConcurrentDictionary<string, CapabilityCaller> Claimed = new();

    public Task FollowClaiming(string source, string principal, string brain)
    {
        Claimed[Id.ToString()] = new CapabilityCaller(principal, "acct-forged", brain);
        return Context.Synapses.Ensure<Counted>(NeuronId.Parse(source));
    }
}

public interface IScoped
{
    Task<int> Ping();
}

/// <summary>A source that admits only a call stamped with principal <c>alice</c> of its own brain, recording what it saw.</summary>
[Neuron("probe.scoped")]
public sealed class ScopedNeuron : Neuron, IScoped
{
    public static readonly ConcurrentDictionary<string, CapabilityCaller?> Seen = new();

    public Task<int> Ping() => Task.FromResult(1);

    protected override async Task AdmitSynapse(SynapseRequest request)
    {
        Seen[request.Subscriber.ToString()] = request.Caller;
        await base.AdmitSynapse(request);
        if (request.Caller is not { PrincipalId: "alice" } caller || caller.BrainId != Id.Brain.Value)
        {
            throw new UnauthorizedAccessException($"{request.Subscriber} is not admitted for {request.Caller?.PrincipalId ?? "no caller"}.");
        }
    }
}

// 04-19, R4-19: AdmitSynapse sees the trusted caller stamp from RuntimeCallerContext, never a caller the subscriber sends.
public sealed class AdmissionCallerFacts(SynapseSilo silo) : IClassFixture<SynapseSilo>
{
    private const string Source = "ac/probe.scoped/s";

    [Fact]
    public async Task Admission_receives_the_stamped_caller_and_admits_it()
    {
        var subscriber = "ac/probe.forger/stamped";
        using (RuntimeCallerContext.Stamp(new CapabilityCaller("alice", "acct-1", "ac", "mod.tables")))
        {
            await silo.Get<IForger>(subscriber).FollowClaiming(Source, "alice", "ac");
        }
        var seen = ScopedNeuron.Seen[subscriber];
        Assert.Equal(new CapabilityCaller("alice", "acct-1", "ac", "mod.tables"), seen);
    }

    [Fact]
    public async Task A_caller_forged_in_the_call_body_is_ignored_and_an_unstamped_call_is_refused()
    {
        var subscriber = "ac/probe.forger/forged";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => silo.Get<IForger>(subscriber).FollowClaiming(Source, "alice", "ac"));
        Assert.Equal("alice", ForgerNeuron.Claimed[subscriber].PrincipalId);
        Assert.Null(ScopedNeuron.Seen[subscriber]);
    }

    [Fact]
    public async Task A_stamped_caller_wins_over_a_different_caller_in_the_body()
    {
        var subscriber = "ac/probe.forger/mallory";
        using (RuntimeCallerContext.Stamp(new CapabilityCaller("mallory", "acct-2", "ac")))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => silo.Get<IForger>(subscriber).FollowClaiming(Source, "alice", "ac"));
        }
        Assert.Equal("mallory", ScopedNeuron.Seen[subscriber]?.PrincipalId);
    }
}
