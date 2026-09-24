using System.Security.Cryptography;
using System.Text.Json;

namespace DigitalBrain.Apps;

internal static class PackageHash
{
    private static readonly JsonSerializerOptions Canonical = new(JsonSerializerDefaults.Web);

    // Spelled out field by field so a revision id never changes when contract types gain members.
    public static string Revision(IReadOnlyList<string> parents, PackageContent content) => Of(new
    {
        parents,
        title = content.Manifest.Title,
        description = content.Manifest.Description,
        operations = content.Manifest.Operations.Select(operation => new[] { operation.Name, operation.Description }),
        settings = content.Manifest.Settings.Select(setting => new[] { setting.Name, setting.Description, setting.DefaultValue }),
        source = content.Source,
        tests = content.Tests,
        moduleIds = content.ModuleIds,
    });

    public static string Of<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Canonical)));
}
