using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Brain.Modules.UI.Contracts;
using DigitalBrain.ProductHost.Mcp;
using DigitalBrain.ProductHost.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brain.ProductHost.Tests;

public sealed class ProductProtocolEndpointTests
{
    [Fact]
    public async Task Product_protocol_discovers_invokes_reads_and_streams_one_runtime_path()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var activityId = Guid.Parse("9e6a3057-5ba1-49f3-923d-400617914658");
        var runtime = new FakeProductRuntimeClient(activityId);
        await using var app = await StartAsync(runtime);
        using var client = app.GetTestClient();

        var modules = await client.GetFromJsonAsync<BrainModuleDescriptor[]>("/v2/modules", cancellationToken);
        var operations = await client.GetFromJsonAsync<BrainOperationDescriptor[]>("/v2/operations", cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/v2/operations/Proof.Run@1:invoke")
        {
            Content = JsonContent.Create(new { input = new { value = "hello" } }),
        };
        request.Headers.Add("Idempotency-Key", "request-1");
        request.Headers.Add("X-DigitalBrain-Workspace", "attacker-workspace");
        request.Headers.Add("X-DigitalBrain-Principal", "attacker-principal");
        var invoked = await client.SendAsync(request, cancellationToken);
        var activity = await client.GetFromJsonAsync<BrainActivitySnapshot>(
            $"/v2/activities/{activityId:N}", cancellationToken);
        var journal = await client.GetFromJsonAsync<BrainJournalPage>(
            $"/v2/activities/{activityId:N}/journal", cancellationToken);
        var brain = await client.GetFromJsonAsync<BrainSnapshot>("/v2/brain", cancellationToken);
        var events = await client.GetStringAsync(
            $"/v2/activities/{activityId:N}/events",
            cancellationToken);
        var journalEvents = await client.GetStringAsync(
            $"/v2/activities/{activityId:N}/journal/events",
            cancellationToken);

        Assert.Contains(modules!, module => module.Id == "proof");
        Assert.Contains(operations!, operation => operation.Id == "Proof.Run@1");
        Assert.Equal(HttpStatusCode.Accepted, invoked.StatusCode);
        Assert.Equal("Proof.Run@1", runtime.LastInvocation?.OperationId);
        Assert.Equal("request-1", runtime.LastInvocation?.IdempotencyKey);
        Assert.Equal("local", runtime.LastInvocation?.WorkspaceId);
        Assert.Equal(ActivityStatus.Completed, activity?.Status);
        Assert.Equal("ProofProduced@1", Assert.Single(journal!.Records).ContractId);
        Assert.Equal(1, Assert.Single(brain!.Synapses).UsageCount);
        Assert.Contains("event: activity", events, StringComparison.Ordinal);
        Assert.Contains("id: 3", events, StringComparison.Ordinal);
        Assert.Contains("event: journal", journalEvents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invocation_requires_an_idempotency_key()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await StartAsync(new FakeProductRuntimeClient(Guid.NewGuid()));
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/v2/operations/Proof.Run@1:invoke",
            new { input = new { value = "hello" } },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Chat_endpoint_returns_the_assistant_turn_and_durable_activity_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var activityId = Guid.NewGuid();
        var runtime = new FakeProductRuntimeClient(activityId);
        await using var app = await StartAsync(runtime);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v2/chat")
        {
            Content = JsonContent.Create(new ChatSendInput("wire and run live graph")),
        };
        request.Headers.Add("Idempotency-Key", "chat-http-1");

        var response = await client.SendAsync(request, cancellationToken);
        var chat = await response.Content.ReadFromJsonAsync<ChatTurnEnvelope>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(activityId, chat?.ActivityId);
        Assert.Equal("Proof completed.", chat?.Turn.Response);
        Assert.Equal("Chat.Send@1", runtime.LastInvocation?.OperationId);
    }

    [Fact]
    public async Task Mcp_tools_expose_the_same_journal_and_BrainGraph_projections()
    {
        var activityId = Guid.NewGuid();
        var tools = new ProductMcpTools(new FakeProductRuntimeClient(activityId));
        var cancellationToken = TestContext.Current.CancellationToken;

        var journal = await tools.GetActivityJournalAsync(
            activityId,
            cancellationToken: cancellationToken);
        var graph = await tools.GetBrainSnapshotAsync(cancellationToken);
        var chat = await tools.ChatAsync("wire and run", "chat-mcp-1", cancellationToken);

        Assert.Equal("ProofProduced@1", Assert.Single(journal.Records).ContractId);
        Assert.Equal(1, Assert.Single(graph.Synapses).UsageCount);
        Assert.Equal(activityId, chat.ActivityId);
        Assert.Equal("Proof completed.", chat.Turn.Response);
    }

    private static async Task<WebApplication> StartAsync(IProductRuntimeClient runtime)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(runtime);
        var app = builder.Build();
        app.MapProductProtocol();
        await app.StartAsync();
        return app;
    }

    private sealed class FakeProductRuntimeClient(Guid activity) : IProductRuntimeClient
    {
        public BrainOperationInvocation? LastInvocation { get; private set; }

        public Task<IReadOnlyList<BrainModuleDescriptor>> GetModulesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<BrainModuleDescriptor>>(
                [new("proof", "Proof", "ready")]);

        public Task<IReadOnlyList<BrainOperationDescriptor>> GetOperationsAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<BrainOperationDescriptor>>(
                [new("Proof.Run@1", "proof", "Run", "{}", "{}")]);

        public Task<BrainActivityReceipt> InvokeAsync(
            BrainOperationInvocation invocation,
            CancellationToken cancellationToken)
        {
            LastInvocation = invocation;
            return Task.FromResult(new BrainActivityReceipt(activity, invocation.OperationId));
        }

        public Task<BrainActivitySnapshot?> GetActivityAsync(
            Guid requested,
            string workspace,
            CancellationToken cancellationToken)
            => Task.FromResult<BrainActivitySnapshot?>(requested == activity && workspace == "local"
                ? new BrainActivitySnapshot(
                    activity,
                    LastInvocation?.OperationId ?? "Proof.Run@1",
                    "local",
                    ActivityStatus.Completed,
                    3,
                    LastInvocation?.OperationId == "Chat.Send@1"
                        ? JsonSerializer.Serialize(new ChatTurnResult(
                            "Proof completed.",
                            [new ChatToolResult("Proof.Run@1", "{\"route\":\"proof/hello\"}")]))
                        : "{\"route\":\"proof/hello\"}",
                    null)
                : null);

        public Task<BrainJournalPage> GetJournalAsync(
            Guid requested,
            string workspace,
            long afterSequence,
            int take,
            CancellationToken cancellationToken)
        {
            var record = new BrainJournalRecord(
                3,
                Guid.NewGuid(),
                workspace,
                requested,
                "owner",
                "proof/source/workspace",
                BrainJournalDirection.Outbound,
                "ProofProduced@1",
                Guid.NewGuid(),
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                1,
                "emitted",
                "Proof produced");
            var records = afterSequence < 3 ? new[] { record } : [];
            return Task.FromResult(new BrainJournalPage(
                workspace,
                requested,
                afterSequence,
                records.Length == 0 ? afterSequence : 3,
                records,
                false));
        }

        public Task<BrainSnapshot> GetBrainAsync(
            string workspace,
            CancellationToken cancellationToken)
            => Task.FromResult(new BrainSnapshot(
                workspace,
                2,
                DateTimeOffset.UtcNow,
                [
                    new BrainNeuronView("proof/source/workspace", "proof", "source", "workspace", 1),
                    new BrainNeuronView("proof/assessment/workspace", "proof", "assessment", "workspace", 0),
                ],
                [new BrainSynapseView(
                    Guid.NewGuid(),
                    1,
                    "proof/source/workspace",
                    "proof/assessment/workspace",
                    "ProofProduced@1",
                    "ProofProduced@1",
                    "live",
                    1,
                    activity)]));
    }
}
