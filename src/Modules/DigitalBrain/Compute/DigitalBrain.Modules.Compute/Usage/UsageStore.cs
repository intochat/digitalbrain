using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Compute.Usage;

public sealed record UsageRow(string Id, string Payload, DateTimeOffset OccurredAt, long? Revision = null);
public sealed record UsagePage(IReadOnlyList<UsageRow> Items, string? NextCursor);

public interface IUsageStore
{
    ValueTask EnsureCreatedAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    ValueTask AppendAsync(string account, string workspace, string id, string payload, CancellationToken ct = default, DateTimeOffset? revision = null);
    ValueTask<UsagePage> ReadAsync(string account, string workspace, int limit, string? cursor, CancellationToken ct = default);
}

internal static class UsagePaging
{
    public static string Scope(string account, string workspace) => Hash(JsonSerializer.Serialize(new[] { account, workspace }));
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Key(string id) => DateTime.UtcNow.Ticks.ToString("D19") + "-" + Hash(id);
    public static DateTimeOffset OccurredAt(string key) => new(long.Parse(key[..19], System.Globalization.CultureInfo.InvariantCulture), TimeSpan.Zero);
    public static string Encode(string account, string workspace, string key) => Convert.ToBase64String(Encoding.UTF8.GetBytes(Scope(account, workspace) + ":" + key));
    public static string? Decode(string account, string workspace, int limit, string? cursor)
    {
        if (limit is < 1 or > 100) { throw new ArgumentException("Limit must be between 1 and 100."); }
        if (cursor is null) { return null; }
        try
        {
            if (cursor.Length > 256) { throw new FormatException(); }
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var prefix = Scope(account, workspace) + ":";
            if (!value.StartsWith(prefix, StringComparison.Ordinal)) { throw new FormatException(); }
            var key = value[prefix.Length..];
            if (key.Length != 84 || key[19] != '-' || !key[..19].All(char.IsAsciiDigit)
                || !key[20..].All(char.IsAsciiHexDigitLower)) { throw new FormatException(); }
            return key;
        }
        catch (FormatException) { throw new ArgumentException("Invalid usage cursor."); }
    }
}

// Legacy receipt format, retained for explicit read-only migration and format tests.
// Production application writes use neuron-backed storage.
internal sealed class FileUsageStore(string root, bool readOnly = false) : IUsageStore
{
    private readonly SemaphoreSlim gate = new(1);
    private sealed record Stored(string Key, string Id, string Payload, long Revision = 0);

    private static async Task<Stored> ReadStored(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            bufferSize: 4096, useAsync: true);
        return (await JsonSerializer.DeserializeAsync<Stored>(stream, cancellationToken: ct).ConfigureAwait(false))!;
    }

    public async ValueTask AppendAsync(string account, string workspace, string id, string payload, CancellationToken ct = default, DateTimeOffset? revision = null)
    {
        if (readOnly) { throw new InvalidOperationException("Legacy compute storage is read-only."); }
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var directory = Path.Combine(root, UsagePaging.Scope(account, workspace));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, UsagePaging.Hash(id) + ".json");
            var version = (revision ?? DateTimeOffset.UtcNow).UtcTicks;
            var stored = File.Exists(path) ? await ReadStored(path, ct).ConfigureAwait(false)
                : new Stored(UsagePaging.Key(id), id, payload, -1);
            if (version >= stored.Revision)
            {
                stored = stored with { Payload = payload, Revision = version };
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(stored), ct).ConfigureAwait(false);
                File.Move(temporary, path, true);
            }
            // A retry repairs a crash between the atomic receipt write and index publication.
            var index = Path.Combine(directory, stored.Key + ".idx");
            await File.WriteAllTextAsync(index, "", ct).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async ValueTask<UsagePage> ReadAsync(string account, string workspace, int limit, string? cursor, CancellationToken ct = default)
    {
        var before = UsagePaging.Decode(account, workspace, limit, cursor);
        var directory = Path.Combine(root, UsagePaging.Scope(account, workspace));
        if (!Directory.Exists(directory)) { return new([], null); }
        var candidates = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "*.idx"))
        {
            ct.ThrowIfCancellationRequested();
            var key = Path.GetFileNameWithoutExtension(path);
            if (before is not null && StringComparer.Ordinal.Compare(key, before) >= 0) { continue; }
            candidates.Add(key);
            if (candidates.Count > limit + 1) { candidates.Remove(candidates.Min!); }
        }
        var keys = candidates.Reverse().ToArray();
        var rows = new List<UsageRow>();
        foreach (var key in keys.Take(limit))
        {
            var stored = await ReadStored(Path.Combine(directory, key![20..] + ".json"), ct).ConfigureAwait(false);
            rows.Add(new(stored.Id, stored.Payload, UsagePaging.OccurredAt(stored.Key), stored.Revision));
        }
        return new(rows, keys.Length > limit ? UsagePaging.Encode(account, workspace, keys[limit - 1]!) : null);
    }
}
