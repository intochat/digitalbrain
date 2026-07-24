using System.ComponentModel;
using DigitalBrain.Abstractions;
using Orleans.Hosting;

namespace DigitalBrain.Kernel;

[EditorBrowsable(EditorBrowsableState.Never)]
public interface ICompiledModule
{
    ModuleId Id { get; }

    void Activate(ISiloBuilder builder);
}
