using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

public sealed class TasksModule : IModule
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
    }
}
