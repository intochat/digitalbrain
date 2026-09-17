namespace IntoChat;

internal sealed record RollbackBehaviorRequest(long Version, long? ExpectedVersion = null);
