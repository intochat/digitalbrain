using System.Collections.Concurrent;
using DigitalBrain.Product.Memory;
using DigitalBrain.Testing;

namespace DigitalBrain.Product.Tests.Memory;

public sealed class MemoryContractTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(MemoryStoreRequested).Assembly)
            .RegisterIngress<MemoryStoreRequested>()
            .RegisterIngress<MemorySearchRequested>()
            .RegisterIngress<MemoryRemoveRequested>()
            .RegisterWorkspaceService<IMemoryStore>(workspace => Stores.For(workspace.Id))
            .RegisterNeuron<MemoryNeuron>(MemoryNeuron.Kind);

    [Fact]
    public async Task StoresAndSearchesImmutableEntriesUsingEveryMetadataFilter()
    {
        const string scope = "workspace/memory-filter";
        const string source = "enrichment/acme";
        Stores.Reset(scope);
        var workspace = OpenMemoryWorkspace(scope, source);
        var sourceMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["account"] = "acme",
            ["source"] = "gmail",
        };
        var immutableEntry = new MemoryEntry("memory-acme-gmail", "Acme announced funding.", sourceMetadata);
        sourceMetadata["account"] = "changed-after-request";

        await PublishAsync(workspace, new MemoryStoreRequested(immutableEntry));
        await PublishAsync(workspace, new MemoryStoreRequested(new MemoryEntry(
            "memory-acme-web",
            "Acme funding coverage from the web.",
            new Dictionary<string, string> { ["account"] = "acme", ["source"] = "web" })));
        await PublishAsync(workspace, new MemoryStoreRequested(new MemoryEntry(
            "memory-other-gmail",
            "Another company announced funding.",
            new Dictionary<string, string> { ["account"] = "other", ["source"] = "gmail" })));
        await PublishAsync(
            workspace,
            new MemorySearchRequested(new MemoryQuery(
                "funding",
                5,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["account"] = "acme",
                    ["source"] = "gmail",
                })));

        var result = await WaitForSearchResultAsync(workspace, source, "the filtered memory search result");
        var hits = result.Serialization.GetProperty("hits").EnumerateArray().ToArray();

        var hit = Assert.Single(hits);
        Assert.Equal("memory-acme-gmail", hit.GetProperty("entry").GetProperty("id").GetString());
        Assert.Equal("acme", hit.GetProperty("entry").GetProperty("metadata").GetProperty("account").GetString());
        Assert.Equal("gmail", hit.GetProperty("entry").GetProperty("metadata").GetProperty("source").GetString());
    }

    [Fact]
    public async Task RemovesAnEntryIdempotentlyWithoutReportingAnAvailabilityFailure()
    {
        const string scope = "workspace/memory-remove";
        const string source = "enrichment/acme";
        Stores.Reset(scope);
        var workspace = OpenMemoryWorkspace(scope, source);

        await PublishAsync(
            workspace,
            new MemoryStoreRequested(new MemoryEntry(
                "memory-remove",
                "An obsolete fact.",
                new Dictionary<string, string> { ["account"] = "acme" })));
        await PublishAsync(workspace, new MemoryRemoveRequested("memory-remove"));
        await PublishAsync(workspace, new MemoryRemoveRequested("memory-remove"));
        await PublishAsync(workspace, new MemorySearchRequested(new MemoryQuery("obsolete", 5)));

        var result = await WaitForSearchResultAsync(workspace, source, "an empty search after repeated removal");
        Assert.Empty(result.Serialization.GetProperty("hits").EnumerateArray());
        var page = await ReadAsync(workspace, new NeuronId(MemoryNeuron.Kind, source), cancellationToken: Cancellation);
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(MemoryUnavailable).FullName);
    }

    [Fact]
    public async Task ReportsProviderFailureAsMemoryUnavailableWithoutLeakingProviderDetails()
    {
        const string scope = "workspace/memory-unavailable";
        const string source = "enrichment/acme";
        Stores.Reset(scope);
        var workspace = OpenMemoryWorkspace(scope, source);
        Stores.For(scope).FailSearch = true;

        await PublishAsync(workspace, new MemorySearchRequested(new MemoryQuery("funding", 5)));

        var page = await WaitForJournalAsync(
            workspace,
            new NeuronId(MemoryNeuron.Kind, source),
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(MemoryUnavailable).FullName),
            "a memory-unavailable outcome",
            Cancellation);
        var unavailable = page.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(MemoryUnavailable).FullName);
        Assert.Equal("search", unavailable.Serialization.GetProperty("operation").GetString());
        Assert.Equal("Memory is temporarily unavailable.", unavailable.Serialization.GetProperty("message").GetString());
        Assert.DoesNotContain("provider-secret", unavailable.Serialization.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BindsMemoryToThePhysicalWorkspaceWithoutPuttingScopeInFacts()
    {
        const string source = "enrichment/acme";
        var left = OpenMemoryWorkspace("workspace/memory-left", source);
        var right = OpenMemoryWorkspace("workspace/memory-right", source);
        Stores.Reset("workspace/memory-left");
        Stores.Reset("workspace/memory-right");

        await PublishAsync(
            left,
            new MemoryStoreRequested(new MemoryEntry(
                "memory-shared-id",
                "Acme funding evidence.",
                new Dictionary<string, string> { ["account"] = "acme" })));
        await PublishAsync(right, new MemorySearchRequested(new MemoryQuery("funding", 5)));

        var rightResult = await WaitForSearchResultAsync(right, source, "the isolated right-workspace search");
        Assert.Empty(rightResult.Serialization.GetProperty("hits").EnumerateArray());
        Assert.DoesNotContain("workspace", rightResult.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplayConvergesOnOneLogicalEntryByTheImmutableMemoryId()
    {
        const string scope = "testing/default";
        const string source = "enrichment/memory-replay";
        Stores.Reset(scope);
        var workspace = OpenMemoryWorkspace(scope, source);
        var memory = new NeuronId(MemoryNeuron.Kind, source);
        var sourceNeuron = new NeuronId("digitalbrain.synapse-source", source);
        var fault = FailNextJournalRecording(memory);

        await PublishAsync(
            workspace,
            new MemoryStoreRequested(new MemoryEntry(
                "memory-replay",
                "A durable upsert must converge.",
                new Dictionary<string, string> { ["account"] = "acme" })));
        await fault.Consumed.WaitAsync(Cancellation);
        await DeactivateAsync([memory], Cancellation);
        await DrainAsync(sourceNeuron, Cancellation);
        await PublishAsync(workspace, new MemorySearchRequested(new MemoryQuery("durable", 5)));

        var result = await WaitForSearchResultAsync(workspace, source, "the replayed memory search result");
        Assert.Single(result.Serialization.GetProperty("hits").EnumerateArray());
        Assert.Equal(1, Stores.For(scope).EntryCount);
    }

    private WorkspaceChannel OpenMemoryWorkspace(string scope, string source)
        => OpenWorkspace(
            scope,
            source,
            typeof(MemoryStoreRequested),
            typeof(MemorySearchRequested),
            typeof(MemoryRemoveRequested));

    private static Task PublishAsync(WorkspaceChannel workspace, Synapse synapse)
        => workspace.Publisher.PublishAsync(synapse, Cancellation);

    private static async Task<JournalRecord> WaitForSearchResultAsync(
        WorkspaceChannel workspace,
        string source,
        string expectation)
    {
        var page = await WaitForJournalAsync(
            workspace,
            new NeuronId(MemoryNeuron.Kind, source),
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(MemorySearchCompleted).FullName),
            expectation,
            Cancellation);
        return page.Records.Last(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(MemorySearchCompleted).FullName);
    }

    private static class Stores
    {
        private static readonly ConcurrentDictionary<string, ControlledMemoryStore> ByWorkspace = new(StringComparer.Ordinal);

        internal static ControlledMemoryStore For(string workspace)
            => ByWorkspace.GetOrAdd(workspace, static _ => new ControlledMemoryStore());

        internal static void Reset(string workspace) => For(workspace).Reset();
    }

    private sealed class ControlledMemoryStore : IMemoryStore
    {
        private readonly ConcurrentDictionary<string, MemoryEntry> entries = new(StringComparer.Ordinal);

        internal bool FailSearch { get; set; }

        internal int EntryCount => entries.Count;

        internal void Reset()
        {
            entries.Clear();
            FailSearch = false;
        }

        public Task<MemoryStoreResult> StoreAsync(MemoryEntry entry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries[entry.Id] = entry;
            return Task.FromResult(new MemoryStoreResult(entry.Id));
        }

        public Task<IReadOnlyList<MemoryHit>> SearchAsync(MemoryQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailSearch)
            {
                throw new InvalidOperationException("provider-secret: simulated provider failure");
            }

            MemoryHit[] hits = [.. entries.Values
                .Where(entry => entry.Content.Contains(query.Text, StringComparison.OrdinalIgnoreCase))
                .Where(entry => query.Metadata.All(filter => entry.Metadata.TryGetValue(filter.Key, out var value)
                    && string.Equals(value, filter.Value, StringComparison.Ordinal)))
                .Take(query.MaximumResults)
                .Select(static entry => new MemoryHit(entry, 1d))];
            return Task.FromResult<IReadOnlyList<MemoryHit>>(hits);
        }

        public Task RemoveAsync(string entryId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = entries.TryRemove(entryId, out _);
            return Task.CompletedTask;
        }
    }
}
