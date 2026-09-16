using DigitalBrain.Aspire;
using DigitalBrain.Core;
using Microsoft.Extensions.Hosting;

namespace IntoChat;

internal static class IntoChatHost
{
    internal static IHostApplicationBuilder AddDigitalBrain(this IHostApplicationBuilder builder)
        => builder.AddDigitalBrain(ModuleManifest.FromConfiguration(builder.Configuration));
}
