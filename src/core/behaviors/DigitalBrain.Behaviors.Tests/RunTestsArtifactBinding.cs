using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class RunTestsArtifactBinding(RunTestsArtifactBindingFixture fixture)
{
    [Fact(DisplayName = "RunTests evaluates the signed proposal envelope and rejects digest mismatches")]
    public async Task RunTestsUsesSignedEnvelopeAndRejectsDigestMismatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        fixture.Gate.Reset();
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(BehaviorsFixture.SampleBehavior);

        var proposed = await behavior.Reference.Propose(new ProposeBehaviorRevision(
            CommandId.New(),
            RailPrograms.UnionGreenProgram(),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["install"] = RailPrograms.GreenFeature },
            "Sample",
            "Sample behavior"));

        Assert.False(string.IsNullOrWhiteSpace(proposed.ProposedArtifactHash));

        var green = await behavior.Reference.RunTests(
            new RunBehaviorTests(CommandId.New(), proposed.ProposedArtifactHash!));
        Assert.Equal(BehaviorRevisionStatus.TestsPassed, green.Status);
        Assert.True(green.TestsPassed);

        var envelope = fixture.Gate.LastEnvelope
            ?? throw new InvalidOperationException("BDD gate did not receive an envelope.");
        Assert.NotEmpty(envelope.Manifest.EntryPoints.Contract.Cases);
        Assert.Contains("case.ManualResearchRequest", envelope.CompilerEvidenceJson, StringComparison.Ordinal);
        Assert.Contains("\"policy\":\"contract-only-v1\"", envelope.CompilerEvidenceJson, StringComparison.Ordinal);
        Assert.Contains("ResearchCompanyRequest", envelope.CompilerEvidenceJson, StringComparison.Ordinal);
        Assert.False(
            string.Equals(
                envelope.Manifest.EntryPoints.Contract.OneOfSchemaJson,
                """{"oneOf":[]}""",
                StringComparison.Ordinal));
        Assert.True(
            envelope.BehaviorAssembly.Span.SequenceEqual(fixture.Gate.LastAssemblyBytes.Span),
            "RunTests must pass the envelope assembly bytes to the BDD gate.");

        var rewritten = CanonicalArtifactWriter.Write(envelope);
        Assert.Equal(proposed.ProposedArtifactHash, rewritten.Digest.Value);

        const string foreignHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var mismatch = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Reference.RunTests(
                new RunBehaviorTests(CommandId.New(), foreignHash)));
        Assert.Contains(foreignHash, mismatch.Message, StringComparison.Ordinal);
        Assert.NotEqual(foreignHash, proposed.ProposedArtifactHash);
    }
}

public sealed class RunTestsArtifactBindingFixture : DigitalBrainFixture
{
    public RecordingBehaviorBddGate Gate { get; } = new();

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<BehaviorsModule>();
        var gate = Gate;
        brain.ConfigureServiceEdge(
            services =>
            {
                services.RemoveAll<IBehaviorBddGate>();
                services.AddSingleton<IBehaviorBddGate>(gate);
            },
            gate,
            static recorded => recorded.Reset());
    }
}

public sealed class RecordingBehaviorBddGate : IBehaviorBddGate
{
    private readonly InstallTestsBddGate _inner = new();
    private readonly Lock _gate = new();
    private BehaviorArtifactEnvelope? _lastEnvelope;
    private byte[] _lastAssemblyBytes = [];

    public BehaviorArtifactEnvelope? LastEnvelope
    {
        get
        {
            lock (_gate)
            {
                return _lastEnvelope;
            }
        }
    }

    public ReadOnlyMemory<byte> LastAssemblyBytes
    {
        get
        {
            lock (_gate)
            {
                return _lastAssemblyBytes;
            }
        }
    }

    public BehaviorInstallTestReport Evaluate(
        BehaviorArtifactEnvelope envelope,
        ReadOnlyMemory<byte> assemblyBytes,
        string artifactHash,
        IBehaviorCapabilityResolver capabilities,
        TimeProvider time)
    {
        lock (_gate)
        {
            _lastEnvelope = envelope;
            _lastAssemblyBytes = assemblyBytes.ToArray();
        }

        return _inner.Evaluate(envelope, assemblyBytes, artifactHash, capabilities, time);
    }

    public void Reset()
    {
        lock (_gate)
        {
            _lastEnvelope = null;
            _lastAssemblyBytes = [];
        }
    }
}
