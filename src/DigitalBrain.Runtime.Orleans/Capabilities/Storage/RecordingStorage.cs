using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;

namespace DigitalBrain.Runtime.Orleans.Capabilities.Storage;

/// <summary>storage@1 decorator. Every member is recorded once; a stream records open, then commit or abort.</summary>
internal sealed class RecordingStorage : IStorage
{
    private readonly IStorage _inner;
    private readonly StorageAccessLog _log;
    private readonly string _app;
    private readonly string _brain;
    private readonly string _moduleKind;
    private readonly string _connectionKey;

    internal RecordingStorage(IStorage inner, StorageAccessLog log, string app, string brain, string moduleKind, string connectionKey)
    {
        _inner = inner;
        _log = log;
        _app = app;
        _brain = brain;
        _moduleKind = moduleKind;
        _connectionKey = connectionKey;
    }

    internal IStorage Inner => _inner;

    public Task<StorageDescriptor> Describe(CancellationToken ct = default)
        => Call(null, "Describe", () => _inner.Describe(ct));

    public Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default)
        => Call(null, "Roots", () => _inner.Roots(ct));

    public Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default)
        => Call(folder, "List", () => _inner.List(folder, query, ct));

    public Task<Item> Stat(ItemId id, CancellationToken ct = default)
        => Call(id, "Stat", () => _inner.Stat(id, ct));

    public Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default)
        => Call(id, "Read", () => _inner.Read(id, range, ct));

    public Task<OperationId> Write(WritePlan plan, CancellationToken ct = default)
        => Call(plan.Source ?? plan.TargetFolder, "Write", () => _inner.Write(plan, ct));

    public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default)
        => Call(query.Within, "Search", () => _inner.Search(query, ct));

    public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default)
        => Call(id, "Ancestors", () => _inner.Ancestors(id, ct));

    public async Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default)
    {
        try
        {
            var stream = await _inner.OpenRead(item, range, ct).ConfigureAwait(false);
            StorageAccessLog.Stamp(_log, _app, _brain, _moduleKind, _connectionKey, item, "OpenRead", "Ok");
            return stream;
        }
        catch (Exception error)
        {
            StorageAccessLog.Stamp(_log, _app, _brain, _moduleKind, _connectionKey, item, "OpenRead", StorageAccessLog.OutcomeOf(error));
            throw;
        }
    }

    public async Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default)
    {
        try
        {
            var stream = await _inner.OpenWrite(plan, ct).ConfigureAwait(false);
            StorageAccessLog.Stamp(_log, _app, _brain, _moduleKind, _connectionKey, plan.TargetFolder, "OpenWrite", "Ok");
            return new RecordingWrite(stream, outcome => StorageAccessLog.Stamp(_log, _app, _brain, _moduleKind, _connectionKey, plan.TargetFolder, "OpenWrite", outcome));
        }
        catch (Exception error)
        {
            StorageAccessLog.Stamp(_log, _app, _brain, _moduleKind, _connectionKey, plan.TargetFolder, "OpenWrite", StorageAccessLog.OutcomeOf(error));
            throw;
        }
    }

    private async Task<T> Call<T>(ItemId? item, string op, Func<Task<T>> action)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            StorageAccessLog.Stamp(_log, _app, _brain, _moduleKind, _connectionKey, item, op, "Ok");
            return result;
        }
        catch (Exception error)
        {
            StorageAccessLog.Stamp(_log, _app, _brain, _moduleKind, _connectionKey, item, op, StorageAccessLog.OutcomeOf(error));
            throw;
        }
    }

    private sealed class RecordingWrite : StorageWriteStream
    {
        private readonly StorageWriteStream _inner;
        private readonly Action<string> _record;

        public RecordingWrite(StorageWriteStream inner, Action<string> record) : base(inner.Content)
        {
            _inner = inner;
            _record = record;
        }

        protected override async Task<ItemId> CommitCore(CancellationToken ct)
        {
            try
            {
                var id = await _inner.Commit(ct).ConfigureAwait(false);
                _record("Commit");
                return id;
            }
            catch (Exception error)
            {
                _record(StorageAccessLog.OutcomeOf(error));
                throw;
            }
        }

        protected override async Task AbortCore(CancellationToken ct)
        {
            try
            {
                await _inner.Abort(ct).ConfigureAwait(false);
                _record("Abort");
            }
            catch (Exception error)
            {
                _record(StorageAccessLog.OutcomeOf(error));
                throw;
            }
        }
    }
}
