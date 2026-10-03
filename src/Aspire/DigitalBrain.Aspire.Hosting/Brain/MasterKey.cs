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
    public static DigitalBrainModuleProjection Provision(IDistributedApplicationBuilder builder, string brainName)
    {
        var scopedName = $"{brainName}-{DigitalBrainHostingNames.MasterKeyParameter}";
        if (builder.Configuration[$"Parameters:{DigitalBrainHostingNames.MasterKeyParameter}"] is not null
            && builder.Configuration[$"Parameters:{scopedName}"] is null)
        {
            throw new InvalidOperationException($"Copy the existing master key to Parameters:{scopedName} before upgrading. Existing encrypted data must keep its original key.");
        }
        var parameter = builder.ExecutionContext.IsRunMode
            ? builder.AddParameter(scopedName, new GenerateParameterDefault { MinLength = 32 }, secret: true, persist: true)
            : builder.AddParameter(scopedName, secret: true);
        return new Projection(parameter);
    }

    private sealed class Projection(IResourceBuilder<ParameterResource> masterKey) : DigitalBrainModuleProjection
    {
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
            => builder.WithEnvironment(DigitalBrainNames.MasterKeyEnvironmentVariable, masterKey);
    }
}
