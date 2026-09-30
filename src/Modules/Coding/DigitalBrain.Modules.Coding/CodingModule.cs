using DigitalBrain.Core;
using DigitalBrain.Microsoft.DotNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Coding;

public sealed class CodingModule : IModule<CodingModuleOptions>
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<CodingModuleOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.PopulateModuleOptions(nameof(CodingModule), options))
            .ValidateOnStart();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<GitRunner>();
    }
}
