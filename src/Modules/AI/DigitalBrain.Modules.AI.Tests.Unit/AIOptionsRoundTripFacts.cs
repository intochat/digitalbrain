using DigitalBrain.AI;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AIOptionsRoundTripFacts
{
    [Fact]
    public void PopulatedDictionariesSurviveCompileAndBindByValue()
    {
        var options = new AIOptions();
        options.ModelProfiles["Fast"] = new AIModelProfileOptions { Provider = "OpenAI", Model = "m1", ContextWindowTokens = 1000 };
        options.Ollama.Models["Gemma"] = new AIModelOptions { Model = "gemma:2b" };
        var definition = ModuleOptionsSerialization.Compile<AIModule, AIOptions>(options);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(definition.Configuration).Build();

        var bound = configuration.GetModuleOptions<AIOptions>(nameof(AIModule));

        Assert.Equal("m1", bound.ModelProfiles["fast"].Model);
        Assert.Equal(1000, bound.ModelProfiles["Fast"].ContextWindowTokens);
        Assert.Equal("gemma:2b", bound.Ollama.Models["gemma"].Model);
    }
}
