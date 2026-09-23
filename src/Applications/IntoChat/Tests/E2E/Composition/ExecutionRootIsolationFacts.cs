using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;

namespace IntoChat.Tests.E2E.Composition;

/// <summary>
/// Regression guard for the startup collision where the test AppHost inherited the developer's
/// persisted <c>IntoChat:BehaviorAuthoring:Root</c> from the shared user secrets and failed to open
/// <c>execution.lock</c>. The harness must hand the AppHost a unique per-run temporary root instead.
/// </summary>
public sealed class ExecutionRootIsolationFacts
{
    private const string BehaviorRootKey = "IntoChat:BehaviorAuthoring:Root";

    [Fact]
    public async Task HarnessSuppliesAUniqueTemporaryBehaviorRoot()
    {
        var builder = IntoChatE2ETest.Create();
        var args = builder.HostArguments("test-" + Guid.NewGuid().ToString("N"));
        var root = builder.ExecutionRoot;

        Assert.NotNull(root);
        Assert.Equal(BehaviorRootKey, root.ConfigurationKey);
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
            var effectiveRoot = new ConfigurationBuilder().AddConfiguration(appHost.Configuration).Build()[BehaviorRootKey];

            Assert.Equal(root.Path, effectiveRoot);
        }
        finally
        {
            await root.DisposeAsync();
        }
    }
}