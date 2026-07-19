using System.Net;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ProviderAdapterContracts
{
    [Fact]
    public async Task OpenAiAdapterTalksToTheDeclaredEndpoint()
    {
        const string Body = """
        {
          "id": "chatcmpl-probe",
          "object": "chat.completion",
          "created": 1,
          "model": "probe-model",
          "choices": [
            { "index": 0, "message": { "role": "assistant", "content": "answered over http" }, "finish_reason": "stop" }
          ]
        }
        """;

        using var server = new CannedHttpServer(Body);

        var model = ProviderFactory.Create(new ModelDescriptor(ModelTier.Fast, ModelDescriptor.OpenAiProvider, "probe-model")
        {
            ApiKey = "synthetic-key",
            Endpoint = server.Address,
        });

        using (model)
        {
            var answer = await model.GetResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "ping")],
                options: null,
                TestContext.Current.CancellationToken);

            Assert.Equal("answered over http", answer.Text);
        }

        Assert.Contains("/chat/completions", server.LastPath, StringComparison.Ordinal);
        Assert.Equal("Bearer synthetic-key", server.LastAuthorization);
    }

    [Fact]
    public async Task AnthropicAdapterTalksToTheDeclaredEndpoint()
    {
        const string Body = """
        {
          "id": "msg_probe",
          "type": "message",
          "role": "assistant",
          "model": "probe-model",
          "content": [ { "type": "text", "text": "answered over http" } ],
          "stop_reason": "end_turn",
          "usage": { "input_tokens": 1, "output_tokens": 1 }
        }
        """;

        using var server = new CannedHttpServer(Body);

        var model = ProviderFactory.Create(new ModelDescriptor(ModelTier.Fast, ModelDescriptor.AnthropicProvider, "probe-model")
        {
            ApiKey = "synthetic-key",
            Endpoint = server.Address,
        });

        using (model)
        {
            var answer = await model.GetResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "ping")],
                options: null,
                TestContext.Current.CancellationToken);

            Assert.Equal("answered over http", answer.Text);
        }

        Assert.Contains("/messages", server.LastPath, StringComparison.Ordinal);
    }

    [Fact]
    public void ADescriptorNeverPrintsItsCredential()
    {
        var descriptor = new ModelDescriptor(ModelTier.Fast, ModelDescriptor.OpenAiProvider, "small")
        {
            ApiKey = "sk-live-should-never-be-printed",
        };

        var printed = descriptor.ToString();

        Assert.DoesNotContain("sk-live-should-never-be-printed", printed, StringComparison.Ordinal);
        Assert.Contains("small", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredTiersResolveByTierKeyAndUndeclaredOnesDoNot()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        services.AddDigitalBrainModels(catalog => catalog.Declare(
            new ModelDescriptor(ModelTier.Fast, ModelDescriptor.OpenAiProvider, "small")
            {
                ApiKey = "synthetic-key",
                Endpoint = new Uri("http://127.0.0.1:1/v1"),
            }));

        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);

        Assert.NotNull(Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions
            .GetKeyedService<Microsoft.Extensions.AI.IChatClient>(provider, ModelTier.Fast));
        Assert.Null(Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions
            .GetKeyedService<Microsoft.Extensions.AI.IChatClient>(provider, ModelTier.Reasoning));
    }

    [Fact]
    public void EachTierBindsToExactlyOneModel()
    {
        var catalog = new ModelCatalog()
            .Declare(new ModelDescriptor(ModelTier.Fast, ModelDescriptor.OpenAiProvider, "small"));

        Assert.Throws<InvalidOperationException>(
            () => catalog.Declare(new ModelDescriptor(ModelTier.Fast, ModelDescriptor.AnthropicProvider, "other")));
    }

    [Fact]
    public void AProviderWithoutCredentialsIsRejectedBeforeAnyCallIsMade()
        => Assert.Throws<InvalidOperationException>(
            () => ProviderFactory.Create(new ModelDescriptor(ModelTier.Fast, ModelDescriptor.OpenAiProvider, "small")));

    [Fact]
    public void AnUnknownProviderIsRejected()
        => Assert.Throws<InvalidOperationException>(
            () => ProviderFactory.Create(new ModelDescriptor(ModelTier.Fast, "smoke-signals", "small") { ApiKey = "k" }));

    private sealed class CannedHttpServer : IDisposable
    {
        private readonly HttpListener _listener = new();

        internal CannedHttpServer(string body)
        {
            var port = FreePort();

            Address = new Uri($"http://127.0.0.1:{port}");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();

            _ = ServeAsync(body);
        }

        internal Uri Address { get; }

        internal string LastPath { get; private set; } = string.Empty;

        internal string LastAuthorization { get; private set; } = string.Empty;

        public void Dispose() => ((IDisposable)_listener).Dispose();

        private static int FreePort()
        {
            using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);

            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            return port;
        }

        private async Task ServeAsync(string body)
        {
            var context = await _listener.GetContextAsync();

            LastPath = context.Request.Url?.AbsolutePath ?? string.Empty;
            LastAuthorization = context.Request.Headers["Authorization"] ?? string.Empty;

            var payload = Encoding.UTF8.GetBytes(body);

            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = payload.Length;

            await context.Response.OutputStream.WriteAsync(payload);
            context.Response.Close();
        }
    }
}
