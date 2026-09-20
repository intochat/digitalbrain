using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain.Core;

public static class CompositionOverrideTransport
{
    public const string ConfigurationKey = "DigitalBrain:Testing:Overrides";
    private const int MaximumBytes = 32 * 1024;
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal sealed record Entry(string Id, string Patch);
    private sealed record Envelope(int Version, Entry[] Modules);

    internal static string Write(Entry[] entries)
    {
        var json = JsonSerializer.Serialize(new Envelope(1, entries), Json);
        ValidateSize(json);
        return json;
    }

    internal static Entry[] Read(string json)
    {
        ValidateSize(json);
        Envelope envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(json, Json) ?? throw new JsonException(); }
        catch (JsonException) { throw new ArgumentException("Invalid composition override envelope.", nameof(json)); }
        if (envelope.Version != 1 || envelope.Modules is null)
            { throw new ArgumentException("Unsupported composition override version or missing modules.", nameof(json)); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in envelope.Modules)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Id) || entry.Patch is null || !seen.Add(entry.Id))
                { throw new ArgumentException("Invalid or duplicate module override.", nameof(json)); }
        }
        return envelope.Modules;
    }

    private static void ValidateSize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes)
            { throw new ArgumentException("Composition overrides exceed the 32 KiB limit.", nameof(json)); }
    }
}
