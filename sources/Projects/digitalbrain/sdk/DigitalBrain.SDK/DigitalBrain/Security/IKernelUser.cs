namespace DigitalBrain.SDK.DigitalBrain.Security;

/// <summary>
/// Represents the active user identity within the currently executing kernel session.
/// </summary>
public interface IKernelUser
{
    string UserId { get; }
    string Username { get; }
    bool IsAuthenticated { get; }
}
