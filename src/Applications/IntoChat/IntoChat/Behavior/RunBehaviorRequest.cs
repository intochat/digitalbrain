using System.Text.Json;

namespace IntoChat;

internal sealed record RunBehaviorRequest(JsonElement Input, string? RunId = null);
