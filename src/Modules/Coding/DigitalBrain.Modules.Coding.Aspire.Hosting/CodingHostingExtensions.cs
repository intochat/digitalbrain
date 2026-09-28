using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Coding.Aspire.Hosting;

public static class CodingHostingExtensions
{
    public static DigitalBrainModuleBuilder<CodingModule> WithSolution(this DigitalBrainModuleBuilder<CodingModule> module, string solutionPath)
        => module.WithSolution(options => options.SolutionPath = solutionPath);

    public static DigitalBrainModuleBuilder<CodingModule> WithSolution(
        this DigitalBrainModuleBuilder<CodingModule> module, Action<CodingHostingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new CodingHostingOptions();
        configure(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SolutionPath);
        module.AddProjection(new SolutionProjection(
            Path.GetFullPath(options.SolutionPath), options.WorkspaceKey, options.TestProject));
        return module;
    }

    private sealed class SolutionProjection(string solutionPath, string? workspaceKey, string? testProject) : DigitalBrainModuleProjection
    {
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.WithEnvironment(EnvironmentKeys.For(CodingModule.ConfigurationRoot, "SolutionPath"), solutionPath);
            if (workspaceKey is not null)
            {
                builder.WithEnvironment(EnvironmentKeys.For(CodingModule.ConfigurationRoot, "WorkspaceKey"), workspaceKey);
            }
            if (testProject is not null)
            {
                builder.WithEnvironment(EnvironmentKeys.For(CodingModule.ConfigurationRoot, "TestProject"), testProject);
            }
        }
    }
}