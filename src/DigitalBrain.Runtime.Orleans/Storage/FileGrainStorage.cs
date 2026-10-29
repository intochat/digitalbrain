using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// Grain state as one JSON file per grain under a folder. File names are a SHA-256 of the length-prefixed
/// service id, grain type, grain key and state name, so no key can form a path. Writes go to a temp file in the same
/// folder, are flushed, and replace the record atomically under a per-path lock; a leftover temp file is ignored.
/// </summary>
public sealed class FileGrainStorage : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>
{
    private const string Extension = ".json";
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly string _name;
    private readonly string _serviceId;
    private readonly IGrainStorageSerializer _serializer;

    /// <summary>Creates a store.</summary>
    public FileGrainStorage(string name, string rootDirectory, string serviceId, IGrainStorageSerializer serializer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(serializer);
        _name = name;
        RootDirectory = Path.GetFullPath(rootDirectory);
        _serviceId = serviceId;
        _serializer = serializer;
    }

    /// <summary>The folder that holds the files.</summary>
    public string RootDirectory { get; }

    /// <inheritdoc />
    public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        ArgumentNullException.ThrowIfNull(grainState);
        var path = PathOf(stateName, grainId);
        var gate = Gate(path);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var record = await Load(path).ConfigureAwait(false);
            if (record is null)
            {
                grainState.State = Activator.CreateInstance<T>();
                grainState.ETag = null;
                grainState.RecordExists = false;
                return;
            }
            grainState.State = _serializer.Deserialize<T>(new BinaryData(record.Data));
            grainState.ETag = record.ETag;
            grainState.RecordExists = true;
        }
        finally { gate.Release(); }
    }

    /// <inheritdoc />
    public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        ArgumentNullException.ThrowIfNull(grainState);
        var path = PathOf(stateName, grainId);
        var gate = Gate(path);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var current = await Load(path).ConfigureAwait(false);
            Check(stateName, grainId, current?.ETag, grainState.ETag);
            var etag = Guid.NewGuid().ToString("N");
            var data = _serializer.Serialize(grainState.State).ToArray();
            await Save(path, new Record(etag, data)).ConfigureAwait(false);
            grainState.ETag = etag;
            grainState.RecordExists = true;
        }
        finally { gate.Release(); }
    }

    /// <inheritdoc />
    public async Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        ArgumentNullException.ThrowIfNull(grainState);
        var path = PathOf(stateName, grainId);
        var gate = Gate(path);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var current = await Load(path).ConfigureAwait(false);
            if (current is not null) { Check(stateName, grainId, current.ETag, grainState.ETag); }
            if (File.Exists(path)) { File.Delete(path); }
            grainState.ETag = null;
            grainState.RecordExists = false;
            grainState.State = Activator.CreateInstance<T>();
        }
        finally { gate.Release(); }
    }

    /// <inheritdoc />
    public void Participate(ISiloLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        lifecycle.Subscribe($"{nameof(FileGrainStorage)}-{_name}", ServiceLifecycleStage.ApplicationServices,
            _ => { Directory.CreateDirectory(RootDirectory); return Task.CompletedTask; });
    }

    /// <summary>The file name of one record: SHA-256 hex of the length-prefixed parts.</summary>
    public static string FileNameOf(string serviceId, string grainType, string grainKey, string stateName)
    {
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(grainType);
        ArgumentNullException.ThrowIfNull(grainKey);
        ArgumentNullException.ThrowIfNull(stateName);
        var builder = new StringBuilder();
        foreach (var part in new[] { serviceId, grainType, grainKey, stateName })
        {
            builder.Append(part.Length).Append(':').Append(part).Append('|');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))) + Extension;
    }

    internal string PathOf(string stateName, GrainId grainId)
        => Path.Combine(RootDirectory, FileNameOf(_serviceId, grainId.Type.ToString()!, grainId.Key.ToString()!, stateName ?? string.Empty));

    private SemaphoreSlim Gate(string path) => _locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

    private void Check(string stateName, GrainId grainId, string? stored, string? expected)
    {
        if (!string.Equals(stored, expected, StringComparison.Ordinal))
        {
            throw new InconsistentStateException(
                $"Grain store {_name}: ETag mismatch for state {stateName} of {grainId}.", stored ?? "none", expected ?? "none");
        }
    }

    private static async Task<Record?> Load(string path)
    {
        if (!File.Exists(path)) { return null; }
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync(stream, FileStorageJson.Default.Record).ConfigureAwait(false);
        }
    }

    private async Task Save(string path, Record record)
    {
        Directory.CreateDirectory(RootDirectory);
        await AtomicFile.Write(path, stream => JsonSerializer.SerializeAsync(stream, record, FileStorageJson.Default.Record)).ConfigureAwait(false);
    }

    internal sealed record Record(string ETag, byte[] Data);
}

/// <summary>Temp file in the same folder, flushed to disk, then moved over the target.</summary>
internal static class AtomicFile
{
    internal static async Task Write(string path, Func<Stream, Task> write)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using (stream.ConfigureAwait(false))
            {
                await write(stream).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) { File.Delete(temp); }
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(FileGrainStorage.Record))]
[System.Text.Json.Serialization.JsonSerializable(typeof(FileReminderTable.Row[]))]
internal sealed partial class FileStorageJson : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>Registers <see cref="FileGrainStorage"/>.</summary>
public static class FileGrainStorageHosting
{
    /// <summary>Adds a named file grain store.</summary>
    public static ISiloBuilder AddFileGrainStorage(this ISiloBuilder silo, string name, Action<FileGrainStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(silo);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new FileGrainStorageOptions();
        configure(options);
        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            throw new ArgumentException($"Grain store {name}: {nameof(FileGrainStorageOptions.RootDirectory)} is required.", nameof(configure));
        }
        silo.Services.AddGrainStorage(name, (services, storeName) => new FileGrainStorage(
            storeName,
            options.RootDirectory,
            options.ServiceId ?? services.GetRequiredService<IOptions<ClusterOptions>>().Value.ServiceId,
            options.GrainStorageSerializer ?? services.GetRequiredService<IGrainStorageSerializer>()));
        return silo;
    }
}
