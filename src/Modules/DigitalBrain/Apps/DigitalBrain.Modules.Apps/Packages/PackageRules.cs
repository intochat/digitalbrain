using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace DigitalBrain.Apps;

internal static partial class PackageRules
{
    public const int MaxRevisions = 256;
    public const int MaxOpenProposals = 64;
    public const int MaxReceipts = 1024;
    private const int MaxCodeBytes = 128 * 1024;
    private const int MaxFiles = 64;
    private static readonly HashSet<string> ReservedSettings = new(["App", "Account"], StringComparer.OrdinalIgnoreCase);

    public static void Validate(PackageContent content)
    {
        Require(content is { Manifest: not null }, "A package needs a manifest and source.");
        var manifest = content.Manifest;
        Require(!string.IsNullOrWhiteSpace(manifest.Title) && manifest.Title.Length <= 100, "A package title is 1-100 characters.");
        Require(manifest.Description is { Length: <= 2000 }, "A package description is at most 2000 characters.");
        Require(manifest.Operations is { Count: <= 32 } && manifest.Settings is { Count: <= 32 } && manifest.Accounts is null or { Count: <= 32 }, "A package declares at most 32 operations, settings and accounts.");
        Require(manifest.Operations.All(operation => operation is { Name: not null } && OperationName().IsMatch(operation.Name) && operation.Description is { Length: <= 500 }),
            "Operation names are lowercase words joined by hyphens, with descriptions of at most 500 characters.");
        Require(manifest.Operations.Select(operation => operation.Name).Distinct(StringComparer.Ordinal).Count() == manifest.Operations.Count, "Operation names must be unique.");
        foreach (var setting in manifest.Settings)
        {
            Require(setting is { Name: not null } && SettingName().IsMatch(setting.Name) && setting.Description is { Length: <= 500 } && setting.DefaultValue is { Length: <= 4096 },
                "Setting names are letters and digits starting with a letter, with defaults of at most 4096 characters.");
            Require(!CredentialName().IsMatch(setting.Name), "Credentials never ship in a package; ask the installer to connect an account instead.");
            Require(!ReservedSettings.Contains(setting.Name), "App and Account are reserved: App is the installed app's address and Account prefixes the connected accounts.");
        }
        // Settings become configuration keys, which are case-insensitive.
        Require(manifest.Settings.Select(setting => setting.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == manifest.Settings.Count, "Setting names must be unique.");
        var accounts = manifest.Accounts ?? [];
        Require(accounts.All(account => account is { Name: not null, Source: not null, Description: not null }
            && SettingName().IsMatch(account.Name) && ModuleId().IsMatch(account.Source) && account.Description.Length <= 500),
            "Account slots need a name, source and description.");
        Require(accounts.Select(account => account.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == accounts.Count,
            "Account slot names must be unique.");
        Require(!accounts.Any(account => manifest.Settings.Any(setting => string.Equals(setting.Name, account.Name, StringComparison.OrdinalIgnoreCase))),
            "Account slots and settings cannot share a name.");
        Require(RuntimeName().IsMatch(manifest.RuntimeName), "A runtime name is lowercase words joined by hyphens.");
        Require(content.Source is not null && System.Text.Encoding.UTF8.GetByteCount(content.Source) <= MaxCodeBytes, "Source is at most 128 KiB.");
        Require(manifest.RuntimeName != PackageManifest.CSharpRuntime || content.Programs().Count > 0,
            "A csharp app needs at least one script: its source or a behaviors/*.cs file.");
        // Mirror ICSharpFile.Write's limits, so an install never starts some behaviors and then
        // trips over one the file neuron refuses, leaving the started ones without a retiree.
        foreach (var (path, program) in content.Programs())
        {
            Require(!string.IsNullOrWhiteSpace(program), $"Program {path} is empty.");
            Require(System.Text.Encoding.UTF8.GetByteCount(program) <= MaxCodeBytes, $"Program {path} is at most 128 KiB.");
        }
        var files = content.Files ?? new Dictionary<string, string>();
        Require(files.Count <= MaxFiles && files.All(file => file.Key.Length <= 128 && FilePath().IsMatch(file.Key) && !file.Key.Contains("..", StringComparison.Ordinal) && file.Value is not null),
            "A package has at most 64 files with relative paths of letters, digits, dots, hyphens and single slashes.");
        Require(files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == files.Count, "File paths must differ by more than letter case.");
        Require(System.Text.Encoding.UTF8.GetByteCount(content.Source ?? "") + files.Values.Sum(System.Text.Encoding.UTF8.GetByteCount) <= MaxCodeBytes * 4,
            "A package is at most 512 KiB of text.");
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

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    private static partial Regex RuntimeName();

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.-]*(/[A-Za-z0-9_][A-Za-z0-9_.-]*)*$")]
    private static partial Regex FilePath();
}
