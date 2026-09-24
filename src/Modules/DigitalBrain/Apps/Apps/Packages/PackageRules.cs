using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace DigitalBrain.Apps;

internal static partial class PackageRules
{
    public const int MaxRevisions = 256;
    public const int MaxOpenProposals = 64;
    public const int MaxReceipts = 1024;
    private const int MaxCodeBytes = 128 * 1024;

    public static void Validate(PackageContent content)
    {
        Require(content is { Manifest: not null, ModuleIds: not null }, "A package needs a manifest, source, tests and module list.");
        var manifest = content.Manifest;
        Require(!string.IsNullOrWhiteSpace(manifest.Title) && manifest.Title.Length <= 100, "A package title is 1-100 characters.");
        Require(manifest.Description is { Length: <= 2000 }, "A package description is at most 2000 characters.");
        Require(manifest.Operations is { Count: <= 32 } && manifest.Settings is { Count: <= 32 }, "A package declares at most 32 operations and 32 settings.");
        Require(manifest.Operations.All(operation => operation is { Name: not null } && OperationName().IsMatch(operation.Name) && operation.Description is { Length: <= 500 }),
            "Operation names are lowercase words joined by hyphens, with descriptions of at most 500 characters.");
        Require(manifest.Operations.Select(operation => operation.Name).Distinct(StringComparer.Ordinal).Count() == manifest.Operations.Count, "Operation names must be unique.");
        foreach (var setting in manifest.Settings)
        {
            Require(setting is { Name: not null } && SettingName().IsMatch(setting.Name) && setting.Description is { Length: <= 500 } && setting.DefaultValue is { Length: <= 4096 },
                "Setting names are letters and digits starting with a letter, with defaults of at most 4096 characters.");
            Require(!CredentialName().IsMatch(setting.Name), "Credentials never ship in a package; ask the installer to connect an account instead.");
            Require(!string.Equals(setting.Name, "App", StringComparison.OrdinalIgnoreCase), "App is reserved for the address of the installed app.");
        }
        // Settings become configuration keys, which are case-insensitive.
        Require(manifest.Settings.Select(setting => setting.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == manifest.Settings.Count, "Setting names must be unique.");
        Require(!string.IsNullOrWhiteSpace(content.Source) && System.Text.Encoding.UTF8.GetByteCount(content.Source) <= MaxCodeBytes, "Source is required and at most 128 KiB.");
        Require(!string.IsNullOrWhiteSpace(content.Tests) && System.Text.Encoding.UTF8.GetByteCount(content.Tests) <= MaxCodeBytes, "Tests are required and at most 128 KiB.");
        Require(content.ModuleIds.Count <= 32 && content.ModuleIds.All(module => ModuleId().IsMatch(module ?? "")), "At most 32 module ids of letters, digits, dots and hyphens.");
    }

    public static string Message(string? message)
    {
        Require(!string.IsNullOrWhiteSpace(message) && message.Length <= 500, "Describe the change in 1-500 characters.");
        return message!.Trim();
    }

    private static void Require([DoesNotReturnIf(false)] bool valid, string message)
    {
        if (!valid) { throw new ArgumentException(message); }
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    private static partial Regex OperationName();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,63}$")]
    private static partial Regex SettingName();

    [GeneratedRegex("password|secret|token|apikey|credential", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialName();

    [GeneratedRegex("^[A-Za-z0-9.-]{1,64}$")]
    private static partial Regex ModuleId();
}
