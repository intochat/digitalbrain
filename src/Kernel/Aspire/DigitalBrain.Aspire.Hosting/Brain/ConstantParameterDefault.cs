using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;

namespace DigitalBrain.Aspire.Hosting;

internal sealed class ConstantParameterDefault(string value) : ParameterDefault
{
    public override string GetDefaultValue() => value;

    public override void WriteToManifest(ManifestPublishingContext context)
    {
    }
}