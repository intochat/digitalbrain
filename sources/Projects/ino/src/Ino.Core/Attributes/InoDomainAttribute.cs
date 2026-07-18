namespace Ino.Core;

/// <summary>
/// Optional assembly-level attribute declaring neuron metadata. If absent, the source
/// generator falls back to the .csproj PackageId, Description, and PackageTags. Used as
/// the authoritative source of keywords when present.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class InoDomainAttribute : Attribute
{
    public InoDomainAttribute(
        string id,
        string version,
        string description,
        string[]? keywords = null,
        string coreVersion = "0.1.0")
    {
        Id = id;
        Version = version;
        Description = description;
        Keywords = keywords is null
            ? Array.Empty<string>()
            : (string[])keywords.Clone();
        CoreVersion = coreVersion;
    }

    public string Id { get; }

    public string Version { get; }

    public string Description { get; }

    public IReadOnlyList<string> Keywords { get; }

    public string CoreVersion { get; }
}
