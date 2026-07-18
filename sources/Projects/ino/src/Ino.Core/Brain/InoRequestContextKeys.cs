namespace Ino.Core.Brain;

public static class InoRequestContextKeys
{
    public const string UserId = "ino.userId";
    public const string SessionId = "ino.sessionId";

    // Flutter / brain UI hue keying — distinct from per-trace telemetry.
    // "autonomic" is reserved for background-mind grains (spec §2.1).
    public const string AutonomicSessionId = "autonomic";
}
