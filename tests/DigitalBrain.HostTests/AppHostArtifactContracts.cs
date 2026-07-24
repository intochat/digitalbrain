using System.Text;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class AppHostArtifactContracts(TestingAppHostFixture fixture)
{
    [Fact]
    public async Task UnknownResourceAttachesEvidenceAndCleanupReleasesTheFixture()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = await fixture.StartAsync(cancellationToken);

        try
        {
            var silo = host.Resource("silo");
            await silo.WaitUntilHealthyAsync(cancellationToken);

            var failure = Assert.Throws<AppHostTestFailureException>(
                () => host.Resource("missing"));
            var artifact = failure.Artifact;
            var json = artifact.ToJson();

            Assert.IsType<InvalidOperationException>(failure.InnerException);
            Assert.Equal("missing", artifact.RequestedResource);
            Assert.Equal("resource.bind", artifact.Operation);
            Assert.Contains("silo", artifact.KnownResourceIds);
            Assert.Equal(
                artifact.KnownResourceIds.Order(StringComparer.Ordinal),
                artifact.KnownResourceIds);
            Assert.Contains(
                artifact.Resources,
                resource => resource.ResourceId == "silo");
            Assert.All(
                artifact.Resources.SelectMany(resource => resource.Urls),
                AssertUrlIsSanitized);
            Assert.Contains("\"state\":", json, StringComparison.Ordinal);
            Assert.Contains("\"health\":", json, StringComparison.Ordinal);
            Assert.Contains("\"urls\":", json, StringComparison.Ordinal);
            Assert.Contains("\"logs\":", json, StringComparison.Ordinal);
            Assert.Contains("\"commands\":", json, StringComparison.Ordinal);
            Assert.Equal("not-started", artifact.CleanupStage);
            Assert.Equal("not-run", artifact.CleanupResult);
            Assert.Contains(
                AppHostTestFailureException.AttachmentName,
                TestContext.Current.Attachments!.Keys);
            Assert.True(
                Encoding.UTF8.GetByteCount(json)
                <= AppHostTestArtifact.MaximumUtf8Bytes);
            Assert.All(
                artifact.Resources,
                resource =>
                {
                    Assert.True(
                        resource.Logs.Count
                        <= AppHostTestArtifact.MaximumLogsPerResource);
                    Assert.All(
                        resource.Logs,
                        line => Assert.True(
                            line.Content.Length
                            <= AppHostTestArtifact.MaximumLogLineLength));
                });
        }
        finally
        {
            try
            {
                await host.DisposeAsync();
            }
            catch (AppHostTestFailureException cleanupFailure)
            {
                Assert.Fail(cleanupFailure.Artifact.ToJson());
            }
        }

        await using var second =
            await fixture.StartAsync(cancellationToken);
        await second.DisposeAsync();
    }

    [Fact]
    public void FinalizedArtifactRedactsAndBoundsInternalEvidence()
    {
        var resources = Enumerable.Range(0, 4)
            .Select(index => ($"resource-{index}", "ProjectResource"))
            .ToArray();
        var diagnostics = new AppHostTestDiagnostics(resources);
        var overlong = new string('x', 5000);

        foreach (var (resourceId, _) in resources)
        {
            for (var index = 0; index < 250; index++)
            {
                diagnostics.RecordLog(
                    resourceId,
                    $"{index:D3}:{overlong}",
                    isError: false);
            }
        }

        diagnostics.RecordLog(
            "resource-0",
            "Authorization: Bearer top-secret-token",
            isError: true);

        for (var index = 0; index < 300; index++)
        {
            diagnostics.RecordCommand(
                "resource-0",
                "resource-restart",
                "result",
                $"token=command-secret-{index}");
        }

        var artifact = diagnostics.Snapshot(
            requestedResource: "missing",
            operation: "resource.bind",
            cleanupStage: "not-started",
            cleanupResult: "not-run");
        var json = artifact.ToJson();

        Assert.True(
            Encoding.UTF8.GetByteCount(json)
            <= AppHostTestArtifact.MaximumUtf8Bytes);
        Assert.All(
            artifact.Resources,
            resource =>
            {
                Assert.True(
                    resource.Logs.Count
                    <= AppHostTestArtifact.MaximumLogsPerResource);
                Assert.All(
                    resource.Logs,
                    line => Assert.True(
                        line.Content.Length
                        <= AppHostTestArtifact.MaximumLogLineLength));
            });
        Assert.True(
            artifact.Commands.Count
            <= AppHostTestArtifact.MaximumCommandTransitions);
        Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "top-secret-token",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "command-secret",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NullStateNotificationRemainsModelOnly()
    {
        var diagnostics = new AppHostTestDiagnostics(
            [("model-only", "ParameterResource")]);

        diagnostics.RecordNotification(
            "model-only",
            "ParameterResource",
            state: null,
            health: null,
            DateTimeOffset.UtcNow,
            exitCode: null,
            urls: []);

        var artifact = diagnostics.Snapshot(
            requestedResource: null,
            operation: "graph.cleanup");
        var resource = Assert.Single(artifact.Resources);

        Assert.False(resource.EmittedRuntimeState);
        Assert.DoesNotContain(
            "model-only",
            diagnostics.RuntimeResourceIds());
    }

    [Fact]
    public void RuntimeStateEvidenceRemainsTrueAfterALaterNullState()
    {
        var diagnostics = new AppHostTestDiagnostics(
            [("runtime-resource", "ContainerResource")]);

        diagnostics.RecordNotification(
            "runtime-resource",
            "ContainerResource",
            "Running",
            health: null,
            DateTimeOffset.UtcNow,
            exitCode: null,
            urls: []);
        diagnostics.RecordNotification(
            "runtime-resource",
            "ContainerResource",
            state: null,
            health: null,
            DateTimeOffset.UtcNow,
            exitCode: null,
            urls: []);

        var artifact = diagnostics.Snapshot(
            requestedResource: null,
            operation: "graph.cleanup");
        var resource = Assert.Single(artifact.Resources);

        Assert.True(resource.EmittedRuntimeState);
        Assert.Contains(
            "runtime-resource",
            artifact.KnownResourceIds);
        Assert.Contains(
            "runtime-resource",
            diagnostics.RuntimeResourceIds());
    }

    [Fact]
    public async Task TerminalObservationRemainsTrueAfterALaterNullState()
    {
        var diagnostics = new AppHostTestDiagnostics(
            [("runtime-resource", "ContainerResource")]);
        var terminalState = KnownResourceStates.TerminalStates[0];

        diagnostics.RecordNotification(
            "runtime-resource",
            "ContainerResource",
            KnownResourceStates.Running,
            health: null,
            DateTimeOffset.UtcNow,
            exitCode: null,
            urls: []);
        diagnostics.RecordNotification(
            "runtime-resource",
            "ContainerResource",
            terminalState,
            health: null,
            DateTimeOffset.UtcNow,
            exitCode: 0,
            urls: []);
        diagnostics.RecordNotification(
            "runtime-resource",
            "ContainerResource",
            state: null,
            health: null,
            DateTimeOffset.UtcNow,
            exitCode: null,
            urls: []);

        var terminalWait = diagnostics.WaitForTerminalAsync(
            "runtime-resource",
            CancellationToken.None);

        Assert.True(terminalWait.IsCompletedSuccessfully);
        await terminalWait;
    }

    [Fact]
    public async Task LaterRunningNotificationStartsANewTerminalLifecycle()
    {
        var diagnostics = new AppHostTestDiagnostics(
            [("runtime-resource", "ContainerResource")]);
        var terminalState = KnownResourceStates.TerminalStates[0];

        diagnostics.RecordNotification(
            "runtime-resource",
            "ContainerResource",
            terminalState,
            health: null,
            DateTimeOffset.UtcNow,
            exitCode: 0,
            urls: []);
        diagnostics.RecordNotification(
            "runtime-resource",
            "ContainerResource",
            KnownResourceStates.Running,
            health: null,
            DateTimeOffset.UtcNow,
            exitCode: null,
            urls: []);

        var terminalWait = diagnostics.WaitForTerminalAsync(
            "runtime-resource",
            CancellationToken.None);

        Assert.False(terminalWait.IsCompleted);

        diagnostics.RecordNotification(
            "runtime-resource",
            "ContainerResource",
            terminalState,
            health: null,
            DateTimeOffset.UtcNow,
            exitCode: 0,
            urls: []);

        await terminalWait;
        Assert.True(terminalWait.IsCompletedSuccessfully);
    }

    [Fact]
    public void NotStartedNotificationIsRecordedButNotAwaited()
    {
        var diagnostics = new AppHostTestDiagnostics(
            [("never-started", "Executable")]);

        diagnostics.RecordNotification(
            "never-started",
            "Executable",
            KnownResourceStates.NotStarted,
            health: null,
            DateTimeOffset.UtcNow,
            exitCode: null,
            urls: []);

        var artifact = diagnostics.Snapshot(
            requestedResource: null,
            operation: "graph.cleanup");
        var resource = Assert.Single(artifact.Resources);

        Assert.Equal(KnownResourceStates.NotStarted, resource.State);
        Assert.False(resource.EmittedRuntimeState);
        Assert.DoesNotContain(
            "never-started",
            diagnostics.RuntimeResourceIds());
    }

    [Fact]
    public void ParameterNotificationIsRecordedButNotAwaited()
    {
        var diagnostics = new AppHostTestDiagnostics(
            [("client-secret", "ParameterResource")]);

        diagnostics.RecordNotification(
            "client-secret",
            "Parameter",
            KnownResourceStates.Running,
            "Healthy",
            DateTimeOffset.UtcNow,
            exitCode: null,
            urls: []);

        var artifact = diagnostics.Snapshot(
            requestedResource: null,
            operation: "graph.cleanup");
        var resource = Assert.Single(artifact.Resources);

        Assert.Equal(KnownResourceStates.Running, resource.State);
        Assert.False(resource.EmittedRuntimeState);
        Assert.DoesNotContain(
            "client-secret",
            diagnostics.RuntimeResourceIds());
    }

    private static void AssertUrlIsSanitized(string value)
    {
        Assert.True(
            Uri.TryCreate(value, UriKind.Absolute, out var uri),
            $"Expected an absolute sanitized URL, but found '{value}'.");
        Assert.Empty(uri.UserInfo);
        Assert.Empty(uri.Query);
        Assert.Empty(uri.Fragment);
    }
}
