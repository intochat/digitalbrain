using System.Diagnostics;
using System.Net;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Brain;
using Ino.Core.Hosting.Llm;
using Ino.Core.Hosting.Placement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Runtime.MembershipService.SiloMetadata;

namespace Ino.Kernel;

public static class KernelSiloConfigurator
{
    /// <summary>
    /// Kernel silo = Orleans cluster primary. Identity + domain silos connect
    /// to this endpoint as <c>primarySiloEndpoint</c> so every silo process
    /// joins ONE cluster via Orleans' localhost membership oracle. All silos
    /// share the same <c>clusterId</c>/<c>serviceId</c> so Discovery grain
    /// calls route cluster-wide. Endpoints come from
    /// <see cref="InoOrleansEndpoints"/>: fixed defaults for `aspire run`,
    /// env-var-overridden in tests so per-fixture port randomization can
    /// avoid TIME_WAIT collisions across test assemblies.
    /// </summary>
    public static IHostApplicationBuilder AddKernel(this IHostApplicationBuilder builder)
    {
        builder.UseOrleans(silo =>
        {
            silo.UseLocalhostClustering(
                siloPort: InoOrleansEndpoints.KernelSiloPort,
                gatewayPort: InoOrleansEndpoints.KernelGatewayPort,
                primarySiloEndpoint: null,
                serviceId: InoOrleansEndpoints.ServiceId,
                clusterId: InoOrleansEndpoints.ClusterId);

            silo.UseSiloMetadata(new Dictionary<string, string>
            {
                [PinToSiloStrategy.SiloMetadataKey] = "kernel",
            });

            silo.UseInoJournaling();
            silo.UseInoBrainStream();
            silo.UseInoNeuron();
        });

        builder.Services.AddPinToSiloPlacement();

        builder.Services.Configure<RegistrationOptions>(o =>
        {
            o.Silo = DomainId.From("kernel");
            o.Domains = [];
            o.BuiltInGrainTypes = [typeof(SystemEcho), typeof(CortexNeuron)];
        });
        builder.Services.AddHostedService<RegistrationHostedService>();

        builder.Services.AddSingleton<IDiscoveryClient, DiscoveryClient>();

        // Kernel silo fires synapses on behalf of the gateway (Caller.Ambient(kernel));
        // SystemFirePort resolves targets via IDiscoveryClient and dispatches across the
        // cluster.
        builder.Services.AddSingleton(_ => new ActivitySource(Telemetry.ActivitySourceName));
        builder.Services.AddSingleton<IInoEventBus, InMemoryInoEventBus>();
        builder.Services.AddSingleton<ISynapseJournal, InMemorySynapseJournal>();
        builder.Services.AddSingleton<IFirePort, SystemFirePort>();

        // LLM provider (Ino:Llm:Provider — bdd-mock by default). The bdd-mock
        // provider scans installed domain Features/*.feature files (copied
        // into the silo bin via ProjectReference + Content) and serves their
        // quoted Given/Then pairs as IChatClient responses. Cortex calls it
        // per-route to populate the inspector Reasoning panel via IReasoningProbe.
        builder.Services.AddInoLlm(builder.Configuration);

        return builder;
    }
}
