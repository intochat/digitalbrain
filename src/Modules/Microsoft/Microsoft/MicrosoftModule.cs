using DigitalBrain.Core;
using DigitalBrain.Microsoft.GitHub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.Microsoft;

public sealed class MicrosoftModule : IModule
{
    public const string AspireConfigurationRoot = "DigitalBrain:Microsoft:Aspire";

    public static ModuleDefinition Define(MicrosoftModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(MicrosoftModule), new Dictionary<string, string?>
        {
            [AspireConfigurationRoot + ":ProjectPath"] = options.AspireProjectPath,
            [AspireConfigurationRoot + ":ApplicationName"] = options.AspireApplicationName,
        });
    }

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<AspireOptions>().Bind(builder.Configuration.GetSection(AspireOptions.SectionName))
            .Validate(options =>
            {
                _ = options.CreateSettings();
                return true;
            })
            .ValidateOnStart();
        builder.Services.AddSingleton(services => new AspireConnection(services.GetRequiredService<IOptions<AspireOptions>>().Value.CreateSettings()));
        GitHubModule.Configure(builder);
    }
}