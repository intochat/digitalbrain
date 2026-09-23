using DigitalBrain.AI;
using DigitalBrain.Compute;
using DigitalBrain.Flutter;
using IntoChat.Workspace;
using IntoChat.Tests.E2E.Workspace;
using Microsoft.Playwright;

namespace IntoChat.Tests.E2E.Agent;

// J3: a chat request finds LeadGenerator through discovery, shows its consent sheet as a UI card,
// and after approval opens the Leads window. J4: a paid request shows the plan card and, after
// "Allow once", charges 9 Compute and keeps the originals.
public sealed class AgentAppJourneyFacts
{
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==";

    [Fact(Timeout = 300_000)]
    public async Task FindDentalClinicsProposesLeadGeneratorAndOpensLeadsAfterApproval()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await IntoChatE2ETest.Create()
            .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint))
            .ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp())
            .StartAsync(ct);
        var page = brain.Page;
        await page.SetViewportSizeAsync(1600, 1000);
        await WorkspaceBrowser.CreateProjectAsync(page, "Lead workspace");

        await SendAsync(page, "Find new dental clinics in Berlin");

        await Assertions.Expect(page.GetByRole(AriaRole.Group, new() { Name = "LeadGenerator · consent", Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
        var approve = page.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true });
        await Assertions.Expect(approve).ToBeVisibleAsync();

        await approve.ClickAsync();

        var leads = page.GetByRole(AriaRole.Region, new() { Name = "Leads", Exact = true });
        await Assertions.Expect(leads).ToBeVisibleAsync(new() { Timeout = 60_000 });
        model.AssertNoProtocolErrors();
    }

    [Fact(Timeout = 300_000)]
    public async Task ThreePhotosShowPlanCardThenChargeNine()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("intochat-j4-").FullName;
        try
        {
            foreach (var name in new[] { "photo-1.png", "photo-2.png", "photo-3.png" })
            {
                await File.WriteAllBytesAsync(Path.Combine(root, name), Convert.FromBase64String(OnePixelPng), ct);
            }

            await using var model = await ScriptedModelServer.StartAsync(ct);
            await using var brain = await IntoChatE2ETest.Create(privateConfiguration: new()
            {
                ["IntoChat:LocalFiles:Roots:downloads"] = root,
                ["IntoChat:LocalFiles:AssetDirectory"] = Path.Combine(root, "assets"),
                ["IntoChat:BackgroundRemoval:FailingImages:0"] = "photo-3.png",
            })
                .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint))
                .ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp())
                .StartAsync(ct);
            var page = brain.Page;
            await page.SetViewportSizeAsync(1600, 1000);
            var projectId = await WorkspaceBrowser.CreateProjectAsync(page, "Paid workspace");

            await SendAsync(page, "Remove the background from these 3 product photos");

            await Assertions.Expect(page.GetByRole(AriaRole.Group, new() { Name = "The app sees only these 3 images", Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            var allowOnce = page.GetByRole(AriaRole.Button, new() { Name = "Allow once", Exact = true });
            await Assertions.Expect(allowOnce).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Always, up to 100 a month", Exact = true })).ToBeVisibleAsync();

            await allowOnce.ClickAsync();

            await Assertions.Expect(page.GetByRole(AriaRole.Group, new() { Name = "Charged 9 Compute", Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            var scope = WorkspaceScope.Create("owner", projectId).Id;
            var report = await brain.Get<IAllowanceLedger>("owner").ReadLimitsAsync(ct);
            Assert.Equal(9m, report.SpentCompute);
            model.AssertNoProtocolErrors();
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task SendAsync(IPage page, string text)
    {
        await WorkspaceBrowser.EnterTextAsync(page.GetByRole(AriaRole.Textbox, new() { Name = "Message" }), text);
        await page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
    }
}