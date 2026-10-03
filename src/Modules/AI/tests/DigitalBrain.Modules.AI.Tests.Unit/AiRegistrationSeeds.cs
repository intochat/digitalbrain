using DigitalBrain.Platform.Contracts.Integrations;
using DigitalBrain.Platform.Contracts.Secrets;
using DigitalBrain.Platform.Secrets;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Module;

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

    internal static ModuleTestBuilder WithRegistrations(this ModuleTestBuilder builder, TestExecutionOptions? seeds = null)
        => builder.WithExecution(seeds ?? new TestExecutionOptions());
}
