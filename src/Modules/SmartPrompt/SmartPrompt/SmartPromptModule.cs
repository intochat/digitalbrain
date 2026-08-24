using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.SmartPrompt;

public sealed class SmartPromptModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IBehaviorCompiler>(static _ => BehaviorCompiler.CreateDefault());
        builder.Services.AddSingleton<IBehaviorReasoner, GemmaBehaviorReasoner>();
        builder.Services.AddSingleton<IBehaviorActionExecutor, BehaviorActionExecutor>();
        builder.AddStartupTask<DefaultSmartPromptStartupTask>();
    }
}
