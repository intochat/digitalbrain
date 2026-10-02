using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Sdk.Integrations;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Modules.AI.Tests.Unit;

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
        Assert.Equal(["ApiKey"], unavailable.Missing);
        Assert.DoesNotContain("Exception", unavailable.Message);
    }

    [Fact]
    public async Task ASeededProviderKeyIsReleasedOnlyInsideTheFactory()
    {
        var ct = TestContext.Current.CancellationToken;
        using var provider = new LoopbackServer();
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

        var seen = ReplyOnceCapturingKey(provider, ct);
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
    public async Task SeedingOnlyTheApiKeyIsReadyAndTheDefaultEndpointIsUsed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create()
            .WithRegistrations(new TestExecutionOptions
            {
                PrivateConfiguration = new Dictionary<string, string?> { ["DigitalBrain:Integrations:openai:ApiKey"] = Canary },
            })
            .WithModule<AIModule>().StartAsync(ct);

        var snapshot = await brain.Get<IIntegrationRegistration>("integration/openai").Read();
        var credentials = brain.SiloServices.GetRequiredService<IAiCredentials>();
        using var client = new OpenAIProviderFactory().CreateChatClient("test-model", new AIOptions(), credentials);

        Assert.Equal(RegistrationStatus.Ready, snapshot.Status);
        Assert.DoesNotContain("Endpoint", snapshot.Settings.Keys);
        Assert.Equal(AiIntegrations.DefaultEndpointOf(AiProvider.OpenAI), ((ChatClientMetadata)client.GetService(typeof(ChatClientMetadata))!).ProviderUri!.ToString());
    }

    [Fact]
    public async Task AClearedOrRotatedRegistrationTakesEffectOnTheLiveClientWithoutARestart()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Rotated = "canary-rotated-key-2c9e4d";
        var clock = new SteppingTimeProvider();
        using var provider = new LoopbackServer();
        await using var brain = await UnitTest.Create()
            .WithRegistrations(AiRegistrationSeeds.OpenAI(Canary, provider.Url))
            .WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<TimeProvider>(clock)).StartAsync(ct);
        var registration = brain.Get<IIntegrationRegistration>("integration/openai");
        var client = brain.SiloServices.GetRequiredKeyedService<IChatClient>(typeof(IGpt56Sol));
        var hi = new[] { new ChatMessage(ChatRole.User, "hi") };

        var first = ReplyOnceCapturingKey(provider, ct);
        await client.GetResponseAsync(hi, cancellationToken: ct);
        Assert.Equal("Bearer " + Canary, await first);

        await registration.Clear("ApiKey");
        clock.Advance(TimeSpan.FromSeconds(6));
        var unavailable = await Assert.ThrowsAsync<ProviderUnavailableException>(() => client.GetResponseAsync(hi, cancellationToken: ct));
        Assert.Equal(["ApiKey"], unavailable.Missing);

        await registration.Configure(new() { Values = { ["ApiKey"] = Rotated } });
        clock.Advance(TimeSpan.FromSeconds(6));
        var second = ReplyOnceCapturingKey(provider, ct);
        await client.GetResponseAsync(hi, cancellationToken: ct);
        Assert.Equal("Bearer " + Rotated, await second);
    }

    [Fact]
    public async Task RefreshesWithoutARegistrationChangeNeverInvalidateReleasedKeysButRotationDoes()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new SteppingTimeProvider();
        await using var brain = await UnitTest.Create()
            .WithRegistrations(AiRegistrationSeeds.OpenAI(Canary))
            .WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<TimeProvider>(clock)).StartAsync(ct);
        var credentials = brain.SiloServices.GetRequiredService<IAiCredentials>();

        var seeded = credentials.GenerationOf("openai");
        clock.Advance(TimeSpan.FromSeconds(6));
        var refreshed = credentials.GenerationOf("openai");
        clock.Advance(TimeSpan.FromSeconds(6));
        var refreshedAgain = credentials.GenerationOf("openai");
        await brain.Get<IIntegrationRegistration>("integration/openai").Configure(new() { Values = { ["ApiKey"] = "canary-rotated-in-place" } });
        clock.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal(seeded, refreshed);
        Assert.Equal(seeded, refreshedAgain);
        Assert.NotEqual(seeded, credentials.GenerationOf("openai"));
    }

    [Fact]
    public async Task AProviderRefusalNeverLeaksTheKeyIntoErrorsOrLogs()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new CapturingLoggerProvider();
        var clock = new SteppingTimeProvider();
        using var provider = new LoopbackServer();
        await using var brain = await UnitTest.Create()
            .WithRegistrations(AiRegistrationSeeds.OpenAI(Canary, provider.Url))
            .WithModule<AIModule>()
            .ConfigureSilo(silo =>
            {
                silo.Services.AddLogging(logging => logging.AddProvider(logs));
                silo.Services.AddSingleton<TimeProvider>(clock);
                silo.Services.Configure<AIOptions>(options =>
                {
                    options.Default.Provider = "OpenAI";
                    options.Default.Model = "test-model";
                    options.Default.Capabilities = LlmCapabilities.Tools;
                });
            }).StartAsync(ct);
        var registration = brain.Get<IIntegrationRegistration>("integration/openai");

        var refused = ReplyOnceCapturingKey(provider, ct, HttpStatusCode.Unauthorized);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => brain.Get<ILLM>("default").Generate(new([new("user", [new AiText("hi")])]), cancellationToken: ct));
        Assert.Equal("Bearer " + Canary, await refused);
        await registration.Clear("ApiKey");
        clock.Advance(TimeSpan.FromSeconds(6));
        var unavailable = await Assert.ThrowsAsync<ProviderUnavailableException>(() => brain.Get<IGpt56Sol>("cleared").Describe());

        Assert.DoesNotContain(Canary, failure.ToString());
        Assert.DoesNotContain(Canary, unavailable.ToString());
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(new { unavailable.Integration, unavailable.Status, unavailable.Missing }));
        Assert.NotEmpty(logs.Text);
        Assert.DoesNotContain(Canary, logs.Text);
    }

    private sealed class SteppingTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

        public string Text => string.Join(Environment.NewLine, _lines);

        public ILogger CreateLogger(string categoryName) => new Sink(_lines);

        public void Dispose() { }

        private sealed class Sink(System.Collections.Concurrent.ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => lines.Enqueue(formatter(state, exception) + exception);
        }
    }

    private const string ChatCompletionJson = """
        {"id":"r","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
        """;

    private static async Task<string?> ReplyOnceCapturingKey(LoopbackServer provider, CancellationToken cancellationToken, HttpStatusCode status = HttpStatusCode.OK)
        => (await provider.ReplyOnce("application/json", ChatCompletionJson, cancellationToken, status)).Authorization;
}
