using DigitalBrain.Platform.Identity;
using DigitalBrain.Platform.Identity.Directory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
namespace DigitalBrain.Platform;

public static class PlatformApplicationHosting
{
    public static IHostApplicationBuilder AddDigitalBrainPlatform(this IHostApplicationBuilder builder)
    {
        // Maintenance inspection has no HTTP surface and must not provision cookie storage.
        if (!builder.Configuration.GetValue<bool>(IdentityMigrationOptions.SectionName + ":Maintenance"))
        { builder.Services.AddCookieProtection(); }
        builder.UseOrleans(silo => silo.AddDigitalBrainPlatform());
        return builder;
    }
}
