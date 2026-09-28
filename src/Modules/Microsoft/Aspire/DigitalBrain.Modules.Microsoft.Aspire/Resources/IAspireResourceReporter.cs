using Orleans.Metadata;

namespace DigitalBrain.Microsoft.Aspire;

// Only the AppHost bridge endpoint reports; neurons and scripts see IAspire.
[Alias("microsoft.aspire.reporter"), DefaultGrainType("microsoft.aspire")]
internal interface IAspireResourceReporter : IGrainWithStringKey
{
    Task Report(AspireResource resource);
}
