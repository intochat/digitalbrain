using DigitalBrain.Sdk.Integrations;
using DigitalBrain.Platform.Integrations;
using DigitalBrain.Sdk.Secrets;
using DigitalBrain.Platform.Secrets;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Unit;

namespace DigitalBrain.Modules.AI.Tests.Unit;

internal static class AiRegistrationSeeds
{
    internal const string OpenAIDefaultEndpoint = "https://api.openai.com/v1";

    internal static TestExecutionOptions OpenAI(string apiKey = "test-only", string endpoint = OpenAIDefaultEndpoint)
        => new()
        {
            PrivateConfiguration = new Dictionary<string, string?>
            {
                ["DigitalBrain:Integrations:openai:ApiKey"] = apiKey,
                ["DigitalBrain:Integrations:openai:Endpoint"] = endpoint,
            },
        };

    internal static UnitTestBuilder WithRegistrations(this UnitTestBuilder builder, TestExecutionOptions? seeds = null)
        => builder.WithExecution(seeds ?? new TestExecutionOptions()).WithModule<SecretsModule>().WithModule<IntegrationsModule>();
}
