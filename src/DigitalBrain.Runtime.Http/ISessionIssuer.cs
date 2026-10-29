using DigitalBrain.Capabilities.Identity;
using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Runtime.Http;

/// <summary>
/// Issues and ends the session cookie for a member another mechanism already authenticated (09-03, identity row 1). The
/// session itself is identity@1 <see cref="ISessions"/>; this is the HTTP half that writes and clears the cookie, kept out of
/// the contract package (Phase 5 D-15). The principal, account, brain and role claims always come from the member;
/// <see cref="SessionRequest.Claims"/> never replaces them (<see cref="SessionPrincipal"/>).
/// </summary>
public interface ISessionIssuer
{
    /// <summary>Issues a session for the request and writes its cookie on <paramref name="http"/>.</summary>
    Task<SessionIssued> IssueAsync(HttpContext http, SessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Ends the session of <paramref name="http"/>, if any, and clears its cookie.</summary>
    Task EndAsync(HttpContext http, CancellationToken cancellationToken = default);
}
