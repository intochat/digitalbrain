using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Flutter.Aspire.Hosting;

public sealed class FlutterModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        var options = brain.ApplicationBuilder.Configuration.GetSection(FlutterHostOptions.SectionName)
            .Get<FlutterHostingOptions>() ?? new();
        var module = new DigitalBrainModuleBuilder<FlutterModule>(brain);
        switch (options.Kind)
        {
            case FlutterHostKind.None: break;
            case FlutterHostKind.Web: module.WithWebHost(); break;
            case FlutterHostKind.Window: module.WithWindowHost(); break;
            case FlutterHostKind.Headless: module.WithHeadlessHost(); break;
            default: throw new ArgumentOutOfRangeException(nameof(options));
        }
    }
}
