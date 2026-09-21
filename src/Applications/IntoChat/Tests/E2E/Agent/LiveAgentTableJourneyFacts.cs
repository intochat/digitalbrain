using DigitalBrain.AI;
using DigitalBrain.Flutter;
using IntoChat.Tests.E2E.Workspace;
using Microsoft.Playwright;

namespace IntoChat.Tests.E2E.Agent;

public sealed class LiveAgentTableJourneyFacts
{
    public static bool LiveEnabled => Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_LIVE_MODEL") == "1";

    [Fact(Timeout = 300_000, SkipUnless = nameof(LiveEnabled), Skip = "Set DIGITALBRAIN_E2E_LIVE_MODEL=1 and DIGITALBRAIN_E2E_MODEL_API_KEY to run paid live verification.")]
    [Trait("Category", "LiveModel")]
    public async Task LiveModelOpensAnInteractiveSupabaseTable()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_MODEL_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(key), "Live model verification requires an explicit API key.");
        var endpoint = new Uri(Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_MODEL_ENDPOINT") ?? "https://api.openai.com/v1/");
        await using var brain = await IntoChatE2ETest.Create(key!)
            .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, endpoint))
            .ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp())
            .StartAsync(ct);
        await LeadData.SeedAsync(brain, "Beyond first page", ct);
        var page = brain.Page;
        await page.SetViewportSizeAsync(1600, 1000);
        await WorkspaceBrowser.CreateProjectAsync(page, "Live model workspace");
        await WorkspaceBrowser.EnterTextAsync(page.GetByRole(AriaRole.Textbox, new() { Name = "Message" }),
            "Discover Supabase schema and show all active leads in an interactive table with id, company and email ordered by id. Title it Active leads.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
        var window = page.GetByRole(AriaRole.Region, new() { Name = "Active leads", Exact = true });
        await Assertions.Expect(window).ToBeVisibleAsync(new() { Timeout = 120_000 });
        await Assertions.Expect(window.GetByText("Company 1", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(window.GetByText("Inactive control", new() { Exact = true })).ToHaveCountAsync(0);
    }
}