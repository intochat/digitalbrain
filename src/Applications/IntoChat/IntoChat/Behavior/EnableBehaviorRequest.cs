namespace IntoChat;

internal sealed record EnableBehaviorRequest(bool Enabled, long? ExpectedVersion = null);
