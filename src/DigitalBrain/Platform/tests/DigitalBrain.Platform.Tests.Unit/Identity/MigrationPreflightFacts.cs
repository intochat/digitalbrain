using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Identity.Directory;

namespace DigitalBrain.Platform.Tests.Unit.Identity;

public sealed class MigrationPreflightFacts
{
    [Fact]
    public void GrantOnlyStoresRequireOwnershipBeforeAnyImport()
    {
        var options = new IdentityMigrationOptions { SourceSnapshotId = "snapshot", LegacyGrantBrainIds = ["orphan"] };
        Assert.Throws<InvalidOperationException>(() => IdentityMigrationPlan.Create(new(), options));
        options.LegacyBrainAccounts["orphan"] = "account";
        Assert.Single(IdentityMigrationPlan.Create(new(), options).Scopes);
    }

    [Fact]
    public void AmbiguousPrincipalDefaultFailsDuringPreflight()
    {
        var source = new IdentityDirectoryState
        {
            Accounts = [new Account { AccountId = "account", Name = "Account", OwnerPrincipalId = "alice" }],
            PasswordHashes = new() { ["alice"] = "hash" }
        };
        Assert.Throws<InvalidOperationException>(() => IdentityMigrationPlan.Create(source,
            new() { SourceSnapshotId = "snapshot", LegacyGrantBrainIds = [] }));
    }

    [Fact]
    public void PlanDigestChangesWhenSourceSnapshotOrMappingChanges()
    {
        var options = new IdentityMigrationOptions { SourceSnapshotId = "one", LegacyGrantBrainIds = ["brain"], LegacyBrainAccounts = new() { ["brain"] = "account" } };
        var first = IdentityMigrationPlan.Create(new(), options);
        first.ComputeId(options.SourceSnapshotId);
        var same = IdentityMigrationPlan.Create(new(), options);
        same.ComputeId(options.SourceSnapshotId);
        Assert.Equal(first.Id, same.Id);
        same.ComputeId("two");
        Assert.NotEqual(first.Id, same.Id);
        options.LegacyBrainAccounts["brain"] = "another-account";
        var changed = IdentityMigrationPlan.Create(new(), options);
        changed.ComputeId(options.SourceSnapshotId);
        Assert.NotEqual(first.Id, changed.Id);
    }
}
