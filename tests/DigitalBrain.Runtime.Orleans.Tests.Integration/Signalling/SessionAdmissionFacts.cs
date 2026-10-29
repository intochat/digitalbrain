using System.Collections.Concurrent;
using DigitalBrain.Capabilities;
using DigitalBrain.Invocation;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Signalling;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Signalling;

public sealed class SessionAdmissionSilo : SessionSilo
{
    internal SessionAdmissionGate Admission { get; } = new();

    protected override Action<IServiceCollection>? ConfigureServices => services =>
    {
        services.Configure<SessionSynapseOptions>(options => options.MaxSessionSynapses = 2);
        services.AddSingleton<IIncomingGrainCallFilter>(Admission);
    };
}

internal sealed class SessionAdmissionGate : IIncomingGrainCallFilter
{
    private readonly ConcurrentDictionary<string, Hold> _holds = new(StringComparer.Ordinal);

    public Hold For(NeuronId source) => _holds.GetOrAdd(source.ToString(), static _ => new());

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        await context.Invoke();
        if (context.InterfaceMethod.DeclaringType == typeof(ISynapseHost) && context.InterfaceMethod.Name == nameof(ISynapseHost.Admit)
            && _holds.TryGetValue(context.TargetId.Key.ToString()!, out var hold))
        {
            hold.Entered.TrySetResult();
            await hold.Release.Task;
        }
    }

    internal sealed class Hold
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public sealed class SessionAdmissionFacts(SessionAdmissionSilo silo) : IClassFixture<SessionAdmissionSilo>
{
    private static readonly string[] Aliases = ["probe.counted"];

    [Fact]
    public async Task Admission_tickets_are_session_bound_unforgeable_and_one_use()
    {
        var key = Key();
        var session = silo.Session(key);
        var ticket = await session.PrepareSubscription(Source(key), Aliases);
        Assert.Equal(0, await silo.Subscriptions(SessionId(key), Aliases[0]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.CommitSubscription("not-issued"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => silo.Session(Key()).CommitSubscription(ticket));

        await session.CommitSubscription(ticket);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.CommitSubscription(ticket));
        Assert.Equal(1, await silo.Subscriptions(SessionId(key), Aliases[0]));
        await session.End();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_ticket_cannot_be_committed_by_another_principal_or_neuron(bool otherPrincipal, bool otherNeuron)
    {
        var key = Key();
        var owner = CallerSession(key, "issuer");
        string ticket;
        using (RuntimeCallerContext.Stamp(new CapabilityCaller("alice", "account", "b1")))
        { ticket = await owner.PrepareSubscription(Source(key), Aliases); }

        using (RuntimeCallerContext.Stamp(new CapabilityCaller(otherPrincipal ? "mallory" : "alice", "account", "b1")))
        {
            var caller = CallerSession(key, otherNeuron ? "other" : "issuer");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => caller.CommitSubscription(ticket));
        }
        Assert.Equal(0, await silo.Subscriptions(SessionId(key), Aliases[0]));
        await owner.End();
    }

    [Fact]
    public async Task Expired_tickets_have_no_effect_and_release_the_pending_capacity()
    {
        var key = Key();
        var session = silo.Session(key);
        var ticket = await session.PrepareSubscription(Source(key), Aliases);
        RuntimeTestHooks.Advance(silo.Services, TimeSpan.FromSeconds(61));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.CommitSubscription(ticket));
        Assert.Equal(0, await silo.Subscriptions(SessionId(key), Aliases[0]));
        await session.CommitSubscription(await session.PrepareSubscription(Source(key), Aliases));
        Assert.Equal(1, await silo.Subscriptions(SessionId(key), Aliases[0]));
        await session.End();
    }

    [Fact]
    public async Task Concurrent_pending_admissions_count_toward_the_cap_before_any_subscription_is_committed()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Key();
        var session = silo.Session(key);
        var first = Source(key + "-first");
        var second = Source(key + "-second");
        var firstGate = silo.Admission.For(first);
        var secondGate = silo.Admission.For(second);
        var firstTicket = session.PrepareSubscription(first, Aliases);
        var secondTicket = session.PrepareSubscription(second, Aliases);
        try
        {
            await Task.WhenAll(firstGate.Entered.Task, secondGate.Entered.Task).WaitAsync(TimeSpan.FromSeconds(5), ct);
            await Assert.ThrowsAsync<SessionLimitException>(() => session.PrepareSubscription(Source(key + "-third"), Aliases));
            Assert.Equal(0, await silo.Subscriptions(SessionId(key), Aliases[0]));
        }
        finally
        {
            firstGate.Release.TrySetResult();
            secondGate.Release.TrySetResult();
        }
        await session.CommitSubscription(await firstTicket.WaitAsync(TimeSpan.FromSeconds(5), ct));
        await session.CommitSubscription(await secondTicket.WaitAsync(TimeSpan.FromSeconds(5), ct));
        var thirdTicket = await session.PrepareSubscription(Source(key + "-third"), Aliases);
        await Assert.ThrowsAsync<SessionLimitException>(() => session.CommitSubscription(thirdTicket));
        await session.End();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task End_or_unsubscribe_invalidates_an_admission_which_is_still_waiting(bool unsubscribe)
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Key();
        var session = silo.Session(key);
        var source = Source(key);
        var gate = silo.Admission.For(source);
        var preparing = session.PrepareSubscription(source, Aliases);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            await (unsubscribe ? session.Unsubscribe(source) : session.End()).WaitAsync(TimeSpan.FromSeconds(5), ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => preparing.WaitAsync(TimeSpan.FromSeconds(5), ct));
            await session.Subscribe(Source(key + "-replacement"), Aliases).WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Equal(1, await silo.Subscriptions(SessionId(key), Aliases[0]));
        }
        finally { gate.Release.TrySetResult(); }
        await session.End();
        Assert.Equal(0, await silo.Subscriptions(SessionId(key), Aliases[0]));
    }

    [Fact]
    public async Task Expiry_releases_the_waiting_prepare_before_the_source_answers()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Key();
        var session = silo.Session(key);
        var source = Source(key);
        var gate = silo.Admission.For(source);
        var preparing = session.PrepareSubscription(source, Aliases);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            RuntimeTestHooks.Advance(silo.Services, TimeSpan.FromSeconds(61));
            var replacement = await session.PrepareSubscription(Source(key + "-replacement"), Aliases).WaitAsync(TimeSpan.FromSeconds(5), ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => preparing.WaitAsync(TimeSpan.FromSeconds(5), ct));
            await session.CommitSubscription(replacement);
            Assert.Equal(1, await silo.Subscriptions(SessionId(key), Aliases[0]));
        }
        finally { gate.Release.TrySetResult(); }
        await session.End();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task End_or_unsubscribe_invalidates_an_already_issued_ticket(bool unsubscribe)
    {
        var key = Key();
        var session = silo.Session(key);
        var source = Source(key);
        var ticket = await session.PrepareSubscription(source, Aliases);
        await (unsubscribe ? session.Unsubscribe(source) : session.End());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.CommitSubscription(ticket));
        Assert.Equal(0, await silo.Subscriptions(SessionId(key), Aliases[0]));
        if (unsubscribe) { await session.End(); }
    }

    private ISessionSynapses CallerSession(string key, string caller)
        => NeuronProxy.Create<ISessionSynapses>(NeuronId.Parse(SessionId(key)), new OrleansInvocationSink(
            silo.Services.GetRequiredService<IGrainFactory>(), silo.Services.GetRequiredService<NeuronTypeRegistry>(),
            NeuronId.Parse("b1/probe/" + caller)));

    private static string Key() => "admission-" + Guid.NewGuid().ToString("N");
    private static string SessionId(string key) => "b1/runtime.session/" + key;
    private static NeuronId Source(string key) => NeuronId.Parse("b1/probe.counter/" + key);
}
