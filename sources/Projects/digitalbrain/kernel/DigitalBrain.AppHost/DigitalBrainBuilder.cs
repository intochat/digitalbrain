using DigitalBrain.Hosting.DigitalBrain;
using DigitalBrain.Runtime.Ai;

namespace DigitalBrain.Hosting;

public interface IDigitalBrainBuilder
{
    DigitalBrainResource Resource { get; }
    IDigitalBrainBuilder WithLlmProvider<TProvider>() where TProvider : ILlmProvider;
    IDigitalBrainBuilder WithEmbedding<TModel>() where TModel : EmbeddingModel, new();
    IDigitalBrainBuilder WithVoice2Text<TModel>() where TModel : IVoiceModel;
    IDigitalBrainBuilder WithDefaultConnectors();
    IDigitalBrainBuilder WithShell();
    IDigitalBrainBuilder WithMcp();
}

public sealed class DigitalBrainBuilder(DigitalBrainResource resource, ProfileConfiguration profile) : IDigitalBrainBuilder
{
    public DigitalBrainResource Resource { get; } = resource;
    private readonly AiDomainBuilder _aiBuilder = new(resource);

    public IDigitalBrainBuilder WithLlmProvider<TProvider>() where TProvider : ILlmProvider
    {
        _aiBuilder.WithLlmProvider<TProvider>();
        return this;
    }

    public IDigitalBrainBuilder WithEmbedding<TModel>() where TModel : EmbeddingModel, new()
    {
        _aiBuilder.WithEmbedding<TModel>();
        return this;
    }

    public IDigitalBrainBuilder WithVoice2Text<TModel>() where TModel : IVoiceModel
    {
        _aiBuilder.WithVoice2Text<TModel>();
        return this;
    }

    public IDigitalBrainBuilder WithDefaultConnectors()
    {
        // Enforces the default SDK connectors matching E-LAUNCH
        // It is already wired up transitive via Projects.DigitalBrain_SDK and the scan.
        return this;
    }

    public IDigitalBrainBuilder WithShell()
    {
        bool webExists = Resource.AppBuilder.Resources.Any(r => string.Equals(r.Name, "flutter-web", StringComparison.OrdinalIgnoreCase));
        bool windowsExists = Resource.AppBuilder.Resources.Any(r => string.Equals(r.Name, "flutter-windows", StringComparison.OrdinalIgnoreCase));

        if (profile.AutostartShell && !webExists && !windowsExists)
        {
            var appBuilder = Resource.AppBuilder;
            var flutterBuilder = appBuilder.AddFlutter();

            // Always add web
            flutterBuilder.WithWeb();

            // Always add windows, but autostart it for Product and Local profiles
            bool autostartWindows = profile.Profile == DigitalBrainProfile.Product || profile.Profile == DigitalBrainProfile.Local;
            flutterBuilder.WithWindows(autostart: autostartWindows);

            // Wire references
            flutterBuilder.WithReference(Resource);
        }

        return this;
    }

    public IDigitalBrainBuilder WithMcp()
    {
        bool mcpExists = Resource.AppBuilder.Resources.Any(r => string.Equals(r.Name, "digitalbrain-mcp", StringComparison.OrdinalIgnoreCase));

        if ((profile.Profile == DigitalBrainProfile.Product || profile.Profile == DigitalBrainProfile.Local) && !mcpExists)
        {
            _ = Resource.AppBuilder.AddProject<Projects.DigitalBrain_SDK>("digitalbrain-mcp")
                .WithReference(Resource.Kernel!)
                .WaitFor(Resource.Kernel!)
                .WithEnvironment("KERNEL_ENDPOINT", Resource.Kernel!.GetEndpoint("kernel-http"))
                .WithHttpEndpoint(port: 5810, targetPort: 5810, name: "http", isProxied: false);
        }

        return this;
    }

    internal void ApplyConfigurations()
    {
        foreach (var silo in Resource.Silos)
        {
            _aiBuilder.ApplyTo(silo);
        }
    }
}
