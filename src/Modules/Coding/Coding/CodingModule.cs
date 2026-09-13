using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Coding;

public sealed class CodingModule : IModule
{
    public const string ConfigurationRoot = "DigitalBrain:Coding";
    public const string SolutionPathKey = "DigitalBrain:Coding:SolutionPath";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<CodingModule>();
        builder.Services.TryAddSingleton<SolutionWorkspace>();
    }
}
