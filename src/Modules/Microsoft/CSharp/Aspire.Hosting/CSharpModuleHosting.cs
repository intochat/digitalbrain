using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.CSharp;

// Declares the sandbox stopped: the brain starts it through IAspire when the first script runs.
public sealed class CSharpModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        var options = brain.GetModuleConfiguration<CSharpModule>().GetSection(CSharpOptions.SectionName).Get<CSharpOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(options.SourceRoot)) { return; }
        brain.ApplicationBuilder.AddDockerfile(CSharpSandbox.ResourceName, Path.Combine(options.SourceRoot, CSharpSandbox.SourceProject))
            .WithHttpEndpoint(targetPort: CSharpSandbox.Port, name: "http")
            .WithHttpHealthCheck("/health")
            .WithBindMount(options.SourceRoot, CSharpSandbox.SourceMount, isReadOnly: true)
            .WithVolume("digitalbrain-csharp-nuget", "/root/.nuget/packages")
            // Linux engines only resolve host.docker.internal, which the script's gateway relay dials, with this mapping.
            .WithContainerRuntimeArgs("--add-host", "host.docker.internal:host-gateway")
            .WithParentRelationship(brain.Resource)
            .WithExplicitStart();
    }
}
