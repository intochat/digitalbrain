using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;

namespace DigitalBrain.Aspire.Hosting;

internal sealed class StateProtectionKeyParameterDefault : ParameterDefault
{
    public override string GetDefaultValue()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public override void WriteToManifest(ManifestPublishingContext context)
        => throw new InvalidOperationException("Local state-protection defaults cannot be published.");
}
