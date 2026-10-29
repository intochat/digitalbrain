using DigitalBrain.Capabilities;

namespace DigitalBrain;

/// <summary>
/// What the composed modules declared, by module id: their privileged grants (<see cref="ModuleManifest.Privileged"/>) and the
/// secrets they read (<see cref="ModuleManifest.Secrets"/>). Capability implementations ask it before a privileged member or a
/// named-secret lookup. Every question fails closed: a null or unknown module id, or an undeclared member or secret, is refused.
/// </summary>
public sealed class ModuleDeclarations
{
    private readonly Dictionary<string, HashSet<PrivilegedGrant>> _privileged = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _secrets = new(StringComparer.Ordinal);

    /// <summary>Collects the declarations of <paramref name="manifests"/>; a manifest without an id declares nothing.</summary>
    public ModuleDeclarations(IEnumerable<ModuleManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        foreach (var manifest in manifests)
        {
            if (string.IsNullOrWhiteSpace(manifest?.Id)) { continue; }
            if (!_privileged.TryGetValue(manifest.Id, out var grants)) { _privileged[manifest.Id] = grants = []; }
            grants.UnionWith(manifest.Privileged);
            if (!_secrets.TryGetValue(manifest.Id, out var secrets)) { _secrets[manifest.Id] = secrets = new(StringComparer.Ordinal); }
            secrets.UnionWith(manifest.Secrets.Select(secret => secret.Name));
        }
    }

    /// <summary>No module declares anything: every privileged call and named lookup is refused.</summary>
    public static ModuleDeclarations None { get; } = new([]);

    /// <summary>True when module <paramref name="moduleId"/> declares a grant for <paramref name="member"/> of <paramref name="capability"/>.</summary>
    public bool IsPrivileged(string? moduleId, CapabilityId capability, string member)
        => !string.IsNullOrWhiteSpace(moduleId) && !string.IsNullOrWhiteSpace(member)
            && _privileged.TryGetValue(moduleId, out var grants) && grants.Contains(new PrivilegedGrant(capability, member));

    /// <summary>
    /// True when module <paramref name="moduleId"/> declares the secret <paramref name="name"/>. A declared secret
    /// <c>signin-issuer</c> covers the name itself and its keys (<c>signin-issuer:SigningKey</c>).
    /// </summary>
    public bool DeclaresSecret(string? moduleId, string name)
    {
        if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(name)) { return false; }
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        var declared = colon < 0 ? name : name[..colon];
        return declared.Length > 0 && _secrets.TryGetValue(moduleId, out var secrets) && secrets.Contains(declared);
    }

    /// <summary>Throws <see cref="PrivilegeDenied"/> unless <see cref="IsPrivileged"/> holds.</summary>
    public void RequirePrivileged(string? moduleId, CapabilityId capability, string member)
    {
        if (!IsPrivileged(moduleId, capability, member)) { throw new PrivilegeDenied(moduleId, capability, member); }
    }
}
