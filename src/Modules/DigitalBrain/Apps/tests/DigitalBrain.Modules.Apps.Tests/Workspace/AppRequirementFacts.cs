using DigitalBrain.Apps;

namespace DigitalBrain.Modules.Apps.Tests.Workspace;

public sealed class AppRequirementFacts
{
    [Fact]
    public async Task PresentContractsInstallAndExposeTheInstalledApp()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var (app, request) = await Prepare(brain, "#:project /brain/DigitalBrain.Modules.Apps.Contracts.csproj\n// behavior");
        var installed = await app.Install(request);
        Assert.Equal(AppStatus.Installed, installed.Status);
        Assert.Equal(request.Revision, installed.Revision);
        Assert.Single(installed.CSharpFiles);
    }

    [Fact]
    public async Task MissingModulesAreNamedBeforeAnythingIsInstalled()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var (app, request) = await Prepare(brain, "#:project /brain/DigitalBrain.Modules.Postgres.Contracts.csproj\n// behavior");
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.Install(request));
        Assert.Equal("Cannot install this app. Missing modules: Postgres.", error.Message);
        Assert.Equal(AppStatus.NotInstalled, (await app.Read()).Status);
        Assert.Empty((await app.Read()).CSharpFiles);
        Assert.Equal(error.Message, (await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.Install(request))).Message);
    }

    [Fact]
    public async Task RenamedContractsAreReportedAsUnresolvable()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var (app, request) = await Prepare(brain, "#:project /brain/Renamed.Contracts.csproj\n// behavior");
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.Install(request));
        Assert.Equal("Cannot install this app. Unresolvable contract references: Renamed.Contracts.", error.Message);
        Assert.Equal(AppStatus.NotInstalled, (await app.Read()).Status);
    }

    [Fact]
    public async Task TestsOnlyContractsAreAlsoRequired()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var (app, request) = await Prepare(brain, "// behavior", "#:project /brain/DigitalBrain.Modules.Postgres.Contracts.csproj\n// test");
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.Install(request));
        Assert.Equal("Cannot install this app. Missing modules: Postgres.", error.Message);
        Assert.Empty((await app.Read()).CSharpFiles);
    }

    [Fact]
    public async Task ExampleDirectivesInsideCommentsAndStringsAreNotRequirements()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var (app, request) = await Prepare(brain, """"
            /*
            #:project Missing.csproj
            */
            var example = """
            #:project Missing.csproj
            """;
            """");
        Assert.Equal(AppStatus.Installed, (await app.Install(request)).Status);
    }

    [Fact]
    public async Task VerificationRefusesMissingTestContractsBeforeLaunchingTheSandbox()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var (_, request) = await Prepare(brain, "// behavior", "#:project /brain/DigitalBrain.Modules.Postgres.Contracts.csproj\n// test");
        var verification = brain.Get<IAppVerification>(IAppVerification.Key(request.Revision));

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(verification.Verify);

        Assert.Equal("Cannot install this app. Missing modules: Postgres.", error.Message);
        Assert.Null(await verification.Read());
        Assert.DoesNotContain(RecordingCSharpFile.Files.Keys, key => key.StartsWith($"specs/{request.Revision.Package}@{request.Revision.Revision}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMissingUpgradeRequirementLeavesTheRunningRevisionAndFilesUntouched()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var (app, request) = await Prepare(brain, "// behavior");
        var installed = await app.Install(request);
        Caller.As("alice");
        await brain.AuthorizeCallerAsync();
        var package = brain.Get<IPackage>(request.Revision.Package.ToString());
        var current = await package.ReadRevision(request.Revision.Revision);
        var next = await package.Commit(brain.Commit(current.Id, current.Content with { Source = "#:project /brain/DigitalBrain.Modules.Postgres.Contracts.csproj\n// upgraded" }));
        Caller.Clear();

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.Upgrade(new(Guid.NewGuid(), new(request.Revision.Package, next.Id))));

        Assert.Equal("Cannot install this app. Missing modules: Postgres.", error.Message);
        Assert.Equal(installed.Revision, (await app.Read()).Revision);
        Assert.Equal(installed.CSharpFiles, (await app.Read()).CSharpFiles);
        Assert.All(installed.CSharpFiles, file => Assert.False(RecordingCSharpFile.Deleted.ContainsKey(file)));
    }

    [Fact]
    public async Task DuplicateQuotedAndWindowsPathsNameEachMissingModuleOnce()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var (app, request) = await Prepare(brain, "#:project \"/brain/DigitalBrain.Modules.Postgres.Contracts.csproj\"\r\n#:project ..\\DigitalBrain.Modules.Postgres.Contracts.csproj\r\n// behavior");

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => app.Install(request));

        Assert.Equal("Cannot install this app. Missing modules: Postgres.", error.Message);
        Assert.Empty((await app.Read()).CSharpFiles);
    }

    private static async Task<(IApp, InstallApp)> Prepare(PackageBrain brain, string source, string? tests = null)
    {
        Caller.As("alice");
        await brain.AuthorizeCallerAsync();
        var id = PackageId.Parse("alice/requirements");
        var content = PackageSamples.Researcher("Research") with { Source = source };
        if (tests is not null) { content = content with { Files = new Dictionary<string, string> { [PackageContent.TestsPath] = tests } }; }
        var revision = await brain.Get<IPackage>(id.ToString()).Commit(brain.Commit(null, content));
        Caller.Clear();
        return (brain.Get<IApp>("requirements/" + Guid.NewGuid().ToString("N")), new(Guid.NewGuid(), new(id, revision.Id), new Dictionary<string, string>()));
    }
}
