using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;

namespace Aspire.Hosting.DigitalBrain;

[AspireExport]
public sealed class DigitalBrainClientResource
{
    internal DigitalBrainClientResource(
        string name,
        OrleansServiceClient orleans,
        IResourceBuilder<AzureStorageResource> discoveryStorage)
    {
        Name = name;
        Orleans = orleans;
        DiscoveryStorage = discoveryStorage;
    }

    public string Name { get; }

    public OrleansServiceClient Orleans { get; }

    internal IResourceBuilder<AzureStorageResource> DiscoveryStorage { get; }
}
