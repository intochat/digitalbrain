using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;

namespace DigitalBrain.Mcp;

public sealed class ToolActionPolicy
{
    public bool IsAllowed(ToolAction? action) => OAuthCallbackPaths.IsStructurallyValidAction(action);

    public bool IsAllowedOpenUrl(string provider, string label, string? target) =>
        target is not null &&
        IsAllowed(new ToolAction("openUrl", label, target)) &&
        OAuthCallbackPaths.TryParseInternalStartPath(target, provider, out _);
}
