using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.GitHub;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Microsoft;

public sealed class MicrosoftModule : IModule
{
    public const string AspireConfigurationRoot = "DigitalBrain:Microsoft:Aspire";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var configuration = builder.Configuration.GetSection(AspireConfigurationRoot);
        AspireConnectionSettings? settings = null;
        if (!DigitalBrainFakes.Enabled(builder.Configuration) && !string.IsNullOrWhiteSpace(configuration["ProjectPath"]))
        {
            var project = Path.GetFullPath(configuration["ProjectPath"]!);
            if (!File.Exists(project))
            {
                throw new InvalidOperationException("The configured Aspire AppHost project does not exist.");
            }
            settings = new(project, configuration["ApplicationName"] ?? "DigitalBrain", configuration["Command"] ?? "aspire");
        }
        builder.Services.AddSingleton(new AspireConnection(settings));
        if (settings is not null)
        {
            builder.Services.AddNativeTool("aspire_read", services => services.GetRequiredService<AspireNativeTools>().CreateRead());
        }
        builder.Services.AddSingleton<AspireNativeTools>();
        GitHubModule.Configure(builder);
    }
}
