using System.Net;
using System.Text.Json;
using DigitalBrain.Client;
using DigitalBrain.Invocation;
using DigitalBrain.Rules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Runtime.Http.Tests.Integration.Edge;

public sealed class RuntimeEdgeClientFacts
{
    private const string BrainId = "test-brain";
    private static readonly byte[] SecretKeyBytes = "integration-test-secret-key-32b!"u8.ToArray();

    public interface ITestCalculatorNeuron
    {
        Task<int> AddAsync(int a, int b);
        Task<string> GreetAsync(string name);
    }

    private sealed class TestDispatcher : IRuntimeInvocationDispatcher
    {
        public Task<JsonElement?> DispatchAsync(string brainId, RuntimeInvocation invocation, CancellationToken cancellationToken)
        {
            if (invocation.Method == "RefusedMethod")
            {
                throw new RuleViolationException(new Rejected("RefusedMethod", ["DisallowedRule"], ["RefusedMethod is not allowed by policy."]));
            }

            if (invocation.Method == "AddAsync")
            {
                var a = invocation.Arguments[0].GetInt32();
                var b = invocation.Arguments[1].GetInt32();
                var result = a + b;
                var doc = JsonDocument.Parse(JsonSerializer.Serialize(result, RuntimeProtocol.Json));
                return Task.FromResult<JsonElement?>(doc.RootElement.Clone());
            }

            if (invocation.Method == "GreetAsync")
            {
                var name = invocation.Arguments[0].GetString();
                var result = $"Hello, {name}!";
                var doc = JsonDocument.Parse(JsonSerializer.Serialize(result, RuntimeProtocol.Json));
                return Task.FromResult<JsonElement?>(doc.RootElement.Clone());
            }

            throw new NotSupportedException($"Unknown method: {invocation.Method}");
        }
    }

    private sealed class TestServer : IAsyncDisposable
    {
        public WebApplication App { get; }
        public Uri EdgeUri { get; }

        private TestServer(WebApplication app, Uri edgeUri)
        {
            App = app;
            EdgeUri = edgeUri;
        }

        public static async Task<TestServer> StartAsync(IRuntimeInvocationDispatcher dispatcher, Action<RuntimeEdgeOptions>? configureOptions = null)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            builder.Services.AddSingleton(dispatcher);
            builder.Services.AddRuntimeEdge(options =>
            {
                options.SecretKey = SecretKeyBytes;
                options.TokenLifetime = TimeSpan.FromMinutes(10);
                configureOptions?.Invoke(options);
            });

            var app = builder.Build();
            app.MapRuntimeEdge();
            await app.StartAsync(TestContext.Current.CancellationToken);

            var address = app.Urls.First();
            var edgeUri = new Uri($"{address}/brains/{BrainId}/");
            return new TestServer(app, edgeUri);
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    [Fact]
    public async Task AClientWithAValidSessionTokenInvokesAnAllowedNeuronMethodOverHttp()
    {
        var ct = TestContext.Current.CancellationToken;
        var dispatcher = new TestDispatcher();
        await using var server = await TestServer.StartAsync(dispatcher);

        var token = SessionTokens.Issue(SecretKeyBytes, BrainId, "session-1", TimeSpan.FromMinutes(5));
        await using var connection = await DigitalBrainConnection.OpenAsync(
            server.EdgeUri,
            token,
            behavior: "test-behavior",
            handler: null,
            cancellationToken: ct);

        var calc = connection.Neuron<ITestCalculatorNeuron>("calculator");
        var result = await calc.AddAsync(40, 2);
        Assert.Equal(42, result);

        var greeting = await calc.GreetAsync("World");
        Assert.Equal("Hello, World!", greeting);
    }

    [Fact]
    public async Task AClientWithAnExpiredOrTamperedSessionTokenIsRejectedWith401()
    {
        var ct = TestContext.Current.CancellationToken;
        var dispatcher = new TestDispatcher();
        await using var server = await TestServer.StartAsync(dispatcher);

        // 1. Expired token
        var expiredToken = SessionTokens.Issue(SecretKeyBytes, BrainId, "session-1", TimeSpan.FromSeconds(-10));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await using var conn = await DigitalBrainConnection.OpenAsync(
                server.EdgeUri,
                expiredToken,
                behavior: "test-behavior",
                handler: null,
                cancellationToken: ct);
        });

        // 2. Tampered token
        var validToken = SessionTokens.Issue(SecretKeyBytes, BrainId, "session-1", TimeSpan.FromMinutes(5));
        var tamperedToken = validToken[..^4] + "xxxx";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await using var conn = await DigitalBrainConnection.OpenAsync(
                server.EdgeUri,
                tamperedToken,
                behavior: "test-behavior",
                handler: null,
                cancellationToken: ct);
        });
    }

    public interface IRefusalNeuron
    {
        Task<int> RefusedMethod();
    }

    [Fact]
    public async Task AClientCallRefusedByTheDispatcherPropagatesRuleViolationException()
    {
        var ct = TestContext.Current.CancellationToken;
        var dispatcher = new TestDispatcher();
        await using var server = await TestServer.StartAsync(dispatcher);

        var token = SessionTokens.Issue(SecretKeyBytes, BrainId, "session-1", TimeSpan.FromMinutes(5));
        await using var connection = await DigitalBrainConnection.OpenAsync(
            server.EdgeUri,
            token,
            behavior: "test-behavior",
            handler: null,
            cancellationToken: ct);

        var neuron = connection.Neuron<IRefusalNeuron>("policy-gated");
        var ex = await Assert.ThrowsAsync<RuleViolationException>(async () =>
        {
            await neuron.RefusedMethod();
        });

        Assert.Equal("RuleViolationException", ex.Rejected.Rules[0]);
        Assert.Contains("RefusedMethod is not allowed by policy.", ex.Message);
    }
}
