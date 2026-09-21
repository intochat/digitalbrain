using DigitalBrain.AI;
using DigitalBrain.AI.Conversations;
using DigitalBrain.Flutter;
using IntoChat.Agent;
using IntoChat.Tests.E2E.Workspace;
using IntoChat.Workspace;
using Microsoft.Playwright;
using System.Text.Json;

namespace IntoChat.Tests.E2E.Agent;

public sealed class AgentTableJourneyFacts
{
    [Fact(Timeout = 300_000)]
    public async Task UserRequestOpensTableAndRecoversAfterCancellationAndFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await IntoChatE2ETest.Create()
            .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint))
            .ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp())
            .StartAsync(ct);
        await LeadData.SeedAsync(brain, "Beyond first page", ct);
        var page = brain.Page;
        await page.SetViewportSizeAsync(1600, 1000);
        var projectId = await WorkspaceBrowser.CreateProjectAsync(page, "Agent workspace");
        await SendAsync(page, "Show active leads from Supabase");
        var window = page.GetByRole(AriaRole.Region, new() { Name = "Active leads", Exact = true });
        await Assertions.Expect(window.GetByText("Company 1", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(window.GetByText("Inactive control", new() { Exact = true })).ToHaveCountAsync(0);
        await model.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        model.AssertCompleted();

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.BeforeTable = async token => { started.TrySetResult(); await release.Task.WaitAsync(token); };
        try
        {
            var request = await page.RunAndWaitForRequestAsync(() => SendAsync(page, "Cancel a delayed request"),
                request => request.Url.EndsWith("/agent", StringComparison.Ordinal) && request.Method == "POST");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            await page.GetByRole(AriaRole.Button, new() { Name = "Stop response" }).ClickAsync();
            await Assertions.Expect(page.GetByText("Response stopped.", new() { Exact = true })).ToBeVisibleAsync();
            using var input = JsonDocument.Parse(request.PostData!);
            var threadId = input.RootElement.GetProperty("threadId").GetString()!;
            var conversation = brain.Get<IConversation>(ConversationCoordinator.Key(WorkspaceScope.Create("owner", projectId).Id, threadId));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while ((await conversation.Read()).ActiveRunId is not null) { await Task.Delay(100, deadline.Token); }
            Assert.Single((await conversation.Read()).Turns);
            await Assertions.Expect(window).ToHaveCountAsync(1);
        }
        finally { release.TrySetResult(); }

        model.BeforeTable = null;
        model.Sql = "select missing_column from leads";
        var failure = page.GetByText("The request could not be completed. Check the data connection or try again.", new() { Exact = true });
        await SendAsync(page, "Try an invalid query");
        await Assertions.Expect(failure).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(window).ToHaveCountAsync(1);

        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.BeforeTable = async token => { retryStarted.TrySetResult(); await retryRelease.Task.WaitAsync(token); };
        try
        {
            await WorkspaceBrowser.EnterTextAsync(page.GetByRole(AriaRole.Textbox, new() { Name = "Message" }), "Retry the query");
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true })).ToBeEnabledAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            await Assertions.Expect(failure).ToBeHiddenAsync();
            retryRelease.TrySetResult();
            await Assertions.Expect(failure).ToBeVisibleAsync(new() { Timeout = 60_000 });
            await WorkspaceBrowser.EnterTextAsync(page.GetByRole(AriaRole.Textbox, new() { Name = "Message" }), "Another request");
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true })).ToBeEnabledAsync();
            await Assertions.Expect(window).ToHaveCountAsync(1);
        }
        finally { retryRelease.TrySetResult(); }
    }

    private static async Task SendAsync(IPage page, string text)
    {
        await WorkspaceBrowser.EnterTextAsync(page.GetByRole(AriaRole.Textbox, new() { Name = "Message" }), text);
        await page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
    }
}
