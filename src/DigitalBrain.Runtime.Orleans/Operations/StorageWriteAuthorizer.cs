using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Capabilities.Storage;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Runtime.Orleans.Operations;

// Cluster infrastructure, deliberately not a neuron contract: HTTP, MCP and sandbox dispatch cannot name this interface.
internal interface IStorageWriteGrants : IGrainWithStringKey
{
    Task<string> Issue(string connection, string requester, string fingerprint);
    Task<bool> Consume(string token, string connection, string requester, string fingerprint);
    Task Revoke(string token);
}

[GrainType("runtime.storage-write-grants")]
internal sealed class StorageWriteGrantGrain(TimeProvider time) : Grain, IStorageWriteGrants
{
    private sealed record Grant(string Connection, string Requester, string Fingerprint, DateTimeOffset Expires);
    private readonly Dictionary<string, Grant> _grants = new(StringComparer.Ordinal);

    public Task<string> Issue(string connection, string requester, string fingerprint)
    {
        var now = time.GetUtcNow();
        foreach (var token in _grants.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray()) { _grants.Remove(token); }
        if (_grants.Count >= 4096) { throw new InvalidOperationException("Too many outstanding storage write grants."); }
        var value = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        _grants.Add(value, new(connection, requester, fingerprint, now + TimeSpan.FromMinutes(1)));
        return Task.FromResult(value);
    }

    public Task<bool> Consume(string token, string connection, string requester, string fingerprint)
        => Task.FromResult(_grants.Remove(token, out var grant) && grant.Expires > time.GetUtcNow()
            && grant.Connection == connection && grant.Requester == requester && grant.Fingerprint == fingerprint);

    public Task Revoke(string token) { _grants.Remove(token); return Task.CompletedTask; }
}

/// <summary>Host-only, ephemeral authorizations for storage primitives executed by an approved runtime runner.</summary>
public sealed partial class StorageWriteAuthorizer(IGrainFactory grains, IStorageWriteIdentity identity, ILogger<StorageWriteAuthorizer> logger) : IStorageWriteAuthorizer
{
    private const string PermitKey = "db.storage-write-grant";
    private readonly AsyncLocal<Execution?> _current = new();

    /// <inheritdoc />
    public IDisposable BeginApproved(BrainId brain, string requester)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requester);
        var previous = _current.Value;
        var execution = new Execution(brain, identity.Current(brain));
        _current.Value = execution;
        return new Scope(() => { execution.Close(); _current.Value = previous; });
    }

    /// <inheritdoc />
    public async Task<T> Invoke<T>(string connection, WritePlan primitive, Func<Task<T>> send, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        ArgumentNullException.ThrowIfNull(send);
        var execution = _current.Value;
        if (execution is null || execution.Closed || execution.Identity is null) { return await send().ConfigureAwait(false); }
        if (!connection.StartsWith(execution.Brain.Value + "/", StringComparison.Ordinal)
            || identity.Current(execution.Brain) != execution.Identity)
        { throw new UnauthorizedAccessException("The approved storage execution belongs to another caller or brain."); }
        ct.ThrowIfCancellationRequested();
        var grants = grains.GetGrain<IStorageWriteGrants>(execution.Brain.Value);
        var token = await grants.Issue(connection, execution.Identity, Fingerprint(primitive)).WaitAsync(ct).ConfigureAwait(false);
        var previous = RequestContext.Get(PermitKey);
        try
        {
            return await SendAndRevoke(async () =>
            {
                if (execution.Closed) { throw new UnauthorizedAccessException("The approved storage execution has ended."); }
                RequestContext.Set(PermitKey, token);
                return await send().ConfigureAwait(false);
            }, () => grants.Revoke(token), logger).ConfigureAwait(false);
        }
        finally
        {
            if (previous is null) { RequestContext.Remove(PermitKey); }
            else { RequestContext.Set(PermitKey, previous); }
        }
    }

    internal static async Task<T> SendAndRevoke<T>(Func<Task<T>> send, Func<Task> revoke, ILogger logger)
    {
        try { return await send().ConfigureAwait(false); }
        finally
        {
            try { await revoke().ConfigureAwait(false); }
            catch (TimeoutException) { CleanupFailed(logger, nameof(TimeoutException)); }
            catch (SiloUnavailableException) { CleanupFailed(logger, nameof(SiloUnavailableException)); }
        }
    }

    [LoggerMessage(EventId = 7131, Level = LogLevel.Warning, Message = "Storage write grant cleanup failed ({Failure}); unused grants expire after one minute.")]
    private static partial void CleanupFailed(ILogger logger, string failure);

    /// <inheritdoc />
    public async Task<bool> Consume(NeuronId storageNeuron, WritePlan primitive, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        if (RequestContext.Get(PermitKey) is not string token) { return false; }
        RequestContext.Remove(PermitKey);
        if (token.Length != 64 || identity.Current(storageNeuron.Brain) is not { } requester) { return false; }
        var connection = storageNeuron.Brain.Value + "/" + storageNeuron.Key;
        return await grains.GetGrain<IStorageWriteGrants>(storageNeuron.Brain.Value)
            .Consume(token, connection, requester, Fingerprint(primitive)).WaitAsync(ct).ConfigureAwait(false);
    }

    internal static string Fingerprint(WritePlan plan)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            plan.Kind,
            Source = plan.Source?.Value,
            TargetFolder = plan.TargetFolder?.Value,
            plan.Name,
            plan.Conflict,
            plan.Permanent,
            plan.DryRun,
            InlineLength = plan.Inline?.Length,
            InlineHash = plan.Inline is { } bytes ? Convert.ToHexStringLower(SHA256.HashData(bytes.Span)) : null,
        });
        return Convert.ToHexStringLower(SHA256.HashData(json));
    }

    private sealed class Execution(BrainId brain, string? identity)
    {
        private int _closed;
        public BrainId Brain { get; } = brain;
        public string? Identity { get; } = identity;
        public bool Closed => Volatile.Read(ref _closed) != 0;
        public void Close() => Interlocked.Exchange(ref _closed, 1);
    }

    private sealed class Scope(Action close) : IDisposable
    {
        private Action? _close = close;
        public void Dispose() => Interlocked.Exchange(ref _close, null)?.Invoke();
    }
}
