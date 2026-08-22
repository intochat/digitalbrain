using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.UI;

public sealed class UIModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.Equals(
                builder.Configuration[DigitalBrainNames.Mode],
                DigitalBrainNames.TestingMode,
                StringComparison.Ordinal))
        {
            builder.Services.TryAddSingleton<IKitImageStore, MemoryKitImageStore>();
        }
        else
        {
            builder.Services.TryAddSingleton<IKitImageStore, BlobKitImageStore>();
        }
    }
}
