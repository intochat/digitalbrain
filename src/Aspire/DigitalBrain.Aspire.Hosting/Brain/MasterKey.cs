using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain;
using DigitalBrain.Contracts;

namespace DigitalBrain.Aspire.Hosting;

// The master key protecting owner data keys: generated and persisted by Aspire in run mode,
// supplied as a deployment parameter in publish mode, and projected to silos only — clients
// never receive it. The platform refuses to start without it.
internal static class MasterKey
{
    public static DigitalBrainModuleProjection Provision(IDistributedApplicationBuilder builder)
    {
        var parameter = builder.ExecutionContext.IsRunMode
            ? builder.AddParameter(DigitalBrainHostingNames.MasterKeyParameter, new GenerateParameterDefault { MinLength = 32 }, secret: true, persist: true)
            : builder.AddParameter(DigitalBrainHostingNames.MasterKeyParameter, secret: true);
        return new Projection(parameter);
    }

    private sealed class Projection(IResourceBuilder<ParameterResource> masterKey) : DigitalBrainModuleProjection
    {
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
            => builder.WithEnvironment(DigitalBrainNames.MasterKeyEnvironmentVariable, masterKey);
    }
}
