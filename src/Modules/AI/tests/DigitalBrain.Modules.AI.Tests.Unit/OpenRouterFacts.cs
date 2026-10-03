using DigitalBrain.AI;
using DigitalBrain.AI.OpenRouter;
using DigitalBrain.Platform.Contracts.Integrations;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.AI.Tests.Unit;

public sealed class OpenRouterFacts
{
    [Fact]
    public async Task DeepSeekPresetUsesRegisteredOpenRouterCredentialsAndDefaultEndpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithRegistrations(new TestExecutionOptions
        {
            PrivateConfiguration = new Dictionary<string, string?>
            { ["DigitalBrain:Integrations:openrouter:ApiKey"] = "test-only" },
        }).WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.Configure<AIOptions>(options =>
                options.Default.Model = nameof(IDeepSeekV41Flash))).StartAsync(ct);

        var resolved = brain.SiloServices.GetRequiredService<ModelProfiles>().Resolve(null, requiresTools: true);
        Assert.Equal("OpenRouter", resolved.Provider);
        Assert.Equal("deepseek/deepseek-v4.1-flash", resolved.Model);
        Assert.Equal("https://openrouter.ai/api/v1", resolved.Endpoint);
        Assert.Equal(RegistrationStatus.Ready,
            (await brain.Get<IIntegrationRegistration>("integration/openrouter").Read()).Status);
        await brain.Get<IDeepSeekV41Flash>("preset").Describe();
    }
}
