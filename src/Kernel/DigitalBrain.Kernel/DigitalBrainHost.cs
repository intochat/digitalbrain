using DigitalBrain.Aspire;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Kernel;

internal static class DigitalBrainHost
{
    internal static IHostApplicationBuilder AddDigitalBrain(this IHostApplicationBuilder builder)
        => builder.AddDigitalBrain(ProductModules.Assemblies);
}
