using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Sdk.Integrations;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CredentialFacts
{
    private const string Canary = "canary-provider-key-71f0b3";

    [Fact]
    public async Task AnUnregisteredProviderReportsUnavailableInsteadOfThrowing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithRegistrations().WithModule<AIModule>().StartAsync(ct);

        var catalogRow = await brain.Get<IIntegrationRegistration>("integration/openai").Read();
        var factory = new OpenAIProviderFactory();
        var credentials = brain.SiloServices.GetRequiredService<IAiCredentials>();
        var unavailable = await Assert.ThrowsAsync<ProviderUnavailableException>(() => brain.Get<IGpt56Sol>("unregistered").Describe());

        Assert.Equal(RegistrationStatus.Unconfigured, catalogRow.Status);
        Assert.False(factory.IsConfigured(new AIOptions(), credentials));
        Assert.Equal("openai", unavailable.Integration);
        Assert.Equal("Unconfigured", unavailable.Status);
        Assert.Equal(["ApiKey", "Endpoint"], unavailable.Missing);
        Assert.DoesNotContain("Exception", unavailable.Message);
    }

    [Fact]
    public async Task ASeededProviderKeyIsReleasedOnlyInsideTheFactory()
    {
        var ct = TestContext.Current.CancellationToken;
        using var provider = new KeyCapturingProvider();
        await using var brain = await UnitTest.Create()
            .WithRegistrations(AiRegistrationSeeds.OpenAI(Canary, provider.Url))
            .WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.Configure<AIOptions>(options =>
            {
                options.Default.Provider = "OpenAI";
                options.Default.Model = "test-model";
                options.Default.Capabilities = LlmCapabilities.Tools;
            })).StartAsync(ct);
        var registration = brain.Get<IIntegrationRegistration>("integration/openai");
        await using var changes = await brain.Observe<RegistrationChanged>(registration, ct);

        var seen = provider.ReplyOnce(ct);
        await brain.Get<ILLM>("default").Generate(new([new("user", [new AiText("hi")])]), cancellationToken: ct);

        Assert.Equal("Bearer " + Canary, await seen);
        var snapshot = await registration.Read();
        Assert.Equal(RegistrationStatus.Ready, snapshot.Status);
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(snapshot));
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(await registration.Configure(new() { Values = { ["Endpoint"] = provider.Url } })));
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(await changes.NextAsync(ct: ct)));
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(brain.SiloServices.GetRequiredService<IOptions<AIOptions>>().Value));
    }

    [Fact]
    public async Task LegacyProviderKeysSeedTheRegistrationWithTheDefaultEndpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create()
            .WithRegistrations(new TestExecutionOptions
            {
                PrivateConfiguration = new Dictionary<string, string?>
                {
                    ["DigitalBrain:AI:Anthropic:ApiKey"] = Canary,
                    ["DigitalBrain:AI:Tavily:ApiKey"] = Canary,
                },
            })
            .WithModule<AIModule>().StartAsync(ct);

        var anthropic = await brain.Get<IIntegrationRegistration>("integration/anthropic").Read();
        var tavily = await brain.Get<IIntegrationRegistration>("integration/tavily").Read();

        Assert.Equal(RegistrationStatus.Ready, anthropic.Status);
        Assert.Equal("https://api.anthropic.com", anthropic.Settings["Endpoint"]);
        Assert.Equal(RegistrationStatus.Ready, tavily.Status);
        Assert.Equal(RegistrationStatus.Unconfigured, (await brain.Get<IIntegrationRegistration>("integration/openai").Read()).Status);
    }

    private sealed class KeyCapturingProvider : IDisposable
    {
        private readonly HttpListener _listener = new();

        public KeyCapturingProvider()
        {
            using var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            Url = $"http://localhost:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
        }

        public string Url { get; }

        public async Task<string?> ReplyOnce(CancellationToken cancellationToken)
        {
            var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
            var authorization = context.Request.Headers["Authorization"];
            var bytes = Encoding.UTF8.GetBytes("""
                {"id":"r","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
                """);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            context.Response.Close();
            return authorization;
        }

        public void Dispose() => _listener.Close();
    }
}
