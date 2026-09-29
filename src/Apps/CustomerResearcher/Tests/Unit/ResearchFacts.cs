using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.WebBrowser.Signals;
using DigitalBrain.Microsoft.Playwright;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Apps.CustomerResearcher.Tests.Unit;

public sealed class ResearchFacts
{
    [Fact]
    public void EvidenceMustComeFromObservedOfficialPages()
    {
        var json = JsonSerializer.SerializeToElement(new {
            status = "found", companyName = "Acme", website = "https://acme.example/", email = "invented@acme.example",
            evidence = new[] {
                new { field = "companyName", url = "https://acme.example/", quote = "Acme" },
                new { field = "website", url = "https://acme.example/", quote = "Acme" },
                new { field = "email", url = "https://acme.example/", quote = "invented@acme.example" } }
        });
        var result = CompanyResearchAgent.Validate(json, [new("https://acme.example/", "Acme", "Acme official company", [])]);
        Assert.NotNull(result.Company);
        Assert.Null(result.Company.Email);
        Assert.Equal(2, result.Company.Evidence.Count);
        Assert.Null(CompanyResearchAgent.Validate(json, []).Company);
    }

    [Fact]
    public void AmbiguousIdentityIsNeverSavable()
    {
        var result = CompanyResearchAgent.Validate(JsonSerializer.SerializeToElement(new { status = "ambiguous" }), []);
        Assert.Null(result.Company);
        Assert.Contains("clarify", result.Status);
    }

    [Fact]
    public async Task SuccessRetryFailureAndWorkspaceIsolation()
    {
        var control = new Control();
        await using var brain = await Start(control);
        var first = await Connect(brain, "workspace-a");
        await first.Research("Acme");
        await Until(async () => (await Status(brain, "workspace-a")).StartsWith("Saved:", StringComparison.Ordinal));
        var initial = Assert.Single(control.Saves);
        control.FailSave = true;
        await first.Research("Acme");
        await Until(async () => (await Status(brain, "workspace-a")).Contains("failed", StringComparison.Ordinal));
        control.FailSave = false;
        await first.Research("Acme");
        await Until(async () => control.Saves.Count == 2);
        Assert.Equal(initial, control.Saves[1]);
        var second = await Connect(brain, "workspace-b");
        await second.Research("Acme");
        await Until(async () => { await Task.Yield(); return control.Saves.Count == 3; });
        Assert.Equal("workspace-b", control.Saves[2].Workspace);
    }

    [Fact]
    public async Task StopAndReplacementFenceLateModelResults()
    {
        var control = new Control { Block = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var brain = await Start(control);
        var app = await Connect(brain, "workspace-a");
        await app.Research("Old");
        await control.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await app.Stop();
        control.Block.SetResult();
        await Until(async () => { await Task.Yield(); return control.Completed; });
        Assert.Empty(control.Saves);
        Assert.Equal("Stopped", await Status(brain, "workspace-a"));
        control.Block = null;
        await app.Research("New");
        await Until(async () => (await Status(brain, "workspace-a")).StartsWith("Saved:", StringComparison.Ordinal));
        Assert.Single(control.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementAndDisconnectRejectAnOldCompletion(bool disconnect)
    {
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var control = new Control { Block = blocked };
        await using var brain = await Start(control);
        var app = await Connect(brain, "workspace-a");
        await app.Research("Old");
        await control.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        control.Block = null;
        if (disconnect)
        {
            await app.HandleUiEvent(new BrowserDisconnected(CustomerResearcherApp.Key("workspace-a") + "/window/browser", new string('a', 32)));
            await Until(async () => await Status(brain, "workspace-a") == "Browser disconnected");
        }
        else
        {
            await app.Research("New");
            await Until(async () => await Status(brain, "workspace-a") == "Saved: New");
        }
        blocked.SetResult();
        // A roundtrip through the grain after the old continuation settles verifies final presentation.
        await Until(async () => { await Task.Yield(); return control.Completed; });
        Assert.Equal(disconnect ? "Browser disconnected" : "Saved: New", await Status(brain, "workspace-a"));
        Assert.Equal(disconnect ? 0 : 1, control.Saves.Count);
    }

    private static Task<UnitBrain> Start(Control control) => UnitTest.Create().WithApp<CustomerResearcherApp>()
        .ConfigureSilo(silo => {
            silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=test;Username=test";
            silo.Services.AddSingleton<ICompanyResearchAgent>(control).AddSingleton<ICompanyResearchStore>(control)
                .AddSingleton<IBrowserSessionProvider>(control)
                .Configure<AIOptions>(options => { options.OpenAI.ApiKey = "fixture"; options.Default.Provider = "OpenAI"; options.Default.Model = "fixture"; });
        }).StartAsync(TestContext.Current.CancellationToken);
    private static async Task<ICustomerResearcher> Connect(UnitBrain brain, string workspace)
    {
        var key = CustomerResearcherApp.Key(workspace) + "/window";
        var app = brain.Get<ICustomerResearcher>(key);
        await app.HandleUiEvent(new BrowserConnected(key + "/browser", 12345, new string('a', 32)));
        await Until(async () => await Status(brain, workspace) == "Ready");
        return app;
    }
    private static async Task<string> Status(UnitBrain brain, string workspace) =>
        (await brain.Get<IText>(CustomerResearcherApp.Key(workspace) + "/window/status").Read()).Markdown;
    private static async Task Until(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await predicate()) { await Task.Delay(20, timeout.Token); }
    }
    private sealed class Control : ICompanyResearchAgent, ICompanyResearchStore, IBrowserSessionProvider
    {
        public readonly List<(string Workspace, string Id)> Saves = [];
        public bool FailSave;
        public bool Completed;
        public TaskCompletionSource? Block;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ResearchResult> Research(string query, IPlaywright browser, CancellationToken ct)
        {
            Started.TrySetResult();
            if (Block is { } block) { await block.Task; }
            Completed = true;
            return new(new(query, "https://example.com", null, null, null, null, null, []), "Verified");
        }
        public Task Save(string workspace, string researchId, CompanyResearch company, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (FailSave) { throw new InvalidOperationException("Database failed"); }
            Saves.Add((workspace, researchId)); return Task.CompletedTask;
        }
        public Task<IBrowserPageSession> AttachAsync(BrowserAttachment attachment, CancellationToken ct) => Task.FromResult<IBrowserPageSession>(new Page());
    }
    private sealed class Page : IBrowserPageSession
    {
        public bool Connected => true;
        public string? Url => "https://example.com";
        public string? Title => "Test";
        public Task<BrowserObservation> SnapshotAsync(CancellationToken ct) => Task.FromResult(new BrowserObservation(Url!, Title!, "Test", []));
        public Task<BrowserObservation> NavigateAsync(string url, CancellationToken ct) => SnapshotAsync(ct);
        public Task<BrowserObservation> ClickAsync(string selector, CancellationToken ct) => SnapshotAsync(ct);
        public Task<BrowserObservation> FillAsync(string selector, string value, CancellationToken ct) => SnapshotAsync(ct);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
