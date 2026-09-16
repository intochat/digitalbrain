using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Identity.State;

namespace DigitalBrain.Identity;

/// <summary>Trusted server facade. Provider proof and owner bootstrap credentials must be verified before calling.</summary>
public sealed class IdentityService(IGrainFactory grains)
{
    private IIdentityDirectory Directory => grains.GetGrain<IIdentityDirectory>("identity");

    public Task<IdentitySession?> SignInAsync(VerifiedExternalIdentity identity, TimeSpan lifetime)
        => Directory.SignIn(identity, lifetime);

    public Task<IdentitySession> BootstrapOwnerAsync(VerifiedExternalIdentity identity, string workspaceId, TimeSpan lifetime)
        => Directory.BootstrapOwner(identity, workspaceId, lifetime);

    public Task<AuthenticatedIdentity?> AuthenticateAsync(string token) => Directory.Authenticate(Hash(token));
    public Task RevokeSessionAsync(string token) => Directory.RevokeSession(Hash(token));
    public Task<string> CreateLinkCodeAsync(string token, TimeSpan lifetime) => Directory.CreateLinkCode(Hash(token), lifetime);
    public Task<IdentitySession?> RedeemLinkCodeAsync(string code, VerifiedExternalIdentity identity, TimeSpan sessionLifetime)
        => Directory.RedeemLinkCode(Hash(code), identity, sessionLifetime);
    public Task<IdentityAccount?> GetAccountAsync(string userId) => Directory.GetAccount(userId);
    public Task<IdentityAccount> CreateMemberAsync(string ownerToken, VerifiedExternalIdentity identity, string workspaceId)
        => Directory.CreateMember(Hash(ownerToken), identity, workspaceId);
    public Task SetAccountDisabledAsync(string ownerToken, string userId, bool disabled)
        => Directory.SetAccountDisabled(Hash(ownerToken), userId, disabled);
    public Task SetMembershipAsync(string ownerToken, string workspaceId, string userId, WorkspaceRole role)
        => Directory.SetMembership(Hash(ownerToken), workspaceId, userId, role);
    public Task<string> GrantAutomationAsync(string token, string workspaceId, string automationId, string[] targets, string[] actions)
        => Directory.GrantAutomation(Hash(token), workspaceId, automationId, targets, actions);
    public Task RevokeAutomationAsync(string token, string grantId) => Directory.RevokeAutomation(Hash(token), grantId);
    public Task<bool> IsAutomationAuthorizedAsync(string grantId, string workspaceId, string automationId, string target, string action)
        => Directory.IsAutomationAuthorized(grantId, workspaceId, automationId, target, action);
    public Task<bool> OwnsAutomationGrantAsync(string userId, string grantId, string workspaceId, string automationId)
        => Directory.OwnsAutomationGrant(userId, grantId, workspaceId, automationId);

    internal static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    internal static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
