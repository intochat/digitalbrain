using DigitalBrain.Apps;
using DigitalBrain.Microsoft.Aspire;
using DigitalBrain.Microsoft.CSharp;

namespace DigitalBrain.Modules.Apps.Tests.E2E;

// Alice shares a C# app, Bob installs and customizes it, forks and changes its code, and his change
// flows back upstream. Every installed app runs as a real script in the C# sandbox container.
public sealed class PackageSharingFacts
{
    private static readonly PackageId Upstream = PackageId.Parse("alice/researcher");
    private static readonly PackageId Fork = PackageId.Parse("bob/researcher");
    private static readonly TimeSpan BuildAndAnswer = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task ASharedCSharpAppIsInstalledCustomizedForkedAndContributedBack()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        var ct = deadline.Token;
        IApp? bobsApp = null;
        IApp? bobsFork = null;
        try
        {
            await using var brain = await E2ETest.Create()
                .WithModule<AspireModule>().WithModule<CSharpModule>(csharp => csharp.WithSandbox(RepositoryRoot())).WithModule<AppsModule>()
                .StartAsync(ct);

            Caller.As("alice");
            var upstream = brain.Get<IPackage>(Upstream.ToString());
            var original = await upstream.Commit(new(Guid.NewGuid(), null, ResearcherPackage.Content("Research"), "Research briefs"));
            await upstream.Publish(new(Guid.NewGuid(), original.Id));
            var listing = Assert.Single(await brain.Get<IPackageDirectory>(PackageDirectory.Key).List());
            Assert.Equal(original.Id, listing.Revision);

            // One step from a marketplace listing to a running script in Bob's workspace.
            bobsApp = brain.Get<IApp>("workspace-bob/apps/alice/researcher");
            var installed = await bobsApp.Install(new(Guid.NewGuid(), new(listing.Package, listing.Revision), new Dictionary<string, string>()));
            Assert.Equal("Research (plain): What is Orleans?", await Ask(brain, bobsApp, installed, "What is Orleans?", ct));

            // Customizing a declared setting reruns the same revision in a fresh file; nothing is forked.
            var configured = await bobsApp.Configure(new(Guid.NewGuid(), new Dictionary<string, string> { ["style"] = "bullets" }));
            Assert.Equal(CSharpFileStatus.Stopped, (await brain.Get<ICSharpFile>(installed.CSharpFile!).Read(ct)).Status);
            Assert.Equal("Research (bullets): What is Orleans?", await Ask(brain, bobsApp, configured, "What is Orleans?", ct));

            // Changing the code needs a fork.
            Caller.As("bob");
            var fork = brain.Get<IPackage>(Fork.ToString());
            await fork.Fork(new(Guid.NewGuid(), new(listing.Package, listing.Revision)));
            var summaries = await fork.Commit(new(Guid.NewGuid(), original.Id, ResearcherPackage.Content("Summary"), "Summarize instead"));
            bobsFork = brain.Get<IApp>("workspace-bob/apps/bob/researcher");
            var forkInstalled = await bobsFork.Install(new(Guid.NewGuid(), new(Fork, summaries.Id), new Dictionary<string, string>()));
            Assert.Equal("Summary (plain): What is Orleans?", await Ask(brain, bobsFork, forkInstalled, "What is Orleans?", ct));

            // Bob proposes the change back; Alice accepts and publishes it.
            var proposal = await upstream.Propose(new(Guid.NewGuid(), new(Fork, summaries.Id), "Summaries"));
            Caller.As("alice");
            var accepted = await upstream.Accept(new(Guid.NewGuid(), proposal.Number));
            Assert.Equal(summaries.Id, accepted.Head);
            await upstream.Publish(new(Guid.NewGuid(), summaries.Id));
            Assert.Equal(summaries.Id, Assert.Single(await brain.Get<IPackageDirectory>(PackageDirectory.Key).List(), item => item.Package == Upstream).Revision);

            // Bob's original install upgrades to the contributed revision and keeps his setting.
            var upgraded = await bobsApp.Upgrade(new(Guid.NewGuid(), new(Upstream, summaries.Id)));
            Assert.Equal("Summary (bullets): What is Orleans?", await Ask(brain, bobsApp, upgraded, "What is Orleans?", ct));
        }
        finally
        {
            Caller.Clear();
            if (bobsApp is not null) { await Uninstall(bobsApp); }
            if (bobsFork is not null) { await Uninstall(bobsFork); }
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx"))) { return directory.FullName; }
        }
        throw new InvalidOperationException("The sandbox mounts the repository; run the test from inside it.");
    }

    // The first answer waits for the container to build the script, so a failure reports its logs.
    private static async Task<string?> Ask(E2EBrain brain, IApp app, AppSnapshot installed, string question, CancellationToken ct)
    {
        var file = brain.Get<ICSharpFile>(installed.CSharpFile!);
        var invocation = await app.Invoke(new(Guid.NewGuid(), "research", question));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(BuildAndAnswer);
        try
        {
            while (invocation.Status == InvocationStatus.Pending)
            {
                if ((await file.Read(timeout.Token)).Status == CSharpFileStatus.Exited)
                { Assert.Fail("The script exited:\n" + await file.ReadLogs(200, timeout.Token)); }
                await Task.Delay(500, timeout.Token);
                invocation = await app.ReadInvocation(invocation.Id);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { Assert.Fail("No answer within " + BuildAndAnswer + ":\n" + await file.ReadLogs(200, ct)); }
        Assert.Equal(InvocationStatus.Completed, invocation.Status);
        return invocation.Output;
    }

    private static async Task Uninstall(IApp app)
    {
        try { await app.Uninstall(new(Guid.NewGuid())); }
        catch (InvalidOperationException) { }
    }
}
