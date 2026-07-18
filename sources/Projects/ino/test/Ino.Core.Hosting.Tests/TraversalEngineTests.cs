using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Tests.Fixtures;
using Ino.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Xunit;

namespace Ino.Core.Hosting.Tests;

/// <summary>
/// Slice A: <see cref="TraversalEngine"/> is the in-process helper plans use to
/// walk the neuron graph during a single execution. These tests exercise the
/// three primitives directly against the test silo's journaled neurons and a
/// substituted <see cref="IFirePort"/> / <see cref="IChatClient"/>.
/// </summary>
[Collection(nameof(InoTestCollection))]
public sealed class TraversalEngineTests
{
    private readonly InoTestSiloFixture _fixture;

    public TraversalEngineTests(InoTestSiloFixture fixture)
    {
        _fixture = fixture;
    }

    static NeuronContext Ctx(IFirePort firePort) =>
        new(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.Ambient(DomainId.From("kernel")),
            SourceStream: new StreamKey("test:engine"))
        {
            FirePort = firePort,
            Logger = NullLogger.Instance,
        };

    [Fact]
    public async Task VisitAsync_returns_full_journal_when_query_is_All()
    {
        var key = $"engine-visit-{Guid.NewGuid():n}";
        var neuron = _fixture.Grains.GetGrain<ITestNeuron>(key);
        var corr = Guid.NewGuid().ToString("n");
        await neuron.ApplyEventAsync(new TestEvent("a", 1), corr);
        await neuron.ApplyEventAsync(new TestEvent("b", 2), corr);
        await neuron.ApplyEventAsync(new TestEvent("c", 3), corr);

        var engine = new TraversalEngine(
            _fixture.Grains,
            Substitute.For<IFirePort>(),
            Ctx(Substitute.For<IFirePort>()));

        var envelopes = await engine.VisitAsync<TestEvent>(
            key, RecallQuery<TestEvent>.All, TestContext.Current.CancellationToken);

        Assert.Equal(3, envelopes.Count);
        Assert.Equal("a", envelopes[0].Payload.Text);
        Assert.Equal("c", envelopes[^1].Payload.Text);
    }

    [Fact]
    public async Task VisitAsync_applies_Where_predicate_in_process()
    {
        var key = $"engine-where-{Guid.NewGuid():n}";
        var neuron = _fixture.Grains.GetGrain<ITestNeuron>(key);
        var corr = Guid.NewGuid().ToString("n");
        await neuron.ApplyEventAsync(new TestEvent("keep", 5), corr);
        await neuron.ApplyEventAsync(new TestEvent("drop", -1), corr);
        await neuron.ApplyEventAsync(new TestEvent("keep", 10), corr);

        var engine = new TraversalEngine(
            _fixture.Grains,
            Substitute.For<IFirePort>(),
            Ctx(Substitute.For<IFirePort>()));

        var query = new RecallQuery<TestEvent> { Where = e => e.Delta > 0 };
        var envelopes = await engine.VisitAsync<TestEvent>(
            key, query, TestContext.Current.CancellationToken);

        // Frequency-aggregation primitive (scenario 1: "home") composes from
        // this shape — filter the journal, then count/group locally.
        Assert.Equal(2, envelopes.Count);
        Assert.All(envelopes, e => Assert.True(e.Payload.Delta > 0));
    }

    [Fact]
    public async Task VisitAsync_filters_by_Since_timestamp()
    {
        var key = $"engine-since-{Guid.NewGuid():n}";
        var neuron = _fixture.Grains.GetGrain<ITestNeuron>(key);
        var corr = Guid.NewGuid().ToString("n");

        await neuron.ApplyEventAsync(new TestEvent("old", 1), corr);
        var cutoff = DateTimeOffset.UtcNow;
        // Tiny sleep so the second envelope's timestamp is strictly greater
        // than the cutoff. The Since predicate uses >= but the test asserts
        // we *exclude* the older entry, so the boundary has to land between.
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await neuron.ApplyEventAsync(new TestEvent("new", 2), corr);

        var engine = new TraversalEngine(
            _fixture.Grains,
            Substitute.For<IFirePort>(),
            Ctx(Substitute.For<IFirePort>()));

        var envelopes = await engine.VisitAsync<TestEvent>(
            key,
            new RecallQuery<TestEvent> { Since = cutoff },
            TestContext.Current.CancellationToken);

        Assert.Single(envelopes);
        Assert.Equal("new", envelopes[0].Payload.Text);
    }

    [Fact]
    public async Task FireAsync_threads_bound_context_into_firePort()
    {
        var firePort = Substitute.For<IFirePort>();
        firePort.Fire(Arg.Any<TestEvent>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok("ok"));

        var ctx = Ctx(firePort);
        var engine = new TraversalEngine(_fixture.Grains, firePort, ctx);

        var result = await engine.FireAsync(
            new TestEvent("payload", 1), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        await firePort.Received(1).Fire(
            Arg.Any<TestEvent>(),
            Arg.Is<NeuronContext>(c => c != null && c.CorrelationId == ctx.CorrelationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReasonAsync_throws_when_no_chat_client_registered()
    {
        var engine = new TraversalEngine(
            _fixture.Grains,
            Substitute.For<IFirePort>(),
            Ctx(Substitute.For<IFirePort>()));

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            engine.ReasonAsync("classify", new { x = 1 }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReasonAsync_invokes_chat_client_with_instruction_and_serialized_context()
    {
        var chat = Substitute.For<IChatClient>();
        IList<ChatMessage>? capturedMessages = null;
        chat.GetResponseAsync(
                Arg.Do<IEnumerable<ChatMessage>>(m => capturedMessages = m.ToList()),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"))));

        var engine = new TraversalEngine(
            _fixture.Grains,
            Substitute.For<IFirePort>(),
            Ctx(Substitute.For<IFirePort>()),
            chat);

        var result = await engine.ReasonAsync(
            "classify these events",
            new { tag = "winter" },
            TestContext.Current.CancellationToken);

        Assert.Equal("answer", result);
        Assert.NotNull(capturedMessages);
        Assert.Equal(2, capturedMessages!.Count);
        Assert.Equal(ChatRole.System, capturedMessages![0].Role);
        Assert.Contains("classify these events", capturedMessages![0].Text!);
        Assert.Equal(ChatRole.User, capturedMessages![1].Role);
        Assert.Contains("winter", capturedMessages![1].Text!);
    }
}
