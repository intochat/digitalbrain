namespace Ino.Core.Hosting.Brain;

/// <summary>
/// Thrown by <c>InoInstanceContextFilter</c> when the
/// <c>RequestContext</c> identity keys do not match the receiving
/// <c>IInoNeuron</c> activation's grain key. This is a security boundary,
/// not a sanity check — a mismatch means a caller addressed grain
/// <c>(uA/sX)</c> while propagating <c>(uB/sX)</c> in context. Treat as
/// fatal for the call.
/// </summary>
public sealed class InoInstanceMismatchException : InvalidOperationException
{
    public InoInstanceMismatchException(string expectedKey, string? actualUserId, string? actualSessionId)
        : base($"InoNeuron activation key '{expectedKey}' does not match RequestContext (userId='{actualUserId ?? "<null>"}', sessionId='{actualSessionId ?? "<null>"}').")
    {
        ExpectedKey = expectedKey;
        ActualUserId = actualUserId;
        ActualSessionId = actualSessionId;
    }

    public string ExpectedKey { get; }
    public string? ActualUserId { get; }
    public string? ActualSessionId { get; }
}
