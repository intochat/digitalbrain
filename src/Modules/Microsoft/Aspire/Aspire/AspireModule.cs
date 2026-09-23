using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.Microsoft.Aspire;

[ModuleConfiguration(typeof(AspireConfigurationContract))]
public sealed class AspireModule : IModule
{
    public const string ConfigurationRoot = "DigitalBrain:Microsoft:Aspire";

    public static ModuleDefinition Define(AspireModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(AspireModule), new Dictionary<string, string?>
        {
            [ConfigurationRoot + ":ProjectPath"] = options.ProjectPath,
            [ConfigurationRoot + ":ApplicationName"] = options.ApplicationName,
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
    }
}