using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Testing;

public sealed class ScriptedModule(ModuleId id, CapabilityManifest capabilities) : ICompiledModule
{
    public ModuleId Id { get; } = id;

    public CapabilityManifest Capabilities { get; } = capabilities;

    public void PrepareSerialization(IServiceCollection services)
    {
    }

    public void Activate(ISiloBuilder builder)
    {
    }
}
