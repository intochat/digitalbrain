using Brain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace Brain.Modules.Flutter;

public static class FlutterRuntimeExtensions
{
    public static ISiloBuilder AddDigitalBrainFlutter(this ISiloBuilder silo)
    {
        silo.Services.AddSingleton<FlutterGatewayPolicy>();
        silo.AddBrainKind("window", _ => new WindowKind());
        return silo.AddBrainKind("feed", _ => new FeedKind());
    }
}
