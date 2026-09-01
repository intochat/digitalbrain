using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.SmartPrompt;

public sealed class SmartPromptModule : IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<ICapabilityHandler, WebSearchHandler>();
        if (DigitalBrainFakes.Enabled(builder.Configuration))
        {
            builder.Services.AddSingleton<IWebSearch, FakeWebSearch>();
        }
        else
        {
            builder.Services.AddSingleton<IWebSearch, NotImplementedWebSearch>();
        }

        builder.Services.AddSingleton<IBehaviorCompiler>(static _ => BehaviorCompiler.CreateDefault());
        builder.Services.AddSingleton<IBehaviorReasoner, BehaviorReasoner>();
        builder.Services.AddSingleton<IBehaviorActionExecutor, BehaviorActionExecutor>();
        builder.Services.AddSingleton<IBehaviorFeatureGenerator, BehaviorFeatureGenerator>();
        builder.Services.AddSingleton<IAgentToolSource, BehaviorToolSource>();
        builder.AddStartupTask<DefaultSmartPromptStartupTask>();
        builder.AddStartupTask<DefaultBehaviorStartupTask>();
    }
}
