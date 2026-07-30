using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Memory;

public sealed partial class MemoryModule : IModule
{
    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        builder.Services.AddSingleton<IVectorMemoryStore, InMemoryVectorMemoryStore>();
    }
}
