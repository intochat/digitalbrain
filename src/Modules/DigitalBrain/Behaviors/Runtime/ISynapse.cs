using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

// The public authoring name. IBehavior remains as a runtime compatibility contract.
public interface ISynapse : IBehavior;

public static class SynapseApp
{
    public static Task RunAsync<TSynapse>(string[] args,
        Func<IDigitalBrain, IReadOnlyList<SubscriptionRequirement>> requirements)
        where TSynapse : class, ISynapse
        => BehaviorApp.RunAsync<TSynapse>(args, requirements);
}

public static class SynapseHosting
{
    public static IServiceCollection AddSynapse<TSynapse>(this IServiceCollection services,
        Func<IDigitalBrain, IReadOnlyList<SubscriptionRequirement>>? requirements = null)
        where TSynapse : class, ISynapse
        => services.AddBehavior<TSynapse>(requirements);
}
