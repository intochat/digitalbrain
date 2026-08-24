using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.AI;

namespace DigitalBrain.SmartPrompt;

public sealed class SmartPromptModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IBehaviorCompiler>(static _ => BehaviorCompiler.CreateDefault());
        builder.Services.AddSingleton<IBehaviorReasoner, GemmaBehaviorReasoner>();
        builder.Services.AddSingleton<IBehaviorActionExecutor, BehaviorActionExecutor>();
        builder.Services.AddSingleton<IBehaviorFeatureGenerator, BehaviorFeatureGenerator>();
        builder.Services.AddSingleton<IAgentToolSource, BehaviorToolSource>();
        builder.AddStartupTask<DefaultSmartPromptStartupTask>();
        builder.AddStartupTask<DefaultBehaviorStartupTask>();
    }
}
