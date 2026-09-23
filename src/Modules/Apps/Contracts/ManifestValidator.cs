using System.Text.RegularExpressions;
using DigitalBrain.Contracts.Types;

namespace DigitalBrain.Apps;

// app.json declarations cannot drift from the semantic type catalog: every referenced type id must exist.
public static partial class ManifestValidator
{
    private static readonly IReadOnlySet<string> CatalogTypeIds =
        TypeCatalog.All.Select(type => type.Id).ToHashSet(StringComparer.Ordinal);

    public static void Validate(AppManifest manifest) => Validate(manifest, CatalogTypeIds);

    public static void Validate(AppManifest manifest, IReadOnlySet<string> knownTypeIds)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(knownTypeIds);

        Require(IdPattern().IsMatch(manifest.Id), $"App id '{manifest.Id}' is not a namespaced alias such as 'intochat.files'.");
        Require(VersionPattern().IsMatch(manifest.Version), $"App version '{manifest.Version}' is not a semantic version.");
        Require(!string.IsNullOrWhiteSpace(manifest.Publisher), "App publisher is required.");
        Require(!string.IsNullOrWhiteSpace(manifest.Name), "App name is required.");
        Require(!string.IsNullOrWhiteSpace(manifest.DescriptionForPeople), "A description for people is required.");
        Require(!string.IsNullOrWhiteSpace(manifest.DescriptionForModel), "A description for the model is required.");
        Require(manifest.Kind != AppKind.Remote || !string.IsNullOrWhiteSpace(manifest.RemoteEndpoint),
            "A remote app declares a remote endpoint.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in manifest.Operations)
        {
            Require(!string.IsNullOrWhiteSpace(operation.Name), "Every operation has a name.");
            Require(seen.Add(operation.Name), $"Operation '{operation.Name}' is declared twice.");
            Require(!string.IsNullOrWhiteSpace(operation.DescriptionForModel), $"Operation '{operation.Name}' needs a model description.");
            foreach (var (parameter, typeId) in operation.InputTypeIds)
            {
                Require(knownTypeIds.Contains(typeId),
                    $"Operation '{operation.Name}' parameter '{parameter}' references unknown type '{typeId}'.");
            }
            if (operation.OutputTypeId is { Length: > 0 } output)
            {
                Require(knownTypeIds.Contains(output), $"Operation '{operation.Name}' references unknown output type '{output}'.");
            }
        }

        foreach (var permission in manifest.Permissions)
        {
            Require(knownTypeIds.Contains(permission.SemanticTypeId),
                $"Permission references unknown type '{permission.SemanticTypeId}'.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new AppManifestException(message); }
    }

    [GeneratedRegex(@"^[a-z0-9]+([.\-][a-z0-9]+)*$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?(\+[0-9A-Za-z.\-]+)?$")]
    private static partial Regex VersionPattern();
}
