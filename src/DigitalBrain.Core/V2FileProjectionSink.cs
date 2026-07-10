using System.Collections.Concurrent;
using System.Text.Json;

namespace DigitalBrain.Core.V2;

/// <summary>Durable local/Test projection sink. Production can replace the file repository without changing worker semantics.</summary>
public sealed class FileV2ProjectionSink(string root, string name) : IV2ProjectionSink
{
    private readonly string _root = Path.Combine(root, "v2-projections", name);
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);
    public string Name { get; } = name;

    public Task ApplyAsync(string ownerId, AggregateCommit commit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate(ownerId))
        {
            Directory.CreateDirectory(_root);
            File.AppendAllText(DataPath(ownerId), JsonSerializer.Serialize(commit, _options) + Environment.NewLine);
            return Task.CompletedTask;
        }
    }

    public Task<V2ProjectionCheckpoint?> ReadCheckpointAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = CheckpointPath(ownerId);
        if (!File.Exists(path)) return Task.FromResult<V2ProjectionCheckpoint?>(null);
        return Task.FromResult(JsonSerializer.Deserialize<V2ProjectionCheckpoint>(File.ReadAllText(path), _options));
    }

    public Task SaveCheckpointAsync(V2ProjectionCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate(checkpoint.OwnerId))
        {
            Directory.CreateDirectory(_root);
            AtomicWrite(CheckpointPath(checkpoint.OwnerId), JsonSerializer.Serialize(checkpoint, _options));
            return Task.CompletedTask;
        }
    }

    public Task QuarantineAsync(V2PoisonRecord poison, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate(poison.OwnerId))
        {
            Directory.CreateDirectory(Path.Combine(_root, "quarantine"));
            File.AppendAllText(Path.Combine(_root, "quarantine", SafeFile(poison.OwnerId) + ".jsonl"), JsonSerializer.Serialize(poison, _options) + Environment.NewLine);
            return Task.CompletedTask;
        }
    }

    private object Gate(string owner) => _locks.GetOrAdd(owner, _ => new object());
    private string DataPath(string owner) => Path.Combine(_root, SafeFile(owner) + ".jsonl");
    private string CheckpointPath(string owner) => Path.Combine(_root, SafeFile(owner) + ".checkpoint.json");
    private static string SafeFile(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static void AtomicWrite(string path, string content) { var temp = path + ".tmp-" + Guid.NewGuid().ToString("N"); File.WriteAllText(temp, content); File.Move(temp, path, true); }
}
