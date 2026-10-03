using DigitalBrain.AI;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Sdk.Integrations;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.AI.Tests.Unit;

public sealed class CapabilityFacts
{
    private static readonly CallerContext Caller = new()
    {
        PrincipalId = "owner-1",
        AccountId = "owner-1",
        BrainId = "brain-1",
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    };

    [Fact]
    public async Task CapabilitiesListOnlyReadyProvidersModels()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create()
            .WithRegistrations(AiRegistrationSeeds.OpenAI())
            .WithModule<AIModule>().StartAsync(ct);
        var capabilities = brain.SiloServices.GetRequiredService<ICapabilities>();

        var first = await capabilities.List(Caller, "llm", ct);

        Assert.NotEmpty(first);
        Assert.All(first, capability => Assert.Equal("openai", capability.IntegrationId));
        Assert.All(first, capability => Assert.Equal("llm", capability.Kind));

        await brain.Get<IIntegrationRegistration>("integration/anthropic")
            .Configure(new() { Values = { ["ApiKey"] = "test-only", ["Endpoint"] = "https://api.anthropic.com" } });
        var second = await capabilities.List(Caller, "llm", ct);

        Assert.Contains(second, capability => capability.IntegrationId == "anthropic");
        Assert.Contains(second, capability => capability.IntegrationId == "openai");
        Assert.Empty(await capabilities.List(Caller, "tts", ct));
        Assert.DoesNotContain("test-only", System.Text.Json.JsonSerializer.Serialize(second));
    }
}
