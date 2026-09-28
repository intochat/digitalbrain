using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Apps.Assistant;

public static class AssistantConversations
{
    // Preserve the existing persisted conversation identity during the host-to-app migration.
    public static string Key(string workspace, string thread) =>
        "agent-conversation-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { workspace, thread })))).ToLowerInvariant();
}
