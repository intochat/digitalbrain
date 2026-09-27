using DigitalBrain.Sdk;
using DigitalBrain.Sdk.Connectors;
using DigitalBrain.Contracts.Types;
using IntoChat.Workspace;

namespace IntoChat.Tests;

public sealed class WorkspaceConnectionsFacts
{
    [Fact]
    public void PackageAccountsIncludeScopedCredentialsAndOnlyOwnedLegacyRecords()
    {
        var scope = WorkspaceScope.Create("alice", "one");
        static ConnectorRecord Record(string id, string workspace, string owner) => new()
        {
            Id = id, Source = "gmail", WorkspaceId = workspace, Status = ConnectorStatus.Connected,
            Credential = SecretRef.For(owner, id, id, true),
        };
        var result = WorkspaceConnectionRecords.Combine(scope, "alice",
            [Record("new", "one", "alice"), Record("wrong-workspace", "two", "alice")],
            [Record("legacy", "alice", "alice"), Record("foreign", "alice", "bob")]);
        Assert.Equal(["new", "legacy"], result.Select(record => record.Id));
    }
    [Theory]
    [InlineData(ConnectorStatus.Connected, "Configured")]
    [InlineData(ConnectorStatus.Expired, "Expired")]
    [InlineData(ConnectorStatus.Failing, "Unavailable")]
    public void StoredCredentialsDoNotImplyVerification(ConnectorStatus status, string expected)
        => Assert.Equal(expected, WorkspaceConnectionsEndpoints.Status(status));

    [Fact]
    public void AuthorizationStartRequiresConfiguredProviderAndMintsDistinctCapabilities()
    {
        var unavailable = new Logins(null);
        Assert.False(unavailable.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => unavailable.Require());
        var logins = new Logins(new Uri("https://example.test"));
        var first = logins.Require();
        Assert.True(logins.IsConfigured);
        Assert.Equal("/integrations/test/login", first.AbsolutePath);
        Assert.StartsWith("?request=", first.Query, StringComparison.Ordinal);
        Assert.NotEqual(first, logins.Require());
    }

    [Fact]
    public void RegistryScopeSeparatesAccountsAndWorkspaces()
    {
        Assert.NotEqual(WorkspaceScope.Create("alice", "one").Id, WorkspaceScope.Create("bob", "one").Id);
        Assert.NotEqual(WorkspaceScope.Create("alice", "one").Id, WorkspaceScope.Create("alice", "two").Id);
        var scope = new BrowserLoginWorkspace(WorkspaceScope.Create("alice", "one").Id);
        Assert.Equal(scope, BrowserLoginWorkspace.FromScope(scope.ToScope()));
        Assert.Null(BrowserLoginWorkspace.FromScope("compose"));
    }

    private sealed class Logins(Uri? origin) : BrowserLogins(new("test", "Test", "test", "/integrations/test/login", "/integrations/test/callback", "Test"))
    {
        protected override Uri? PublicOrigin => origin;
    }
}
