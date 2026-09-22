using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Microsoft.Aspire;

public static class MicrosoftHostingExtensions
{
    public static DigitalBrainModuleBuilder<AspireModule> WithAspire(
        this DigitalBrainModuleBuilder<AspireModule> module, Action<AspireHostingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new AspireHostingOptions();
        configure(options);
        return module.WithAspire(options.ProjectPath, options.ApplicationName, options.Alias);
    }

    public static DigitalBrainModuleBuilder<AspireModule> WithAspire(
        this DigitalBrainModuleBuilder<AspireModule> module,
        string appHostProject,
        string applicationName = "DigitalBrain",
        string alias = "digitalbrain-local")
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(appHostProject);
        module.AddProjection(new AspireProjection(Path.GetFullPath(appHostProject), applicationName, alias));
        return module;
    }

    private sealed class AspireProjection(string project, string applicationName, string alias)
        : DigitalBrainModuleProjection
    {
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            const string root = AspireModule.ConfigurationRoot;
            builder
                .WithEnvironment(EnvironmentKeys.For(root, "ProjectPath"), project)
                .WithEnvironment(EnvironmentKeys.For(root, "ApplicationName"), applicationName)
                .WithEnvironment(EnvironmentKeys.For(root, "Alias"), alias);
        }
    }
}