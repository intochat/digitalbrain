using System.ComponentModel;
using DigitalBrain.Abstractions;
using Orleans.Hosting;

namespace DigitalBrain.AI;

public sealed class AIModule : IModule
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Configure(ISiloBuilder builder)
        => ArgumentNullException.ThrowIfNull(builder);
}
