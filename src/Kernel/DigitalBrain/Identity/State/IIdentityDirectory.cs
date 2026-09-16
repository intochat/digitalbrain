namespace DigitalBrain.Identity.State;

[Alias("db.v2.identity.directory")]
internal interface IIdentityDirectory : IGrainWithStringKey
{
    [Alias("SignIn")]
    Task<IdentitySession?> SignIn(VerifiedExternalIdentity identity, TimeSpan lifetime);
    [Alias("BootstrapOwner")]
    Task<IdentitySession> BootstrapOwner(VerifiedExternalIdentity identity, string workspaceId, TimeSpan lifetime);
    [Alias("Authenticate")]
    Task<AuthenticatedIdentity?> Authenticate(string sessionHash);
    [Alias("RevokeSession")]
    Task RevokeSession(string sessionHash);
    [Alias("CreateLinkCode")]
    Task<string> CreateLinkCode(string sessionHash, TimeSpan lifetime);
    [Alias("RedeemLinkCode")]
    Task<IdentitySession?> RedeemLinkCode(string codeHash, VerifiedExternalIdentity identity, TimeSpan sessionLifetime);
    [Alias("GetAccount")]
    Task<IdentityAccount?> GetAccount(string userId);
    [Alias("CreateMember")]
    Task<IdentityAccount> CreateMember(string sessionHash, VerifiedExternalIdentity identity, string workspaceId);
    [Alias("SetAccountDisabled")]
    Task SetAccountDisabled(string sessionHash, string userId, bool disabled);
    [Alias("SetMembership")]
    Task SetMembership(string sessionHash, string workspaceId, string userId, WorkspaceRole role);
    [Alias("GrantAutomation")]
    Task<string> GrantAutomation(string sessionHash, string workspaceId, string automationId, string[] targets, string[] actions);
    [Alias("RevokeAutomation")]
    Task RevokeAutomation(string sessionHash, string grantId);
    [Alias("IsAutomationAuthorized")]
    Task<bool> IsAutomationAuthorized(string grantId, string workspaceId, string automationId, string target, string action);
    [Alias("OwnsAutomationGrant")]
    Task<bool> OwnsAutomationGrant(string userId, string grantId, string workspaceId, string automationId);
}
