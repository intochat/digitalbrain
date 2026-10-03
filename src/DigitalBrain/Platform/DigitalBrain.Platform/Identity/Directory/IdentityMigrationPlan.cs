using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Kernel.Enforcement;

namespace DigitalBrain.Platform.Identity.Directory;

// An in-memory preflight result. Only its digest is persisted; never log password hashes.
internal sealed class IdentityMigrationPlan
{
    public string Id { get; private set; } = "";
    public required Account[] Accounts { get; init; }
    public required PrincipalImport[] Principals { get; init; }
    public required ScopeImport[] Scopes { get; init; }
    public required string[] GrantInventory { get; init; }

    public void ComputeId(string? snapshotId)
        => Id = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { Version = 1, Snapshot = snapshotId, Accounts, Principals, Scopes, GrantInventory })));

    public static IdentityMigrationPlan Create(IdentityDirectoryState source, IdentityMigrationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SourceSnapshotId) || options.LegacyGrantBrainIds is null)
        { throw new InvalidOperationException("Supply SourceSnapshotId and the complete LegacyGrantBrainIds inventory before migration."); }
        var accounts = source.Accounts.OrderBy(a => a.AccountId, StringComparer.Ordinal).ToArray();
        if (accounts.Any(a => string.IsNullOrWhiteSpace(a.AccountId) || string.IsNullOrWhiteSpace(a.OwnerPrincipalId))
            || source.Members.Any(m => string.IsNullOrWhiteSpace(m.PrincipalId) || !Enum.IsDefined(m.Role))
            || source.PasswordHashes.Any(p => string.IsNullOrWhiteSpace(p.Key) || string.IsNullOrWhiteSpace(p.Value)))
        { throw new InvalidOperationException("Legacy identity contains invalid account, principal, or membership records."); }
        if (accounts.Select(a => a.AccountId).Distinct(StringComparer.Ordinal).Count() != accounts.Length)
        { throw new InvalidOperationException("Legacy accounts contain duplicate IDs."); }
        var scopes = source.Members.Select(m => (m.AccountId, m.BrainId))
            .Concat(source.Invitations.Select(i => (i.AccountId, BrainId: i.WorkspaceId)))
            .Concat(options.LegacyBrainAccounts.Select(kv => (AccountId: kv.Value, BrainId: kv.Key)))
            .Distinct().OrderBy(s => s.AccountId, StringComparer.Ordinal).ThenBy(s => s.BrainId, StringComparer.Ordinal).ToArray();
        if (scopes.Any(s => string.IsNullOrWhiteSpace(s.AccountId) || string.IsNullOrWhiteSpace(s.BrainId))
            || scopes.GroupBy(s => s.BrainId).Any(g => g.Count() != 1))
        { throw new InvalidOperationException("Legacy grant keys are ambiguous across accounts; resolve the mapping before migration."); }
        if (options.LegacyGrantBrainIds.Any(id => !scopes.Any(s => s.BrainId == id)))
        { throw new InvalidOperationException("Every inventoried legacy grant store requires an account mapping."); }
        foreach (var scope in scopes) { _ = BrainScope.Create(scope.AccountId, scope.BrainId); }
        var principals = source.PasswordHashes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(pair =>
        {
            var owned = source.Members.Where(m => m.PrincipalId == pair.Key
                && accounts.Any(a => a.AccountId == m.AccountId && a.OwnerPrincipalId == pair.Key)).ToArray();
            var preferred = owned.Where(m => m.BrainId == "account-" + m.AccountId).ToArray();
            var member = preferred.Length == 1 ? preferred[0] : owned.Length == 1 ? owned[0]
                : throw new InvalidOperationException("A legacy principal has no unambiguous default account membership.");
            return new PrincipalImport(member, pair.Value);
        }).ToArray();
        var imports = scopes.Select(scope => new ScopeImport
        {
            AccountId = scope.AccountId,
            BrainId = scope.BrainId,
            Members = source.Members.Where(m => m.AccountId == scope.AccountId && m.BrainId == scope.BrainId)
                .OrderBy(m => m.PrincipalId, StringComparer.Ordinal).ToArray(),
            Invitations = source.Invitations.Where(i => i.AccountId == scope.AccountId && i.WorkspaceId == scope.BrainId)
                .OrderBy(i => i.Code, StringComparer.Ordinal).ToArray()
        }).ToArray();
        if (imports.Any(s => s.Members.Select(m => m.PrincipalId).Distinct(StringComparer.Ordinal).Count() != s.Members.Length))
        { throw new InvalidOperationException("Legacy scope contains duplicate principal memberships."); }
        return new()
        {
            Accounts = accounts,
            Principals = principals,
            Scopes = imports,
            GrantInventory = options.LegacyGrantBrainIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
        };
    }

    internal sealed record PrincipalImport(Member Member, string Hash);
    internal sealed class ScopeImport
    {
        public required string AccountId { get; init; }
        public required string BrainId { get; init; }
        public required Member[] Members { get; init; }
        public required Invitation[] Invitations { get; init; }
        public Grant[] Grants { get; set; } = [];
    }
}
