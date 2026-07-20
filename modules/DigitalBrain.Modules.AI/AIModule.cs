using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Modules.AI.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.AI;

public sealed class AIModule : IModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Id: "ai",
        Version: "0.1.0",
        DisplayName: "AI",
        Configuration: [new ConfigurationKey("DigitalBrain:Models", "application", "Tier bindings")],
        Secrets: [new SecretRequirement("provider-api-key", "Provider API key for a declared model tier")],
        Capabilities: [new CapabilityDeclaration("ai.complete", "Complete a prompt with a bound model tier")],
        Effects: [],
        Connections: []);
}

public static class AIModuleHosting
{
    public static IServiceCollection AddAIModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IModelCompletionService, ChatClientModelCompletion>();

        return services;
    }
}

internal sealed class ChatClientModelCompletion(IServiceProvider services) : IModelCompletionService
{
    public async Task<string> CompleteAsync(ModelTier tier, string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var model = services.GetKeyedService<IChatClient>(tier)
            ?? throw new InvalidOperationException(
                $"No model is bound to the {tier} tier. Bind tiers with AddDigitalBrainModels.");

        var answer = await model.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            options: null,
            cancellationToken);

        return answer.Text;
    }
}

public sealed class ChatModelNeuron : Neuron, IChatModel
{
    public Task<string> CompleteAsync(string prompt)
        => AskModelAsync(ModelTier.Balanced, prompt, CancellationToken.None);
}
