using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.GitHub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft;

public sealed class MicrosoftModule : IModule
{
    public const string AspireConfigurationRoot = "DigitalBrain:Microsoft:Aspire";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.Configuration.GetSection(AspireOptions.SectionName).Get<AspireOptions>() ?? new();
        var enabled = !string.IsNullOrWhiteSpace(options.ProjectPath);
        builder.Services.AddOptions<AspireOptions>().Bind(builder.Configuration.GetSection(AspireOptions.SectionName))
            .Validate(options =>
            {
                _ = options.CreateSettings();
                return true;
            })
            .ValidateOnStart();
        builder.Services.AddSingleton(services => new AspireConnection(services.GetRequiredService<IOptions<AspireOptions>>().Value.CreateSettings()));
        if (enabled)
        {
            builder.Services.AddNativeTool("aspire_read", services => services.GetRequiredService<AspireNativeTools>().CreateRead());
        }
        builder.Services.AddSingleton<AspireNativeTools>();
        GitHubModule.Configure(builder);
    }
}
