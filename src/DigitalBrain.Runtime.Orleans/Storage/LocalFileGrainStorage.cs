using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>File-backed grain storage persisting named stores (<c>platform</c> and module stores) under <c>&lt;Home&gt;/state/files/&lt;name&gt;</c>.</summary>
internal sealed class LocalFileGrainStorage : IGrainStorage
{
    private static readonly JsonSerializerOptions EnvelopeJson = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly string _directory;
    private readonly Serializer? _serializer;
    private readonly ConcurrentDictionary<string, Lock> _locks = new(StringComparer.Ordinal);

    public LocalFileGrainStorage(string directory, Serializer? serializer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        _serializer = serializer;
    }

    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        ArgumentNullException.ThrowIfNull(grainState);
        var key = $"{grainId}|{stateName}";
        var gate = _locks.GetOrAdd(key, static _ => new Lock());
        lock (gate)
        {
            var path = FilePath(key);
            if (!File.Exists(path))
            {
                grainState.State = CreateDefault<T>();
                grainState.ETag = null;
                grainState.RecordExists = false;
                return Task.CompletedTask;
            }

            var bytes = File.ReadAllBytes(path);
            var envelope = JsonSerializer.Deserialize<StoredGrainEnvelope>(bytes, EnvelopeJson)
                ?? throw new IOException($"Corrupt grain state file '{path}'.");
            grainState.State = Decode<T>(envelope.Format, envelope.Payload);
            grainState.ETag = envelope.ETag;
            grainState.RecordExists = true;
            return Task.CompletedTask;
        }
    }

    public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        ArgumentNullException.ThrowIfNull(grainState);
        var key = $"{grainId}|{stateName}";
        var gate = _locks.GetOrAdd(key, static _ => new Lock());
        lock (gate)
        {
            Directory.CreateDirectory(_directory);
            var path = FilePath(key);
            if (File.Exists(path))
            {
                var existingBytes = File.ReadAllBytes(path);
                var existing = JsonSerializer.Deserialize<StoredGrainEnvelope>(existingBytes, EnvelopeJson);
                if (grainState.ETag is not null && existing is not null && !string.Equals(existing.ETag, grainState.ETag, StringComparison.Ordinal))
                {
                    throw new InconsistentStateException("ETag mismatch writing local grain state.", existing.ETag, grainState.ETag);
                }
            }
            else if (grainState.ETag is not null)
            {
                throw new InconsistentStateException("Grain state record was deleted before write.", string.Empty, grainState.ETag);
            }

            var (format, payload) = Encode(grainState.State);
            var nextEtag = Guid.NewGuid().ToString("N");
            var envelope = new StoredGrainEnvelope(grainId.ToString(), stateName, nextEtag, format, payload);
            var serialized = JsonSerializer.SerializeToUtf8Bytes(envelope, EnvelopeJson);
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllBytes(tempPath, serialized);
                File.Move(tempPath, path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tempPath)) { File.Delete(tempPath); } } catch { }
                throw;
            }

            grainState.ETag = nextEtag;
            grainState.RecordExists = true;
            return Task.CompletedTask;
        }
    }

    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        ArgumentNullException.ThrowIfNull(grainState);
        var key = $"{grainId}|{stateName}";
        var gate = _locks.GetOrAdd(key, static _ => new Lock());
        lock (gate)
        {
            var path = FilePath(key);
            if (File.Exists(path))
            {
                if (grainState.ETag is not null)
                {
                    var existingBytes = File.ReadAllBytes(path);
                    var existing = JsonSerializer.Deserialize<StoredGrainEnvelope>(existingBytes, EnvelopeJson);
                    if (existing is not null && !string.Equals(existing.ETag, grainState.ETag, StringComparison.Ordinal))
                    {
                        throw new InconsistentStateException("ETag mismatch clearing local grain state.", existing.ETag, grainState.ETag);
                    }
                }
                File.Delete(path);
            }
            grainState.State = CreateDefault<T>();
            grainState.ETag = null;
            grainState.RecordExists = false;
            return Task.CompletedTask;
        }
    }

    private (string Format, byte[] Payload) Encode<T>(T value)
    {
        try
        {
            return ("json", JournalPayloads.EncodeState(value));
        }
        catch (JournalPayloadException) when (_serializer is not null)
        {
            return ("orleans", _serializer.SerializeToArray(value));
        }
    }

    private T Decode<T>(string format, byte[] payload)
    {
        if (string.Equals(format, "orleans", StringComparison.Ordinal))
        {
            if (_serializer is null)
            {
                throw new InvalidOperationException($"Orleans serializer is required to read grain state {typeof(T).FullName}.");
            }
            return _serializer.Deserialize<T>(payload) ?? CreateDefault<T>();
        }
        return JournalPayloads.DecodeState<T>(payload) ?? CreateDefault<T>();
    }

    private static T CreateDefault<T>()
        => typeof(T).IsValueType ? default! : Activator.CreateInstance<T>()!;

    private string FilePath(string key)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_directory, $"{hash}.json");
    }

    private sealed record StoredGrainEnvelope(
        string GrainId,
        string StateName,
        string ETag,
        string Format,
        byte[] Payload);
}
