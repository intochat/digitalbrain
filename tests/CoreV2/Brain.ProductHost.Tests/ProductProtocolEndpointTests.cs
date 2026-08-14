using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Brain.Runtime.Abstractions;
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

        var modules = await client.GetFromJsonAsync<RuntimeModuleDescriptor[]>("/v2/modules", cancellationToken);
        var operations = await client.GetFromJsonAsync<RuntimeOperationDescriptor[]>("/v2/operations", cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/v2/operations/proof%2Frun@1:invoke")
        {
            Content = JsonContent.Create(new { input = new { value = "hello" } }),
        };
        request.Headers.Add("Idempotency-Key", "request-1");
        request.Headers.Add("X-DigitalBrain-Workspace", "attacker-workspace");
        request.Headers.Add("X-DigitalBrain-Principal", "attacker-principal");
        var invoked = await client.SendAsync(request, cancellationToken);
        var activity = await client.GetFromJsonAsync<RuntimeActivitySnapshot>(
            $"/v2/activities/{activityId:N}", cancellationToken);
        var events = await client.GetStringAsync(
            $"/v2/activities/{activityId:N}/events",
            cancellationToken);

        Assert.Contains(modules!, module => module.Id == "proof");
        Assert.Contains(operations!, operation => operation.Id == "proof/run@1");
        Assert.Equal(HttpStatusCode.Accepted, invoked.StatusCode);
        Assert.Equal("proof/run@1", runtime.LastInvocation?.OperationId);
        Assert.Equal("request-1", runtime.LastInvocation?.IdempotencyKey);
        Assert.Equal("local", runtime.LastInvocation?.Workspace);
        Assert.Equal(RuntimeActivityStatus.Completed, activity?.Status);
        Assert.Contains("event: activity", events, StringComparison.Ordinal);
        Assert.Contains("id: 3", events, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invocation_requires_an_idempotency_key()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await StartAsync(new FakeProductRuntimeClient(Guid.NewGuid()));
        using var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/v2/operations/proof%2Frun@1:invoke",
            new { input = new { value = "hello" } },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
        public RuntimeInvocation? LastInvocation { get; private set; }

        public Task<IReadOnlyList<RuntimeModuleDescriptor>> GetModulesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RuntimeModuleDescriptor>>(
                [new("proof", "Proof", RuntimeModuleStatus.Ready)]);

        public Task<IReadOnlyList<RuntimeOperationDescriptor>> GetOperationsAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RuntimeOperationDescriptor>>(
                [new("proof/run@1", "proof", "Run", "{}", "{}")]);

        public Task<RuntimeActivityReceipt> InvokeAsync(
            RuntimeInvocation invocation,
            CancellationToken cancellationToken)
        {
            LastInvocation = invocation;
            return Task.FromResult(new RuntimeActivityReceipt(activity, invocation.OperationId));
        }

        public Task<RuntimeActivitySnapshot?> GetActivityAsync(
            Guid requested,
            string workspace,
            CancellationToken cancellationToken)
            => Task.FromResult<RuntimeActivitySnapshot?>(requested == activity && workspace == "local"
                ? new RuntimeActivitySnapshot(
                    activity,
                    "proof/run@1",
                    "local",
                    RuntimeActivityStatus.Completed,
                    3,
                    "{\"route\":\"proof/hello\"}",
                    null)
                : null);
    }
}
