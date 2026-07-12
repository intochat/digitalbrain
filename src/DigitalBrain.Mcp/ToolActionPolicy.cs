using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;

namespace DigitalBrain.Mcp;

public sealed class ToolActionPolicy
{
    private const int MaximumLabelCharacters = 64;
    private const int MaximumTargetCharacters = 4096;

    public ToolActionPolicy(string? salesforceRedirectUri = null) =>
        _ = salesforceRedirectUri;

    public bool IsAllowed(ToolAction? action) =>
        action is not null &&
        string.Equals(action.Kind, "openUrl", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(action.Label) &&
        action.Label.Length <= MaximumLabelCharacters &&
        !action.Label.Any(char.IsControl) &&
        !string.IsNullOrWhiteSpace(action.Target) &&
        action.Target.Length <= MaximumTargetCharacters &&
        OAuthCallbackPaths.TryParseInternalStartPath(action.Target, out _, out _);

    public bool IsAllowedOpenUrl(string label, string? target) =>
        target is not null && IsAllowed(new ToolAction("openUrl", label, target));

    public bool IsAllowedOpenUrl(string provider, string label, string? target) =>
        target is not null &&
        IsAllowed(new ToolAction("openUrl", label, target)) &&
        OAuthCallbackPaths.TryParseInternalStartPath(target, provider, out _);
}
