using DigitalBrain.Sdk.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace DigitalBrain.Microsoft.GitHub;

// Deployments that still project the App key on DigitalBrain:Microsoft:GitHub keep working: those keys only seed an
// Unconfigured registration, and the canonical DigitalBrain:Integrations:github seed wins whenever it is present.
internal sealed class GitHubLegacyKeySeeder(IConfiguration configuration, IGrainFactory grains, ILogger<GitHubLegacyKeySeeder> logger) : IStartupTask
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        var app = configuration.GetSection(GitHubAppOptions.SectionName);
        var declaredRepository = configuration.GetSection($"{GitHubRepositoriesOptions.SectionName}:Repositories").GetChildren()
            .FirstOrDefault(repository => !string.IsNullOrWhiteSpace(repository["PrivateKeyPem"]));
        var privateKey = FirstText(app["PrivateKeyPem"], declaredRepository?["PrivateKeyPem"]);
        var appId = FirstText(app["AppId"], declaredRepository?["AppId"]);
        if (privateKey is null || appId is null)
        {
            return;
        }

        try
        {
            var after = await grains.GetGrain<IIntegrationRegistration>("integration/github")
                .SeedIfUnconfigured(new ConfigureRegistration { Values = new() { ["PrivateKeyPem"] = privateKey, ["AppId"] = appId } });
            logger.LogInformation("Integration github is {Status} after seeding from DigitalBrain:Microsoft:GitHub.", after.Status);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(error, "Seeding integration github from DigitalBrain:Microsoft:GitHub failed.");
        }
    }

    private static string? FirstText(params string?[] candidates) => candidates.FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
}
