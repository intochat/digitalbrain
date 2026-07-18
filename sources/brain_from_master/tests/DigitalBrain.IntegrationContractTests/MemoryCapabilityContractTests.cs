using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DigitalBrain.IntegrationContractTests;

public sealed class MemoryCapabilityContractTests
{
    [Fact]
    public void Memory_handlers_have_stable_ids_versions_and_operation_kinds()
    {
        var memory = new MemoryService(new InMemoryMemoryFactStore(), new NoopAudit());

        var recall = new MemoryRecallCapabilityHandler(memory);
        var remember = new MemoryRememberCapabilityHandler(memory, TimeProvider.System);

        Assert.Equal("memory.recall", recall.CapabilityId);
        Assert.Equal(1, recall.CapabilityVersion);
        Assert.Equal(CapabilityOperationKind.Query, recall.OperationKind);
        Assert.Equal("memory.remember", remember.CapabilityId);
        Assert.Equal(1, remember.CapabilityVersion);
        Assert.Equal(CapabilityOperationKind.InternalWrite, remember.OperationKind);
    }

    [Fact]
    public async Task Capability_payloads_round_trip_through_bounded_handlers_with_owner_isolation()
    {
        var memory = new MemoryService(new InMemoryMemoryFactStore(), new NoopAudit());
        var remember = new MemoryRememberCapabilityHandler(memory, TimeProvider.System);
        var recall = new MemoryRecallCapabilityHandler(memory);
        var owner = new BrainOwnerId("owner-1");
        var actor = new ActorId("feature-1");

        var written = await remember.ExecuteAsync(
            Request(owner, actor, "memory.remember", new
            {
                factId = "fact-1",
                text = "Project Alpha ships Friday",
                tags = new[] { "Project" }
            }),
            Grant(owner, "memory.remember"));
        var recalled = await recall.ExecuteAsync(
            Request(owner, actor, "memory.recall", new
            {
                query = "alpha",
                tags = new[] { "project" },
                limit = 20
            }),
            Grant(owner, "memory.recall"));
        var isolated = await recall.ExecuteAsync(
            Request(new BrainOwnerId("owner-2"), actor, "memory.recall", new
            {
                query = "alpha",
                tags = Array.Empty<string>(),
                limit = 20
            }),
            Grant(new BrainOwnerId("owner-2"), "memory.recall"));

        Assert.Equal("Created", written.GetProperty("status").GetString());
        var fact = Assert.Single(recalled.GetProperty("facts").EnumerateArray());
        Assert.Equal("fact-1", fact.GetProperty("factId").GetString());
        Assert.Empty(isolated.GetProperty("facts").EnumerateArray());
    }

    [Fact]
    public void Azure_Table_wiring_uses_only_the_memoryfacts_table()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:memoryfacts"] = "UseDevelopmentStorage=true"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDigitalBrainMemory(configuration);
        using var provider = services.BuildServiceProvider();

        var store = Assert.IsType<AzureTableMemoryFactStore>(provider.GetRequiredService<IMemoryFactStore>());
        Assert.Equal("memoryfacts", store.TableName);
        Assert.Equal("memoryfacts", provider.GetRequiredService<TableClient>().Name);
        var handlers = provider.GetServices<ICapabilityHandler>().ToArray();
        Assert.Contains(handlers, handler => handler.CapabilityId == "memory.recall");
        Assert.Contains(handlers, handler => handler.CapabilityId == "memory.remember");
    }

    [Fact]
    public void Memory_registration_fails_closed_without_durable_storage()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddDigitalBrainMemory(configuration));

        Assert.Contains("memoryfacts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Volatile_memory_requires_the_explicit_test_mode_opt_in()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:TestMode"] = "true"
            })
            .Build();

        services.AddDigitalBrainMemory(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryMemoryFactStore>(provider.GetRequiredService<IMemoryFactStore>());
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    [Fact]
    public async Task Memory_registration_initializes_the_exact_Azure_table()
    {
        var table = new RecordingTableClient();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDigitalBrainMemory(new ConfigurationBuilder().Build(), table);
        using var provider = services.BuildServiceProvider();

        foreach (var hostedService in provider.GetServices<IHostedService>())
            await hostedService.StartAsync(CancellationToken.None);

        Assert.Equal(1, table.CreateIfNotExistsCalls);
        Assert.Equal("memoryfacts", table.Name);
    }

    [Fact]
    public async Task Azure_replacement_returns_its_committed_etag_without_a_racy_reread()
    {
        var table = new RecordingTableClient();
        var store = new AzureTableMemoryFactStore(table);
        var submitted = new MemoryFactSnapshot(
            "fact-1",
            "after",
            ["new"],
            new ActorId("owner-actor"),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            "\"old\"");

        var replaced = await store.ReplaceAsync(
            new BrainOwnerId("owner-1"),
            submitted,
            "\"old\"",
            CancellationToken.None);

        Assert.Equal("next", replaced.ETag);
        Assert.Equal("\"old\"", table.LastIfMatch.ToString());
        Assert.Equal(0, table.GetEntityCalls);
    }

    [Fact]
    public async Task Azure_transactions_enforce_the_final_slot_retry_and_delete_counter()
    {
        var table = new RecordingTableClient { ConflictsRemaining = 1 };
        var store = new AzureTableMemoryFactStore(table);
        var owner = new BrainOwnerId("owner-1");

        var competing = await Task.WhenAll(
            store.CreateAsync(owner, Fact("fact-1"), 1),
            store.CreateAsync(owner, Fact("fact-2"), 1));

        Assert.Equal(1, competing.Count(status => status == MemoryWriteStatus.Created));
        Assert.Equal(1, competing.Count(status => status == MemoryWriteStatus.CapacityReached));
        var createdId = competing[0] == MemoryWriteStatus.Created ? "fact-1" : "fact-2";
        var created = await store.FindAsync(owner, createdId);
        Assert.NotNull(created);
        Assert.True(await store.DeleteAsync(owner, createdId, created.ETag));
        Assert.Equal(MemoryWriteStatus.Created, await store.CreateAsync(owner, Fact("replacement"), 1));

        Assert.True(table.SubmitTransactionCalls >= 4);
        Assert.Contains(table.Transactions, actions => actions.SequenceEqual(
            [TableTransactionActionType.Add, TableTransactionActionType.Add]));
        Assert.Contains(table.Transactions, actions => actions.SequenceEqual(
            [TableTransactionActionType.UpdateReplace, TableTransactionActionType.Delete]));
        Assert.Contains(table.Transactions, actions => actions.SequenceEqual(
            [TableTransactionActionType.UpdateReplace, TableTransactionActionType.Add]));
        Assert.Equal(MemoryWriteStatus.Created, await store.CreateAsync(
            new BrainOwnerId("owner-2"),
            Fact("replacement"),
            1));
    }

    [Fact]
    public async Task Memory_operations_flow_through_the_shared_dispatcher_with_authority_and_kind()
    {
        var memory = new MemoryService(new InMemoryMemoryFactStore(), new NoopAudit());
        var grants = new RequestGrantSource();
        var dispatcher = new CapabilityDispatcher(
            [
                new MemoryRecallCapabilityHandler(memory),
                new MemoryRememberCapabilityHandler(memory, TimeProvider.System)
            ],
            grants,
            TimeProvider.System);
        var owner = new BrainOwnerId("owner-1");
        var actor = new ActorId("feature-1");

        var remembered = await dispatcher.ExecuteAsync(Request(owner, actor, "memory.remember", new
        {
            factId = "fact-1",
            text = "Project Alpha ships Friday",
            tags = new[] { "project" }
        }));
        var recalled = await dispatcher.ExecuteAsync(Request(owner, actor, "memory.recall", new
        {
            query = "alpha",
            tags = Array.Empty<string>(),
            limit = 20
        }));
        grants.Enabled = false;

        await Assert.ThrowsAsync<CapabilityDeniedException>(() =>
            dispatcher.ExecuteAsync(Request(owner, actor, "memory.recall", new
            {
                query = "alpha",
                tags = Array.Empty<string>(),
                limit = 20
            })));
        Assert.Equal(CapabilityOperationKind.InternalWrite, remembered.Kind);
        Assert.Equal(CapabilityOperationKind.Query, recalled.Kind);
    }

    [Fact]
    public void Memory_has_no_vector_embedding_or_grain_dependency()
    {
        var assembly = typeof(MemoryService).Assembly;
        var names = assembly.GetTypes()
            .Where(type => type.Namespace == "DigitalBrain.Kernel.Memory")
            .Select(type => type.FullName ?? string.Empty)
            .ToArray();
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty);

        Assert.DoesNotContain(names, name => name.Contains("MemoryGrain", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.Contains("Vector", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Embedding", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name.Contains("Qdrant", StringComparison.OrdinalIgnoreCase));
    }

    private static CapabilityRequest Request(
        BrainOwnerId owner,
        ActorId actor,
        string capabilityId,
        object payload) =>
        new(
            owner,
            actor,
            new FeatureInstallationId("installation-1"),
            new ReleaseDigest(new string('a', 64)),
            "input-1",
            "operation-1",
            capabilityId,
            1,
            null,
            new GrantRevision(1),
            JsonSerializer.SerializeToElement(payload),
            DateTimeOffset.UtcNow.AddMinutes(1),
            "correlation-1",
            null);

    private static CapabilityGrant Grant(BrainOwnerId owner, string capabilityId) =>
        new(
            owner,
            new FeatureInstallationId("installation-1"),
            new ReleaseDigest(new string('a', 64)),
            capabilityId,
            1,
            null,
            new GrantRevision(1),
            JsonSerializer.SerializeToElement(new { allowedToolIds = new[] { capabilityId } }),
            true,
            false);

    private static MemoryFactSnapshot Fact(string factId) => new(
        factId,
        $"text-{factId}",
        [],
        new ActorId("feature-1"),
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        string.Empty);

    private sealed class NoopAudit : IMemoryAuditSink
    {
        public ValueTask WriteAsync(MemoryAuditRecord record, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RequestGrantSource : ICapabilityGrantSource
    {
        public bool Enabled { get; set; } = true;

        public ValueTask<CapabilityGrant?> ReadAsync(
            CapabilityRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<CapabilityGrant?>(new CapabilityGrant(
                request.OwnerId,
                request.InstallationId,
                request.ReleaseDigest,
                request.CapabilityId,
                request.CapabilityVersion,
                request.ProviderConnectionId,
                request.GrantRevision,
                JsonSerializer.SerializeToElement(new { allowedToolIds = new[] { request.CapabilityId } }),
                Enabled,
                false));
    }

    private sealed class RecordingTableClient() : TableClient(
        new Uri("https://test.table.core.windows.net"),
        "memoryfacts",
        new TableSharedKeyCredential("test", Convert.ToBase64String(new byte[32])))
    {
        private readonly object _gate = new();
        private readonly Dictionary<(string PartitionKey, string RowKey), ITableEntity> _entities = [];
        private long _version;

        public int CreateIfNotExistsCalls { get; private set; }
        public int GetEntityCalls { get; private set; }
        public int SubmitTransactionCalls { get; private set; }
        public int ConflictsRemaining { get; set; }
        public ETag LastIfMatch { get; private set; }
        public List<IReadOnlyList<TableTransactionActionType>> Transactions { get; } = [];

        public override Task<Response<TableItem>> CreateIfNotExistsAsync(
            CancellationToken cancellationToken = default)
        {
            CreateIfNotExistsCalls++;
            return Task.FromResult(Response.FromValue(new TableItem(Name), new RecordingResponse(201)));
        }

        public override Task<Response> UpdateEntityAsync<T>(
            T entity,
            ETag ifMatch,
            TableUpdateMode mode = TableUpdateMode.Merge,
            CancellationToken cancellationToken = default)
        {
            LastIfMatch = ifMatch;
            return Task.FromResult<Response>(new RecordingResponse(204, "\"next\""));
        }

        public override async Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(
            string partitionKey,
            string rowKey,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            GetEntityCalls++;
            lock (_gate)
            {
                return _entities.TryGetValue((partitionKey, rowKey), out var entity)
                    ? new RecordingNullableResponse<T>((T)entity, new RecordingResponse(200))
                    : new RecordingNullableResponse<T>(new RecordingResponse(404));
            }
        }

        public override async Task<Response<IReadOnlyList<Response>>> SubmitTransactionAsync(
            IEnumerable<TableTransactionAction> transactionActions,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var actions = transactionActions.ToArray();
            lock (_gate)
            {
                SubmitTransactionCalls++;
                if (ConflictsRemaining > 0)
                {
                    ConflictsRemaining--;
                    throw new RequestFailedException(412, "Injected transaction conflict.");
                }
                foreach (var action in actions)
                    Validate(action);
                foreach (var action in actions)
                    Apply(action);
                Transactions.Add(actions.Select(action => action.ActionType).ToArray());
            }
            IReadOnlyList<Response> responses = actions
                .Select(_ => (Response)new RecordingResponse(204))
                .ToArray();
            return Response.FromValue(responses, new RecordingResponse(202));
        }

        private void Validate(TableTransactionAction action)
        {
            var key = (action.Entity.PartitionKey, action.Entity.RowKey);
            var exists = _entities.TryGetValue(key, out var current);
            if (action.ActionType == TableTransactionActionType.Add && exists)
                throw new RequestFailedException(409, "Entity already exists.");
            if (action.ActionType is TableTransactionActionType.UpdateReplace or TableTransactionActionType.Delete)
            {
                if (!exists || current!.ETag != action.ETag)
                    throw new RequestFailedException(412, "Entity changed.");
            }
        }

        private void Apply(TableTransactionAction action)
        {
            var key = (action.Entity.PartitionKey, action.Entity.RowKey);
            if (action.ActionType == TableTransactionActionType.Delete)
            {
                _entities.Remove(key);
                return;
            }
            action.Entity.ETag = new ETag($"v{++_version}");
            _entities[key] = action.Entity;
        }
    }

    private sealed class RecordingNullableResponse<T> : NullableResponse<T>
    {
        private readonly T? _value;
        private readonly Response _response;

        public RecordingNullableResponse(T value, Response response)
        {
            _value = value;
            _response = response;
            HasValue = true;
        }

        public RecordingNullableResponse(Response response)
        {
            _response = response;
        }

        public override bool HasValue { get; }
        public override T Value => HasValue ? _value! : throw new InvalidOperationException();
        public override Response GetRawResponse() => _response;
    }

    private sealed class RecordingResponse(int status, string? etag = null) : Response
    {
        public override int Status => status;
        public override string ReasonPhrase => string.Empty;
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = string.Empty;

        public override void Dispose()
        {
        }

        protected override bool TryGetHeader(string name, out string value)
        {
            if (etag is not null && string.Equals(name, "ETag", StringComparison.OrdinalIgnoreCase))
            {
                value = etag;
                return true;
            }
            value = string.Empty;
            return false;
        }

        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            if (TryGetHeader(name, out var value))
            {
                values = [value];
                return true;
            }
            values = [];
            return false;
        }

        protected override bool ContainsHeader(string name) =>
            etag is not null && string.Equals(name, "ETag", StringComparison.OrdinalIgnoreCase);

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
    }
}
