namespace Brain.Kernel.Connections;

public sealed record ConnectionAuthorizationState(string StateDigest, DateTimeOffset ExpiresAt);
