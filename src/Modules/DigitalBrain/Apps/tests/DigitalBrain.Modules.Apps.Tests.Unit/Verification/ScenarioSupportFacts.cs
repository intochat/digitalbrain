using DigitalBrain.Apps;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Verification;

public sealed class ScenarioSupportFacts
{
    [Fact]
    public async Task InvocationHandlersRecoverPendingWorkAndContinueWithLiveSignals()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await PackageBrain.StartAsync(ct);
        Caller.As("alice");
        var id = PackageId.Parse("alice/invocation-support");
        var revision = await brain.Get<IPackage>(id.ToString()).Commit(brain.Commit(null, new(new("Test", "", [new("ask", "Ask")], []), "// source")));
        var app = brain.Get<IApp>("scenario/invocations");
        await app.Install(new(Guid.NewGuid(), new(id, revision.Id), new Dictionary<string, string>()));
        var pending = await app.Invoke(new(Guid.NewGuid(), "ask", "before subscription"));
        await using var invocations = app.Invocations(brain.Brain, ct).GetAsyncEnumerator(ct);
        Assert.True(await invocations.MoveNextAsync());
        Assert.Equal(pending.Id, invocations.Current.InvocationId);
        await app.Respond(new(pending.Id, "Recovered", null));
        Assert.Equal("Recovered", await app.AwaitResponse(pending.Id, ct));

        var next = invocations.MoveNextAsync().AsTask();
        var live = await app.Invoke(new(Guid.NewGuid(), "ask", "after subscription"));
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(10), ct));
        Assert.Equal(live.Id, invocations.Current.InvocationId);
    }

    [Fact]
    public async Task ScratchScenariosReportStableIdsAndAlwaysUninstall()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await PackageBrain.StartAsync(ct);
        Caller.As("alice");
        var id = PackageId.Parse("alice/scenario-support");
        var revision = await brain.Get<IPackage>(id.ToString()).Commit(brain.Commit(null, new(new("Test", "", [], []), "// source")));
        using var output = new StringWriter();
        string? installedKey = null;
        var suite = new AppScenarioSuite(key => { installedKey = key; return brain.Get<IApp>(key); }, new(id, revision.Id), output);

        await suite.Run("11111111111111111111111111111111", "A failing scenario", (_, _) => throw new InvalidOperationException("Expected failure"), ct);

        Assert.Equal(1, suite.ExitCode);
        Assert.Equal(AppStatus.Uninstalled, (await brain.Get<IApp>(installedKey!).Read()).Status);
        var verdict = Assert.Single(CSharpFileTestRunner.ParseVerdicts(output.ToString()));
        Assert.Equal("11111111111111111111111111111111", verdict.ScenarioId);
        Assert.False(verdict.Passed);
        Assert.Contains("Expected failure", verdict.Message);
    }

    [Fact]
    public void ScenarioIdsSurviveRenamesAndNeverFallBackToAnotherScenariosName()
    {
        var document = AppDocumentFacts.Document() with { Behaviors = [] };
        var content = new PackageContent(new("Test", "", [], []), "", new()
        {
            [AppDocumentCodec.Path] = AppDocumentCodec.Encode(document),
            [PackageContent.SpecPath] = AppDocumentCodec.ExportSpec(document),
        });
        var id = document.Scenarios[0].Id;
        Assert.True(AppScenarioChecks.Bind(content, new([new("Previous title", true, "", id)], 0)).Green);
        Assert.False(AppScenarioChecks.Bind(content, new([new(document.Scenarios[0].Name, true, "", "wrong-id")], 0)).Green);
        Assert.False(AppScenarioChecks.Bind(content, new([new("Previous title", true, "", id), new("Previous title", true, "", id)], 0)).Green);
    }
}
