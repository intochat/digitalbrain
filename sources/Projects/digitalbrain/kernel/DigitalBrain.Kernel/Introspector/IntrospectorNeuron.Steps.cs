using DigitalBrain.SDK.Ai.Contracts.Explaining;
using DigitalBrain.Runtime.Introspector;
using DigitalBrain.Kernel.Conversation;
using DigitalBrain.Kernel.Introspector;
using DigitalBrain.Kernel.User;
using DigitalBrain.Kernel.Visualization;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.InoLang.TestKit;

namespace DigitalBrain.Kernel.Tests.Introspector;

// Plain xUnit fast tests for IntrospectorNeuron using the testable-implementation pattern.
// Each test instantiates TestableIntrospector directly against in-process stubs so no
// silo, grain host, or Aspire cluster is required.

public sealed class IntrospectorNeuronTests
{
    static readonly DateTimeOffset T0 = new(2026, 5, 12, 14, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------------------
    // FindNeuronsByFeatureTextAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FindNeuronsByFeatureTextAsync_returnsMatchingNeuronsWithSnippet()
    {
        var ct = TestContext.Current.CancellationToken;

        var catalog = new StubCatalog();
        catalog.Entries.Add(new NeuronCatalogEntry(
            new("kernel/scheduler"), "sched", NeuronCapability.None,
            "DigitalBrain.Kernel.Scheduler.SchedulerNeuron", [], [],
            "kernel"));
        catalog.Entries.Add(new NeuronCatalogEntry(
            new("kernel/other"), "other", NeuronCapability.None,
            "DigitalBrain.Kernel.Other.OtherNeuron", [], [],
            "kernel"));

        var loader = new StubFeatureLoader();
        loader.Features["DigitalBrain.Kernel.Scheduler.SchedulerNeuron"] =
            ("Feature: schedule a recurring task\n  Scenario: ...", "SchedulerNeuron.feature");
        loader.Features["DigitalBrain.Kernel.Other.OtherNeuron"] =
            ("Feature: something unrelated", "OtherNeuron.feature");

        var introspector = new TestableIntrospector(catalog, loader, null, null, null);

        var result = await introspector.FindNeuronsByFeatureTextAsync("schedule", 10, ct);

        Assert.Single(result);
        Assert.Equal("DigitalBrain.Kernel.Scheduler.SchedulerNeuron", result[0].NeuronType);
        Assert.Equal("kernel", result[0].Domain);
        Assert.NotNull(result[0].FeatureSnippet);
        Assert.Contains("schedule", result[0].FeatureSnippet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindNeuronsByFeatureTextAsync_respectsLimit()
    {
        var ct = TestContext.Current.CancellationToken;

        var catalog = new StubCatalog();
        var loader = new StubFeatureLoader();

        for (var i = 0; i < 5; i++)
        {
            var key = $"DigitalBrain.Kernel.N{i}.N{i}Neuron";
            catalog.Entries.Add(new NeuronCatalogEntry(
                new($"kernel/n{i}"), "icon", NeuronCapability.None, key, [], [], "kernel"));
            loader.Features[key] = ($"Feature: schedule thing {i}", $"N{i}.feature");
        }

        var introspector = new TestableIntrospector(catalog, loader, null, null, null);
        var result = await introspector.FindNeuronsByFeatureTextAsync("schedule", 3, ct);

        Assert.Equal(3, result.Count);
    }

    // ---------------------------------------------------------------------------
    // FindChainsByConversationTextAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FindChainsByConversationTextAsync_returnsDistinctCorrelationIds()
    {
        var ct = TestContext.Current.CancellationToken;

        var cid1 = Guid.NewGuid();
        var cid2 = Guid.NewGuid();

        var conv = new StubConversation();
        conv.Messages.Add(new ChatMessage(Guid.NewGuid(), ChatRole.User, "calendar event today", null, cid1, T0));
        conv.Messages.Add(new ChatMessage(Guid.NewGuid(), ChatRole.User, "add calendar reminder", null, cid1, T0.AddMinutes(1)));
        conv.Messages.Add(new ChatMessage(Guid.NewGuid(), ChatRole.User, "calendar tomorrow", null, cid2, T0.AddMinutes(2)));

        var introspector = new TestableIntrospector(null, null, conv, null, null);
        var result = await introspector.FindChainsByConversationTextAsync("calendar", null, null, 10, ct);

        Assert.Equal(2, result.Count);
        Assert.Contains(cid1, result);
        Assert.Contains(cid2, result);
    }

    // ---------------------------------------------------------------------------
    // TraceCorrelationAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task TraceCorrelationAsync_returnsSynapsesOrderedByTimestamp()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.NewGuid();

        var s1 = MakeSynapse(T0.AddSeconds(2));
        var s2 = MakeSynapse(T0.AddSeconds(1));
        var s3 = MakeSynapse(T0.AddSeconds(3));

        var chain = new StubCorrelationChain([s1, s2, s3]);
        var introspector = new TestableIntrospector(null, null, null, chain, null);

        var result = await introspector.TraceCorrelationAsync(correlationId, ct);

        Assert.Equal(3, result.Count);
        Assert.True(result[0].Timestamp <= result[1].Timestamp);
        Assert.True(result[1].Timestamp <= result[2].Timestamp);
    }

    // ---------------------------------------------------------------------------
    // GetRecentActivityAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetRecentActivityAsync_delegatesToUserNeuron()
    {
        var ct = TestContext.Current.CancellationToken;

        var expected = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var user = new StubUserNeuron(expected);
        var introspector = new TestableIntrospector(null, null, null, null, user);

        var result = await introspector.GetRecentActivityAsync("alice", TimeSpan.FromHours(24), ct);

        Assert.Equal(2, result.Count);
        Assert.Equal(expected[0], result[0]);
        Assert.Equal(expected[1], result[1]);
    }

    // ---------------------------------------------------------------------------
    // FindRootSynapseAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FindRootSynapseAsync_walksToRootViaCausationChain()
    {
        var ct = TestContext.Current.CancellationToken;

        var rootId = Guid.NewGuid();
        var middleId = Guid.NewGuid();
        var leafId = Guid.NewGuid();

        var root   = MakeSynapse(T0,              synapseId: rootId,   causationId: null);
        var middle = MakeSynapse(T0.AddSeconds(1), synapseId: middleId, causationId: rootId);
        var leaf   = MakeSynapse(T0.AddSeconds(2), synapseId: leafId,   causationId: middleId);

        var relay = new StubRelay([root, middle, leaf]);
        var introspector   = new TestableIntrospector(null, null, null, null, null, relay);

        var result = await introspector.FindRootSynapseAsync(leafId, ct);

        Assert.NotNull(result);
        Assert.Equal(rootId, result!.SynapseId);
    }

    // ---------------------------------------------------------------------------
    // ExplainDecisionRequest / ExplainerResponse round-trip
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExplainDecisionRequest_firesExplainerRequest()
    {
        var explainerFired = new List<ExplainerRequest>();
        var introspector = new TestableIntrospector(onFireSynapse: s =>
        {
            if (s is ExplainerRequest er) explainerFired.Add(er);
        });

        var correlationId = Guid.NewGuid();
        var req = new ExplainDecisionRequest(NaturalLanguageQuery: "why did you do X",
        UserId:             "default") { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: correlationId,
            causationId: null,
            callerNeuronId: Guid.Empty,
            callerNeuronType: "External",
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "IntrospectorNeuron",
            timestamp: TimeProvider.System.GetUtcNow()
        ) };

        await introspector.HandleExplainRequestAsync(req);

        Assert.Single(explainerFired);
        Assert.Equal("why did you do X", explainerFired[0].NaturalLanguageQuery);
        Assert.Equal(correlationId, explainerFired[0].CorrelationId);
    }

    [Fact]
    public async Task ExplainerResponse_firesExplainDecisionResponse_andRemovesOutstanding()
    {
        var responseFired = new List<ExplainDecisionResponse>();
        var introspector = new TestableIntrospector(onFireSynapse: s =>
        {
            if (s is ExplainDecisionResponse r) responseFired.Add(r);
        });

        var correlationId = Guid.NewGuid();
        var originalCallerId = Guid.NewGuid();

        // Prime the outstanding entry as if an ExplainDecisionRequest was handled first.
        await introspector.HandleExplainRequestAsync(new ExplainDecisionRequest(NaturalLanguageQuery: "why did you do X",
        UserId:             "default") { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: correlationId,
            causationId: null,
            callerNeuronId: originalCallerId,
            callerNeuronType: "GatewayNeuron",
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "IntrospectorNeuron",
            timestamp: TimeProvider.System.GetUtcNow()
        ) });

        var citedIds = new[] { Guid.NewGuid() };
        await introspector.HandleExplainerResponseAsync(new ExplainerResponse(NaturalLanguageAnswer: "Because of reason X.",
        CitedCorrelationIds:   citedIds,
        ToolCallTrace:         []) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: correlationId,
            causationId: null,
            callerNeuronId: Guid.Empty,
            callerNeuronType: "ExplainerNeuron",
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "IntrospectorNeuron",
            timestamp: TimeProvider.System.GetUtcNow()
        ) });

        Assert.Single(responseFired);
        Assert.Equal("Because of reason X.", responseFired[0].NaturalLanguageAnswer);
        Assert.Equal(correlationId, responseFired[0].CorrelationId);
        Assert.Equal(citedIds, responseFired[0].CitedCorrelationIds);
        Assert.Equal(originalCallerId, responseFired[0].ReceiverNeuronId);
        Assert.Empty(introspector.OutstandingExplain);
    }

    [Fact]
    public async Task QueryCatalogContractsRequest_returnsQueryCatalogContractsResponseWithSchemas()
    {
        var responseFired = new List<QueryCatalogContractsResponse>();
        var introspector = new TestableIntrospector(onFireSynapse: s =>
        {
            if (s is QueryCatalogContractsResponse r) responseFired.Add(r);
        });

        // Set up test schemas in a mock/stub catalog
        var mockCatalog = new FakeCatalog()
            .With("Test.SynapseContract", ContractKind.Synapse, "field1", "field2")
            .With("Test.NeuronContract", ContractKind.Neuron, "fieldA");

        var req = new QueryCatalogContractsRequest() { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.NewGuid(),
            causationId: null,
            callerNeuronId: Guid.Empty,
            callerNeuronType: "GatewayNeuron",
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "IntrospectorNeuron",
            timestamp: TimeProvider.System.GetUtcNow()
        ) };

        await introspector.HandleQueryCatalogContractsRequestAsync(req, mockCatalog);

        Assert.Single(responseFired);
        var resp = responseFired[0];
        Assert.Equal(req.CorrelationId, resp.CorrelationId);
        Assert.Equal(2, resp.Schemas.Count);

        var synapseSchema = resp.Schemas.First(s => s.Fqn == "Test.SynapseContract");
        Assert.Equal(CatalogContractKind.Synapse, synapseSchema.Kind);
        Assert.Equal(new[] { "field1", "field2" }, synapseSchema.Fields.ToArray());

        var neuronSchema = resp.Schemas.First(s => s.Fqn == "Test.NeuronContract");
        Assert.Equal(CatalogContractKind.Neuron, neuronSchema.Kind);
        Assert.Equal(new[] { "fieldA" }, neuronSchema.Fields.ToArray());
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    static Synapse MakeSynapse(
        DateTimeOffset at,
        Guid? synapseId = null,
        Guid? causationId = null)
        => new TestSynapse() { Headers = SynapseMetadata.Create(
            synapseId: synapseId ?? Guid.NewGuid(),
            correlationId: Guid.NewGuid(),
            causationId: causationId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: "Test",
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "Test",
            timestamp: at
        ) };

    // Not annotated with [Orleans.GenerateSerializer] — test-only, never crosses the wire.
    sealed record TestSynapse : Synapse;

    // ---------------------------------------------------------------------------
    // Stubs
    // ---------------------------------------------------------------------------

    sealed class StubCatalog : IBrainCatalog
    {
        public List<NeuronCatalogEntry> Entries { get; } = [];

        public Task RegisterAsync(NeuronCatalogEntry entry) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<NeuronCatalogEntry>> ListRegisteredAsync() => Task.FromResult<IReadOnlyList<NeuronCatalogEntry>>(Entries);
        public Task<IReadOnlyList<CatalogedNeuron>> ListNeuronsAsync() => Task.FromResult<IReadOnlyList<CatalogedNeuron>>([]);
        public Task<IReadOnlyList<Synapse>> SnapshotAsync(DateTimeOffset since) => Task.FromResult<IReadOnlyList<Synapse>>([]);
        public Task<SynapseSlice> WatchSinceAsync(long cursor) => Task.FromResult(new SynapseSlice(0, []));
    }

    sealed class StubFeatureLoader : INeuronFeatureLoader
    {
        public Dictionary<string, (string Text, string SourceFile)> Features { get; } = new(StringComparer.Ordinal);

        public (string Text, string SourceFile)? GetFeature(string neuronTypeFullName)
            => Features.TryGetValue(neuronTypeFullName, out var v) ? v : null;
    }

    sealed class StubConversation : IConversation
    {
        public List<ChatMessage> Messages { get; } = [];

        public Task AppendUserMessageAsync(Guid id, string text, Guid correlationId, CancellationToken ct)
        {
            Messages.Add(new ChatMessage(id, ChatRole.User, text, null, correlationId, DateTimeOffset.MinValue));
            return Task.CompletedTask;
        }

        public Task AppendAssistantMessageAsync(Guid id, string? text, string? rfwEnvelopeJson, Guid correlationId, CancellationToken ct)
        {
            Messages.Add(new ChatMessage(id, ChatRole.Assistant, text ?? "", rfwEnvelopeJson, correlationId, DateTimeOffset.MinValue));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ChatMessage>> RecentAsync(int count, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.TakeLast(count).ToList());

        public Task<IReadOnlyList<ChatMessage>> SinceAsync(DateTimeOffset since, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(m => m.Timestamp >= since).ToList());

        public Task<IReadOnlyList<ChatMessage>> SearchAsync(string query, DateTimeOffset? since, DateTimeOffset? until, int limit, CancellationToken ct)
        {
            var matches = Messages
                .Where(m => m.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Where(m => since == null || m.Timestamp >= since)
                .Where(m => until == null || m.Timestamp <= until)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<ChatMessage>>(matches);
        }
    }

    sealed class StubCorrelationChain(IReadOnlyList<Synapse> synapses) : ICorrelationChain
    {
        public Task AppendAsync(Synapse synapse, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<Synapse>> SnapshotAsync(CancellationToken ct) => Task.FromResult(synapses);
        public Task<int> CountAsync(CancellationToken ct) => Task.FromResult(synapses.Count);
    }

    sealed class StubUserNeuron(IReadOnlyList<Guid> correlationIds) : IUserNeuron
    {
        public Task SubmitPromptAsync(string text, Guid correlationId, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<Guid>> GetRecentCorrelationIdsAsync(TimeSpan since, CancellationToken ct)
            => Task.FromResult(correlationIds);

        public Task<IReadOnlyList<Synapse>> GetIncomingJournalAsync(int fromIndex = 0, int toIndex = int.MaxValue) => Task.FromResult<IReadOnlyList<Synapse>>([]);
        public Task<IReadOnlyList<Synapse>> GetOutgoingJournalAsync(int fromIndex = 0, int toIndex = int.MaxValue) => Task.FromResult<IReadOnlyList<Synapse>>([]);
        public Task<int> GetIncomingCountAsync() => Task.FromResult(0);
        public Task<int> GetOutgoingCountAsync() => Task.FromResult(0);
    }

    sealed class StubRelay(IReadOnlyList<Synapse> buffer) : IBrainTimelineRelay
    {
        public Task<SynapseSlice> WatchSinceAsync(long cursor) => Task.FromResult(new SynapseSlice(0, []));
        public Task<IReadOnlyList<Synapse>> SnapshotAsync(DateTimeOffset since) => Task.FromResult(buffer);
        public Task<IReadOnlyList<CatalogedNeuron>> ListSeenAsync() => Task.FromResult<IReadOnlyList<CatalogedNeuron>>([]);
    }

    // TestableIntrospector implements IIntrospector's query methods directly against stubs,
    // and exposes HandleExplainRequestAsync / HandleExplainerResponseAsync so the
    // explain round-trip logic can be tested without a DurableGrain silo.
    sealed class TestableIntrospector : IIntrospector
    {
        readonly StubCatalog?          catalog;
        readonly StubFeatureLoader?    featureLoader;
        readonly StubConversation?     conversation;
        readonly StubCorrelationChain? chain;
        readonly StubUserNeuron?       userNeuron;
        readonly StubRelay?            relay;
        readonly Action<Synapse>?      onFireSynapse;
        readonly List<OutstandingExplain> outstanding = [];

        public IReadOnlyList<OutstandingExplain> OutstandingExplain => outstanding;

        // Constructor used by query tests (Task 10).
        public TestableIntrospector(
            StubCatalog? catalog,
            StubFeatureLoader? featureLoader,
            StubConversation? conversation,
            StubCorrelationChain? chain,
            StubUserNeuron? userNeuron,
            StubRelay? relay = null)
        {
            this.catalog       = catalog;
            this.featureLoader = featureLoader;
            this.conversation  = conversation;
            this.chain         = chain;
            this.userNeuron    = userNeuron;
            this.relay         = relay;
        }

        // Constructor used by explain round-trip tests (Task 26).
        public TestableIntrospector(Action<Synapse> onFireSynapse)
        {
            this.onFireSynapse = onFireSynapse;
        }

        // Mirrors IntrospectorNeuron.HandleSynapseAsync case ExplainDecisionRequest.
        public async Task HandleExplainRequestAsync(ExplainDecisionRequest req)
        {
            outstanding.Add(new OutstandingExplain(
                CorrelationId:            req.CorrelationId,
                OriginalCallerNeuronId:   req.CallerNeuronId,
                OriginalCallerNeuronType: req.CallerNeuronType,
                OriginalRequestSynapseId: req.SynapseId
            ));

            onFireSynapse?.Invoke(new ExplainerRequest(NaturalLanguageQuery: req.NaturalLanguageQuery,
        UserId:               req.UserId) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: nameof(IntrospectorNeuron),
            receiverNeuronId: Guid.NewGuid(),
            receiverNeuronType: "ExplainerNeuron",
            timestamp: TimeProvider.System.GetUtcNow()
        ) });

            await Task.CompletedTask;
        }

        // Mirrors IntrospectorNeuron.HandleSynapseAsync case ExplainerResponse.
        public async Task HandleExplainerResponseAsync(ExplainerResponse exp)
        {
            var idx = outstanding.FindIndex(o => o.CorrelationId == exp.CorrelationId);
            if (idx < 0) return;
            var open = outstanding[idx];

            onFireSynapse?.Invoke(new ExplainDecisionResponse(NaturalLanguageAnswer: exp.NaturalLanguageAnswer,
        CitedCorrelationIds:   exp.CitedCorrelationIds) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: exp.CorrelationId,
            causationId: exp.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: nameof(IntrospectorNeuron),
            receiverNeuronId: open.OriginalCallerNeuronId,
            receiverNeuronType: open.OriginalCallerNeuronType ?? "External",
            timestamp: TimeProvider.System.GetUtcNow()
        ) });

            outstanding.RemoveAt(idx);
            await Task.CompletedTask;
        }

        // Mirrors IntrospectorNeuron.HandleSynapseAsync case QueryCatalogContractsRequest.
        public async Task HandleQueryCatalogContractsRequestAsync(QueryCatalogContractsRequest req, IContractCatalog catalog)
        {
            var internalSchemas = catalog.GetAllSchemas();
            var catalogSchemas = internalSchemas.Select(s => new CatalogContractSchema(
                s.Fqn,
                s.Kind switch
                {
                    ContractKind.Synapse => CatalogContractKind.Synapse,
                    _                    => CatalogContractKind.Neuron
                },
                s.Fields)).ToArray();

            onFireSynapse?.Invoke(new QueryCatalogContractsResponse(Schemas:            catalogSchemas) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: nameof(IntrospectorNeuron),
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "External",
            timestamp: TimeProvider.System.GetUtcNow()
        ) });

            await Task.CompletedTask;
        }

        public async Task<IReadOnlyList<NeuronRef>> FindNeuronsByFeatureTextAsync(
            string query, int limit, CancellationToken ct)
        {
            var registered = await catalog!.ListRegisteredAsync();
            var results = new List<NeuronRef>();

            foreach (var entry in registered)
            {
                if (results.Count >= limit) break;
                var feature = featureLoader!.GetFeature(entry.TypeFullName);
                if (feature is null) continue;
                if (!feature.Value.Text.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;

                var text = feature.Value.Text;
                var idx = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                var snippetStart = Math.Max(0, idx - 40);
                var snippetEnd = Math.Min(text.Length, idx + query.Length + 80);
                var snippet = text[snippetStart..snippetEnd].Trim();

                results.Add(new NeuronRef(entry.TypeFullName, entry.Domain, snippet));
            }

            return results;
        }

        public async Task<IReadOnlyList<Guid>> FindChainsByConversationTextAsync(
            string text, DateTimeOffset? since, DateTimeOffset? until, int limit, CancellationToken ct)
        {
            var hits = await conversation!.SearchAsync(text, since, until, limit, ct);
            return hits.Select(m => m.CorrelationId).Distinct().ToArray();
        }

        public async Task<IReadOnlyList<Synapse>> TraceCorrelationAsync(Guid correlationId, CancellationToken ct)
        {
            var snapshot = await chain!.SnapshotAsync(ct);
            return snapshot.OrderBy(s => s.Timestamp).ToArray();
        }

        public Task<IReadOnlyList<Guid>> GetRecentActivityAsync(string userId, TimeSpan since, CancellationToken ct)
            => userNeuron!.GetRecentCorrelationIdsAsync(since, ct);

        public async Task<Synapse?> FindRootSynapseAsync(Guid synapseId, CancellationToken ct)
        {
            var recent = await relay!.SnapshotAsync(default);
            var byId = recent.ToDictionary(s => s.SynapseId);

            if (!byId.TryGetValue(synapseId, out var current)) return null;

            while (current.CausationId is { } parentId && byId.TryGetValue(parentId, out var parent))
                current = parent;

            return current;
        }

        public Task<IReadOnlyList<Synapse>> GetIncomingJournalAsync(int fromIndex = 0, int toIndex = int.MaxValue) => Task.FromResult<IReadOnlyList<Synapse>>([]);
        public Task<IReadOnlyList<Synapse>> GetOutgoingJournalAsync(int fromIndex = 0, int toIndex = int.MaxValue) => Task.FromResult<IReadOnlyList<Synapse>>([]);
        public Task<int> GetIncomingCountAsync() => Task.FromResult(0);
        public Task<int> GetOutgoingCountAsync() => Task.FromResult(0);
    }

    private sealed class FakeCatalog : IContractCatalog
    {
        private readonly Dictionary<string, ContractSchema> _schemas = new(StringComparer.Ordinal);

        public ContractSchema? Resolve(string fqn) => _schemas.GetValueOrDefault(fqn);

        public IReadOnlyCollection<ContractSchema> GetAllSchemas() => _schemas.Values;

        public void Register(ContractSchema schema) => _schemas[schema.Fqn] = schema;

        public FakeCatalog With(string fqn, ContractKind kind, params string[] fields)
        {
            _schemas[fqn] = new ContractSchema(fqn, kind, fields);
            return this;
        }
    }
}
