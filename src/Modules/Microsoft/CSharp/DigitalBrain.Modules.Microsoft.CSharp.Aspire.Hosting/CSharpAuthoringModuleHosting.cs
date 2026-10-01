using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Microsoft.CSharp;

public sealed class CSharpAuthoringModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain) => ArgumentNullException.ThrowIfNull(brain);
}
