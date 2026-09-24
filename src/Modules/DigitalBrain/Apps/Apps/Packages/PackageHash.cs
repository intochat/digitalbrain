using System.Security.Cryptography;
using System.Text.Json;

namespace DigitalBrain.Apps;

internal static class PackageHash
{
    private static readonly JsonSerializerOptions Canonical = new(JsonSerializerDefaults.Web);

    public static string Revision(IReadOnlyList<string> parents, PackageContent content) => Of(new { parents, content });

    public static string Of<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Canonical)));
}
