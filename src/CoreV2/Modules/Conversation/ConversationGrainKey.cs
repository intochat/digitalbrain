using System.Security.Cryptography;
using System.Text;

namespace Brain.Modules.Conversation;

internal static class ConversationGrainKey
{
    internal static string Create(string workspace, string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        var workspaceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workspace)));
        return $"{workspaceHash}:{conversationId}";
    }
}
