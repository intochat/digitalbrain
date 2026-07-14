using DigitalBrain.Kernel.Contracts.Runtime;
namespace DigitalBrain.Kernel.Contracts;

public static class OAuthCallbackPaths
{
    public const int MinimumFlowReferenceLength = 32;
    public const int MaximumFlowReferenceLength = 1024;
    public const int MaximumActionLabelLength = 64;
    public const int MaximumActionTargetLength = 4096;
    public static bool IsStructurallyValidAction(ToolAction? action) =>
            action is not null && string.Equals(action.Kind, "openUrl", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(action.Label) &&
            action.Label.Length <= MaximumActionLabelLength &&
            !action.Label.Any(char.IsControl) &&
            !string.IsNullOrWhiteSpace(action.Target) &&
            action.Target.Length <= MaximumActionTargetLength &&
            TryParseInternalStartPath(action.Target, out _, out _);
    public static bool IsProviderKey(string? provider) =>
        provider is { Length: >= 1 and <= 64 } &&
        provider.All(static character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    public static string CreateInternalStartPath(string provider, string flowReference)
    {
        if (!IsProviderKey(provider))
            throw new ArgumentException("A canonical provider key is required.", nameof(provider));
        if (!IsOpaqueFlowReference(flowReference))
            throw new ArgumentException("An opaque bounded OAuth flow reference is required.", nameof(flowReference));
        return "/oauth/start/" + provider + "?f=" + flowReference;
    }
    public static bool TryParseInternalStartPath(string? value, string expectedProvider, out string flowReference)
    {
        flowReference = string.Empty;
        if (!IsProviderKey(expectedProvider)) return false;
        var prefix = "/oauth/start/" + expectedProvider + "?f=";
        if (value is null || value.Length > prefix.Length + MaximumFlowReferenceLength || !value.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var candidate = value[prefix.Length..];
        if (!IsOpaqueFlowReference(candidate)) return false;
        flowReference = candidate;
        return true;
    }
    public static bool TryParseInternalStartPath(string? value, out string provider, out string flowReference)
    {
        provider = string.Empty;
        flowReference = string.Empty;
        const string prefix = "/oauth/start/";
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var queryIndex = value.IndexOf("?f=", prefix.Length, StringComparison.Ordinal);
        if (queryIndex < 0) return false;
        var candidateProvider = value[prefix.Length..queryIndex];
        if (!IsProviderKey(candidateProvider) || !TryParseInternalStartPath(value, candidateProvider, out flowReference))
            return false;
        provider = candidateProvider;
        return true;
    }
    public static bool IsOpaqueFlowReference(string? value) =>
        value is { Length: >= MinimumFlowReferenceLength and <= MaximumFlowReferenceLength } &&
        value.All(static character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');
}
