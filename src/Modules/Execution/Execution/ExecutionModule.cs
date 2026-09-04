using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Execution;

public sealed class ExecutionModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IExecutionContextProvider, PreferenceContextProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IExecutionContextProvider, TranscriptContextProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IExecutionContextProvider, RelatedExecutionProvider>());
    }
}
