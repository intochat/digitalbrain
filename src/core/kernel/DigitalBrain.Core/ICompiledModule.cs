using System.ComponentModel;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

[EditorBrowsable(EditorBrowsableState.Never)]
public interface ICompiledModule
{
    ModuleId Id { get; }

    CapabilityManifest Capabilities
        => new(
            Id,
            "0.0.0",
            "unspecified",
            Array.Empty<string>(),
            Array.Empty<NeuronCapabilityDescriptor>());

    void PrepareSerialization(IServiceCollection services);

    void Activate(ISiloBuilder builder);
}
