namespace DigitalBrain.Identity.State;

[GenerateSerializer, Alias("db.v2.identity.IdentityDirectoryState")]
internal sealed class IdentityDirectoryState
{
    [Id(0)] public Dictionary<string, AccountState> Accounts { get; set; } = [];
    [Id(1)] public Dictionary<string, string> ExternalLinks { get; set; } = [];
    [Id(2)] public Dictionary<string, SessionState> Sessions { get; set; } = [];
    [Id(3)] public Dictionary<string, LinkCodeState> LinkCodes { get; set; } = [];
    [Id(4)] public Dictionary<string, AutomationGrantState> Grants { get; set; } = [];
    [Id(5)] public string? OwnerUserId { get; set; }
}

[GenerateSerializer, Alias("db.v2.identity.AccountState")]
internal sealed class AccountState
{
    [Id(0)] public bool Disabled { get; set; }
    [Id(1)] public Dictionary<string, WorkspaceRole> Memberships { get; set; } = [];
}

[GenerateSerializer, Alias("db.v2.identity.SessionState")]
internal sealed record SessionState([property: Id(0)] string UserId, [property: Id(1)] string CsrfToken, [property: Id(2)] DateTimeOffset ExpiresAt);
[GenerateSerializer, Alias("db.v2.identity.LinkCodeState")]
internal sealed record LinkCodeState([property: Id(0)] string UserId, [property: Id(1)] string SessionHash, [property: Id(2)] DateTimeOffset ExpiresAt);
[GenerateSerializer, Alias("db.v2.identity.AutomationGrantState")]
internal sealed record AutomationGrantState(
    [property: Id(0)] string UserId, [property: Id(1)] string WorkspaceId,
    [property: Id(2)] string AutomationId, [property: Id(3)] string[] Targets, [property: Id(4)] string[] Actions);
