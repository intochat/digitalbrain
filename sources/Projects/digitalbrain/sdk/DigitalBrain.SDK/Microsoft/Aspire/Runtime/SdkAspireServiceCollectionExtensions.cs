using Microsoft.Extensions.DependencyInjection.Extensions;
using DigitalBrain.Runtime.Aspire;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.SDK.Microsoft.Aspire;

namespace DigitalBrain.SDK.Microsoft.Aspire.Runtime;

// E-SDK #60. The kernel host calls this to enrol the Aspire connector's
// ingress emission into the silo's hosted-service pipeline. Typed
// ServiceDescriptor (ImplementationType = AspireAppStartedEmitter) so
// TryAddEnumerable can distinguish this registration from other
// factory-based IHostedService entries in the kernel — a factory-returning-
// IHostedService descriptor reports its ImplementationType as IHostedService
// itself, which TryAddEnumerable rejects as "indistinguishable" and crashes
// silo startup with ArgumentException.
public static class SdkAspireServiceCollectionExtensions
{
    public static IServiceCollection AddDigitalBrainSdkAspire(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AspireAppStartedEmitter>());

        services.TryAddSingleton<IAspireBootConnector, AspireBootConnector>();

        services.AddSingleton<IInterpretedNeuronSource, AspireInoSource>();

        return services;
    }
}
