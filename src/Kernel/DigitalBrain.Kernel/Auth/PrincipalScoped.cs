using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

// Neuron names cannot contain '/' (owner/name separator). Scope per-principal resources as
// "{principal:N}.{local}" so chat/surface grains are isolated by authenticated principal.
internal static class PrincipalScoped
{
    public static string InstanceName(PrincipalId principal, string localName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localName);

        var local = localName.Trim();
        if (local.Contains('/', StringComparison.Ordinal) || local.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Resource names cannot contain '/' or whitespace.",
                nameof(localName));
        }

        return $"{principal.Value:N}.{local}";
    }
}

internal static class PrincipalChat
{
    public static string InstanceName(PrincipalId principal, string conversationName)
        => PrincipalScoped.InstanceName(principal, conversationName);
}

internal static class PrincipalSurface
{
    public static string InstanceName(PrincipalId principal, string surfaceName)
        => PrincipalScoped.InstanceName(principal, surfaceName);
}
