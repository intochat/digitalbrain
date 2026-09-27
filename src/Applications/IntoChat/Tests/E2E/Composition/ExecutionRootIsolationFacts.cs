using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;

namespace IntoChat.Tests.E2E.Composition;

// Regression guard: the test AppHost must not inherit the developer's persisted IntoChat:CSharp:Root
// from the shared user secrets. The harness hands the AppHost a unique per-run temporary root instead.
public sealed class ExecutionRootIsolationFacts
{
    private const string CSharpRootKey = "IntoChat:CSharp:Root";

    [Fact]
    public async Task HarnessSuppliesAUniqueTemporaryCSharpRoot()
    {
        var builder = IntoChatE2ETest.Create();
        var args = builder.HostArguments("test-" + Guid.NewGuid().ToString("N"));
        var root = builder.ExecutionRoot;

        Assert.NotNull(root);
        Assert.Equal(CSharpRootKey, root.ConfigurationKey);
        Assert.StartsWith(Path.GetTempPath(), root.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(root.Argument, args);

        var other = IntoChatE2ETest.Create();
        other.HostArguments("test-" + Guid.NewGuid().ToString("N"));
        Assert.NotEqual(root.Path, other.ExecutionRoot!.Path);

        await root.DisposeAsync();
        await other.ExecutionRoot.DisposeAsync();
    }

    [Fact]
    public async Task TestAppHostResolvesTheIsolatedRootInsteadOfThePersistedDeveloperRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = IntoChatE2ETest.Create();
        var args = builder.HostArguments("test-" + Guid.NewGuid().ToString("N"));
        var root = builder.ExecutionRoot!;
        try
        {
            var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IntoChat_AppHost>([.. args], ct);
            var effectiveRoot = new ConfigurationBuilder().AddConfiguration(appHost.Configuration).Build()[CSharpRootKey];

            Assert.Equal(root.Path, effectiveRoot);
        }
        finally
        {
            await root.DisposeAsync();
        }
    }
}