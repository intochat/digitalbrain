using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Execution;

public sealed class ExecutionModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<EffectBroker>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IExecutionContextProvider, PreferenceContextProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IExecutionContextProvider, TranscriptContextProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IExecutionContextProvider, RelatedExecutionProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICapabilityHandler, ExplainabilityHandler>());

        if (UseAllowListedScriptDriver(builder.Configuration))
        {
            builder.Services.TryAddSingleton<IScriptDriver, InProcessAllowListedScriptDriver>();
        }
        else
        {
            builder.Services.TryAddSingleton<IScriptDriver, NotImplementedScriptDriver>();
        }
    }

    private static bool UseAllowListedScriptDriver(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        if (string.Equals(
                configuration[DigitalBrainNames.Mode],
                DigitalBrainNames.TestingMode,
                StringComparison.Ordinal))
        {
            return true;
        }

        var fakes = configuration[DigitalBrainNames.Fakes];
        return string.Equals(fakes, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fakes, "1", StringComparison.OrdinalIgnoreCase);
    }
}
