using System.Reflection;
using DigitalBrain.Capabilities;

namespace DigitalBrain;

/// <summary>Declares the OAuth / auth scheme and scopes required by a module.</summary>
/// <param name="Scheme">The auth scheme id (e.g. <c>google</c>, <c>github</c>).</param>
/// <param name="Scopes">The scopes the module requests.</param>
public sealed record ModuleAuth(string Scheme, IReadOnlyList<string> Scopes);

/// <summary>What a module provides, requires and needs from its host.</summary>
/// <param name="Provides">The capabilities the module implements.</param>
/// <param name="Requires">The capabilities the module calls.</param>
/// <param name="Needs">The resource kinds the host must supply.</param>
/// <param name="Settings">The options the module binds.</param>
/// <param name="Secrets">The secrets the module reads.</param>
public sealed record ModuleManifest(
    IReadOnlyList<CapabilityId> Provides,
    IReadOnlyList<CapabilityId> Requires,
    IReadOnlyList<Need> Needs,
    IReadOnlyList<Setting> Settings,
    IReadOnlyList<Secret> Secrets)
{
    /// <summary>The module id from <see cref="ModuleAttribute"/>; filled by <see cref="Of(Type)"/>.</summary>
    public string? Id { get; init; }

    /// <summary>
    /// The privileged capability members the module may call (for example identity@1 <c>Issue</c>). Every module that does not
    /// list a member here is refused it (fail closed). Empty by default.
    /// </summary>
    public IReadOnlyList<PrivilegedGrant> Privileged { get; init; } = [];

    /// <summary>Optional OAuth / auth metadata when the module connects to an external account service.</summary>
    public ModuleAuth? Auth { get; init; }

    /// <summary>Reads the <c>[Module]</c> id and the public static <c>Manifest</c> property of a module class.</summary>
    /// <exception cref="InvalidDataException">Provides or Requires lists a capability twice.</exception>
    /// <exception cref="InvalidOperationException">The type is not a module, has no <c>[Module]</c> or no static <c>Manifest</c>.</exception>
    public static ModuleManifest Of(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        if (!typeof(Module).IsAssignableFrom(moduleType))
        {
            throw new InvalidOperationException($"{moduleType.FullName} does not derive from {typeof(Module).FullName}.");
        }
        var attribute = moduleType.GetCustomAttribute<ModuleAttribute>(inherit: false)
            ?? throw new InvalidOperationException($"{moduleType.FullName} has no [Module(id)] attribute.");
        var property = moduleType.GetProperty("Manifest", BindingFlags.Public | BindingFlags.Static);
        if (property is null || property.PropertyType != typeof(ModuleManifest)
            || property.GetValue(null) is not ModuleManifest manifest)
        {
            throw new InvalidOperationException($"{moduleType.FullName} has no public static ModuleManifest Manifest property.");
        }
        RejectDuplicates(manifest.Provides, $"Module '{attribute.Id}' ({moduleType.FullName}) Provides");
        RejectDuplicates(manifest.Requires, $"Module '{attribute.Id}' ({moduleType.FullName}) Requires");
        return manifest with { Id = attribute.Id };
    }

    // A requirement is a need, not a grant: nothing grants access from Requires (T-05-01-02).
    internal static IReadOnlyList<CapabilityId> RejectDuplicates(IReadOnlyList<CapabilityId> ids, string where)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var seen = new HashSet<CapabilityId>();
        foreach (var id in ids)
        {
            if (!seen.Add(id)) { throw new InvalidDataException($"{where} lists capability {id} more than once."); }
        }
        return ids;
    }
}
