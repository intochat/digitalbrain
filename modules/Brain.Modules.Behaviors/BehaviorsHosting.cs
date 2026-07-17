using Brain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace Brain.Modules.Behaviors;

public static class BehaviorsHosting
{
    public static ISiloBuilder AddBrainBehaviors(this ISiloBuilder silo) =>
        silo.AddBrainKind("behavior", sp => new BehaviorKind(sp.GetRequiredService<IGrainFactory>()));
}
