using System.Security.Cryptography;
using System.Text;

namespace Brain.Modules.Behavior;

internal static class BehaviorGrainKey
{
    internal static string Create(string workspace, string behaviorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
        var workspaceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workspace)));
        return $"{workspaceHash}:{behaviorId}";
    }
}
