namespace DigitalBrain.Capabilities.Identity;

/// <summary>
/// What a module does when a member signs in, running as that member. Invoked after the session is issued; a failing
/// handler is logged and never fails sign-in.
/// </summary>
public interface ISignInHandler
{
    /// <summary>The member signed in.</summary>
    Task SignedIn(Member member, CancellationToken cancellationToken);
}
