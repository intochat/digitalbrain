using DigitalBrain.Apps;
using DigitalBrain.Behavior;
using DigitalBrain.Coding;

namespace DigitalBrain.Modules.Apps.Tests.E2E;

// Alice shares a behavior, Bob installs and customizes it, forks and changes its code, and his
// change flows back upstream. Every revision is compiled and tested; every app runs in a real worker.
public sealed class PackageSharingFacts
{
    private static readonly PackageId Upstream = PackageId.Parse("alice/researcher");
    private static readonly PackageId Fork = PackageId.Parse("bob/researcher");

    [Fact]
    public async Task ASharedBehaviorIsInstalledCustomizedForkedAndContributedBack()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        var ct = deadline.Token;
        var root = Path.Combine(Path.GetTempPath(), "brain-packages-e2e", Guid.NewGuid().ToString("N"));
        try
        {
            await using var brain = await E2ETest.Create()
                .WithModule<CodingModule>().WithModule<BehaviorModule>().WithModule<AppsModule>()
                .WithExecution(new TestExecutionOptions
                {
                    PrivateConfiguration = new Dictionary<string, string?>
                    {
                        [CodeExecutionOptions.SectionName + ":Root"] = Path.Combine(root, "coding"),
                        [BehaviorOptions.SectionName + ":Root"] = Path.Combine(root, "behaviors"),
                        [BehaviorOptions.SectionName + ":HeartbeatInterval"] = "00:00:00.250",
                    },
                })
                .StartAsync(ct);

            Caller.As("alice");
            var upstream = brain.Get<IPackage>(Upstream.ToString());
            var original = await CommitChecked(brain, upstream, null, ResearcherPackage.Content("Research"), "Research briefs", ct);
            await upstream.Publish(new(Guid.NewGuid(), original.Id));
            var listing = Assert.Single(await brain.Get<IPackageDirectory>(PackageDirectory.Key).List());
            Assert.Equal(original.Id, listing.Revision);

            // One step from a marketplace listing to a running behavior in Bob's workspace.
            var bobsApp = brain.Get<IApp>("workspace-bob/apps/alice/researcher");
            var installed = await bobsApp.Install(new(Guid.NewGuid(), new(listing.Package, listing.Revision), new Dictionary<string, string>()));
            await Running(brain, installed, 1, ct);
            Assert.Equal("Research (plain): What is Orleans?", await Ask(bobsApp, "What is Orleans?", ct));

            // Customizing a declared setting redeploys the same revision; nothing is forked.
            var configured = await bobsApp.Configure(new(Guid.NewGuid(), new Dictionary<string, string> { ["style"] = "bullets" }));
            await Running(brain, configured, 2, ct);
            Assert.Equal("Research (bullets): What is Orleans?", await Ask(bobsApp, "What is Orleans?", ct));

            // Changing the code needs a fork, and the fork's own tests gate the change.
            Caller.As("bob");
            var fork = brain.Get<IPackage>(Fork.ToString());
            await fork.Fork(new(Guid.NewGuid(), new(listing.Package, listing.Revision)));
            var failing = await Check(brain, ResearcherPackage.Content("Summary") with { Tests = ResearcherPackage.Tests("Research") }, ct);
            Assert.Equal(CodeCheckStatus.Failed, failing.Status);
            Assert.Null(failing.Artifact);
            var summaries = await CommitChecked(brain, fork, original.Id, ResearcherPackage.Content("Summary"), "Summarize instead", ct);
            var bobsFork = brain.Get<IApp>("workspace-bob/apps/bob/researcher");
            var forkInstalled = await bobsFork.Install(new(Guid.NewGuid(), new(Fork, summaries.Id), new Dictionary<string, string>()));
            await Running(brain, forkInstalled, 1, ct);
            Assert.Equal("Summary (plain): What is Orleans?", await Ask(bobsFork, "What is Orleans?", ct));

            // Bob proposes the change back; Alice accepts and publishes it.
            var proposal = await upstream.Propose(new(Guid.NewGuid(), new(Fork, summaries.Id), "Summaries"));
            Caller.As("alice");
            var accepted = await upstream.Accept(new(Guid.NewGuid(), proposal.Number));
            Assert.Equal(summaries.Id, accepted.Head);
            await upstream.Publish(new(Guid.NewGuid(), summaries.Id));
            Assert.Equal(summaries.Id, Assert.Single(await brain.Get<IPackageDirectory>(PackageDirectory.Key).List(), item => item.Package == Upstream).Revision);

            // Bob's original install upgrades to the contributed revision and keeps his setting.
            var upgraded = await bobsApp.Upgrade(new(Guid.NewGuid(), new(Upstream, summaries.Id)));
            await Running(brain, upgraded, 3, ct);
            Assert.Equal("Summary (bullets): What is Orleans?", await Ask(bobsApp, "What is Orleans?", ct));

            await bobsApp.Uninstall(new(Guid.NewGuid()));
            await bobsFork.Uninstall(new(Guid.NewGuid()));
        }
        finally
        {
            Caller.Clear();
            if (Directory.Exists(root)) { Directory.Delete(root, true); }
        }
    }

    private static async Task<PackageRevision> CommitChecked(E2EBrain brain, IPackage package, string? head, PackageContent content, string message, CancellationToken ct)
    {
        var check = await Check(brain, content, ct);
        Assert.True(check.Artifact is not null, string.Join("\n", check.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return await package.Commit(new(Guid.NewGuid(), head, content, check.Artifact, message));
    }

    private static async Task<CodeCheckSnapshot> Check(E2EBrain brain, PackageContent content, CancellationToken ct)
    {
        var draft = brain.Get<ICodeDraft>("package-draft-" + Guid.NewGuid().ToString("N"));
        await draft.Save(new(0, Guid.NewGuid(), content.Source, content.Tests, content.ModuleIds), ct);
        var check = await draft.Check(new(1, Guid.NewGuid()), ct);
        while (check.Status is CodeCheckStatus.Queued or CodeCheckStatus.Building or CodeCheckStatus.Testing)
        {
            await Task.Delay(100, ct);
            check = await draft.ReadCheck(check.OperationId, ct);
        }
        return check;
    }

    private static async Task Running(E2EBrain brain, AppSnapshot app, long deployment, CancellationToken ct)
    {
        var program = brain.Get<IBehaviorProgram>(app.BehaviorProgram!);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        while (true)
        {
            var state = await program.Read(timeout.Token);
            if (state.Ready && state.ActiveDeploymentRevision == deployment) { return; }
            if (state.State == BehaviorExecutionState.Failed)
            {
                var logs = await program.ReadLogs(0, 200, timeout.Token);
                Assert.Fail(state.Error + "\n" + string.Join("\n", logs.Entries.Select(entry => entry.Message)));
            }
            await Task.Delay(100, timeout.Token);
        }
    }

    private static async Task<string?> Ask(IApp app, string question, CancellationToken ct)
    {
        var invocation = await app.Invoke(new(Guid.NewGuid(), "research", question));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (invocation.Status == InvocationStatus.Pending)
        {
            await Task.Delay(100, timeout.Token);
            invocation = await app.ReadInvocation(invocation.Id);
        }
        Assert.Equal(InvocationStatus.Completed, invocation.Status);
        return invocation.Output;
    }
}
