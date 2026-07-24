using System.ComponentModel;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Kernel;

[EditorBrowsable(EditorBrowsableState.Never)]
public interface ICompiledModule
{
    ModuleId Id { get; }

    void PrepareSerialization(IServiceCollection services);

    void Activate(ISiloBuilder builder);
}
