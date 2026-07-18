using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Hosting;

public enum DigitalBrainProfile
{
    Local,
    Product,
    Production
}

public sealed class ProfileConfiguration
{
    public DigitalBrainProfile Profile { get; set; } = DigitalBrainProfile.Local;
    public bool AutostartShell { get; set; } = true;
    public bool UseRedis { get; set; } = false;

    public static ProfileConfiguration Parse(IConfiguration configuration, string[] args)
    {
        var config = new ProfileConfiguration();
        var profileStr = configuration["profile"] ?? "local";

        foreach (var arg in args)
        {
            if (arg.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase))
            {
                profileStr = arg["--profile=".Length..].ToLowerInvariant();
            }
        }

        config.Profile = profileStr switch
        {
            "product" => DigitalBrainProfile.Product,
            "production" => DigitalBrainProfile.Production,
            _ => DigitalBrainProfile.Local
        };

        if (config.Profile == DigitalBrainProfile.Local)
        {
            config.AutostartShell = true;
            config.UseRedis = false;
        }
        else if (config.Profile == DigitalBrainProfile.Product)
        {
            config.AutostartShell = true;
            config.UseRedis = true;
        }
        else if (config.Profile == DigitalBrainProfile.Production)
        {
            config.AutostartShell = false;
            config.UseRedis = true;
        }

        return config;
    }
}
