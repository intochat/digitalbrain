using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Coding.Aspire.Hosting;

public static class CodingHostingExtensions
{
    public static DigitalBrainModuleBuilder<CodingModule> WithSolution(this DigitalBrainModuleBuilder<CodingModule> module, string solutionPath)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        module.AddProjection(new SolutionProjection(Path.GetFullPath(solutionPath)));
        return module;
    }

    private sealed class SolutionProjection(string solutionPath) : DigitalBrainModuleProjection
    {
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.WithEnvironment(EnvironmentKeys.For(CodingModule.ConfigurationRoot, "SolutionPath"), solutionPath);
        }
    }
}
