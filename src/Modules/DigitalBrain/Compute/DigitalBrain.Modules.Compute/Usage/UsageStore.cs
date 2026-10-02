using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Compute.Usage;

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
