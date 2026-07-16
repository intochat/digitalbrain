using Brain.Kernel;
using Brain.Modules.Ai;
using Brain.Modules.Sdk;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace Brain.KernelTests;

public sealed class AiKindsConfigurator : ISiloConfigurator
{
    public static FakeChatClient Client { get; } = new("fake-reply");

    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddBrainKernel();
        siloBuilder.AddBrainKind("llm", sp => new LlmKind(
            new ModelCatalog([
                new ModelBinding(ModelTier.Fast, "ollama", "fake"),
                new ModelBinding(ModelTier.Balanced, "ollama", "fake"),
                new ModelBinding(ModelTier.Reasoning, "ollama", "fake")
            ]),
            sp));
        siloBuilder.Services.AddKeyedSingleton<IChatClient>("ollama", Client);
    }
}
