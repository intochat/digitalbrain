using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Host;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class AuthoredProgramEmission(AuthoredHostFixture fixture)
{
    private const string SpeakingBehavior = "com.digitalbrain.speaking";
    private const string HearingBehavior = "com.digitalbrain.hearing";
    private const string PingBehavior = "com.digitalbrain.ping-side";
    private const string PongBehavior = "com.digitalbrain.pong-side";

    private static string SpeakingProgram()
        => AuthoredHostHarness.RelayProgram(
            "ProbeCyclePing",
            AuthoredHostHarness.PingFactContractId,
            "ProbeFactHeard",
            AuthoredHostHarness.HeardFactContractId);

    [Fact(
        Timeout = 180_000,
        DisplayName = "a real compiled program woken by a fact speaks its declared fact and the kernel delivers it to a subscriber")]
    public async Task AuthoredProgramSpeaksItsDeclaredFactToASubscriber()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var speaker = test.Neuron<IBehaviorNeuron>(SpeakingBehavior);
        var hearer = test.Neuron<IBehaviorNeuron>(HearingBehavior);
        var opener = test.Neuron<IAuthoredCycleOpener>("opener");

        var active = await AuthoredHostHarness.ActivateAsync(
            test,
            speaker,
            SpeakingProgram(),
            cancellationToken);
        Assert.Equal(BehaviorRevisionStatus.Active, active.Status);

        await AuthoredHostHarness.ActivateAsync(
            test,
            hearer,
            AuthoredHostHarness.RelayProgram(
                "ProbeFactHeard",
                AuthoredHostHarness.HeardFactContractId,
                "ProbeCyclePong",
                AuthoredHostHarness.PongFactContractId),
            cancellationToken);

        var spokenWait = speaker.Outgoing.NextAsync<ProbeFactHeard>(cancellationToken);
        var auditWait = speaker.Outgoing.NextAsync<BehaviorFactEmitted>(cancellationToken);
        var heardWait = hearer.Incoming.NextAsync<ProbeFactHeard>(cancellationToken);
        await opener.Reference.OpenCycle("authored");

        var spoken = await spokenWait;
        var audit = await auditWait;

        Assert.Equal("authored", spoken.Synapse.Label);
        Assert.Equal(AuthoredHostHarness.HeardFactContractId, audit.Synapse.EmitAlias);
        Assert.Equal(active.ActiveArtifactHash, audit.Synapse.ArtifactHash);
        Assert.Equal(spoken.CorrelationId, audit.CorrelationId);

        var heard = await heardWait;

        Assert.Equal("authored", heard.Synapse.Label);
        Assert.Equal(speaker.Id, heard.Caller);
        Assert.Equal(spoken.CorrelationId, heard.CorrelationId);
    }

    [Fact(
        Timeout = 180_000,
        DisplayName = "an alias the signed manifest does not grant is refused across the broker with the typed reason")]
    public async Task UndeclaredAliasIsRefusedAcrossTheBrokerOnTheAttemptsOwnIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var speaker = test.Neuron<IBehaviorNeuron>(SpeakingBehavior);
        var opener = test.Neuron<IAuthoredCycleOpener>("opener");

        await AuthoredHostHarness.ActivateAsync(test, speaker, SpeakingProgram(), cancellationToken);

        var wokeWait = speaker.Outgoing.NextAsync<BehaviorWokeOnFact>(cancellationToken);
        await opener.Reference.OpenCycle("refusal");
        var woke = await wokeWait;

        // The compiler refuses to author an emit the catalog does not declare, so an undeclared
        // emission can only ever come from a host asserting an alias its behavior was not granted.
        // That is exactly the threat this leg exists to refuse, driven on the attempt's own identity.
        var siloServices = fixture.Silo.Services
            ?? throw new InvalidOperationException("The authored host fixture never built its broker factory.");
        var client = siloServices
            .GetRequiredService<IBehaviorHostBrokerClientFactory>()
            .Create(
                test.Client.Owner,
                woke.Synapse.Task,
                woke.Synapse.Attempt,
                NeuronId.For<IWorker>(test.Client.Owner, woke.Synapse.Task.Name));

        var refused = await Assert.ThrowsAsync<BehaviorHostException>(async () =>
            await client.EmitFactAsync(
                new BehaviorId(SpeakingBehavior),
                AuthoredHostHarness.PongFactContractId,
                System.Text.Encoding.UTF8.GetBytes("""{"Label":"forbidden"}"""),
                BehaviorFactEmission.MaximumHops,
                cancellationToken));

        Assert.Equal(BehaviorFactEmission.UndeclaredAlias, refused.Reason);

        var journaled = await speaker.Outgoing.NextAsync<BehaviorFactEmitRefused>(cancellationToken);
        Assert.Equal(BehaviorFactEmission.UndeclaredAlias, journaled.Synapse.Reason);
        Assert.Equal(AuthoredHostHarness.PongFactContractId, journaled.Synapse.AttemptedAlias);
        Assert.Empty(await speaker.Outgoing.ReadAsync<ProbeCyclePong>(
            afterSequence: 0,
            cancellationToken));
    }

    [Fact(
        Timeout = 300_000,
        DisplayName = "an authored A-to-B-to-A cycle terminates on the typed hop budget refusal instead of looping")]
    public async Task AuthoredCycleTerminatesOnTheHopBudgetRefusal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var pingSide = test.Neuron<IBehaviorNeuron>(PingBehavior);
        var pongSide = test.Neuron<IBehaviorNeuron>(PongBehavior);
        var opener = test.Neuron<IAuthoredCycleOpener>("opener");

        // Ping-side hears Ping and speaks Pong; pong-side hears Pong and speaks Ping. Both sides
        // are authored programs, so the cycle closes entirely through compiled behavior code.
        await AuthoredHostHarness.ActivateAsync(
            test,
            pingSide,
            AuthoredHostHarness.RelayProgram(
                "ProbeCyclePing",
                AuthoredHostHarness.PingFactContractId,
                "ProbeCyclePong",
                AuthoredHostHarness.PongFactContractId),
            cancellationToken);
        await AuthoredHostHarness.ActivateAsync(
            test,
            pongSide,
            AuthoredHostHarness.RelayProgram(
                "ProbeCyclePong",
                AuthoredHostHarness.PongFactContractId,
                "ProbeCyclePing",
                AuthoredHostHarness.PingFactContractId),
            cancellationToken);

        var pingExhausted = pingSide.Outgoing.NextAsync<BehaviorFactEmitRefused>(cancellationToken);
        var pongExhausted = pongSide.Outgoing.NextAsync<BehaviorFactEmitRefused>(cancellationToken);
        await opener.Reference.OpenCycle("loop");

        var refused = await Task.WhenAny(pingExhausted, pongExhausted);
        var exhausted = await refused;

        Assert.Equal(BehaviorFactEmission.HopBudgetExhausted, exhausted.Synapse.Reason);

        var pingWakes = await pingSide.Outgoing.ReadAsync<BehaviorWokeOnFact>(
            afterSequence: 0,
            cancellationToken);
        var pongWakes = await pongSide.Outgoing.ReadAsync<BehaviorWokeOnFact>(
            afterSequence: 0,
            cancellationToken);

        // Bounded, not merely finite: the ceiling is what stops it, so the whole cycle cannot
        // have taken more turns than the budget allows.
        Assert.InRange(pingWakes.Count + pongWakes.Count, 2, BehaviorFactEmission.MaximumHops);
    }
}
