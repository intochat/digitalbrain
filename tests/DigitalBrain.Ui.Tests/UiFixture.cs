using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Testing;
using DigitalBrain.Ui;
using Orleans;

namespace DigitalBrain.Ui.Tests;

public sealed class UiFixture : DigitalBrainFixture
{
    public const string DefaultShellName = FlutterHostingExtensions.DefaultShellName;

    public const string DefaultUiResourceName = FlutterHostingExtensions.DefaultUiResourceName;

    public const string UiBaseEnvironmentVariable = FlutterHostingExtensions.UiBaseEnvironmentVariable;

    public static Uri ResolveProductUiBaseAddress()
    {
        var configured = Environment.GetEnvironmentVariable(UiBaseEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return LaunchSettingsUiBase;
        }

        return new Uri(configured.TrimEnd('/') + "/");
    }

    public static async Task<WebApplication> StartUiEdgeAsync(
        TestBrain test,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(test);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(test.Client);
        builder.Services.AddSingleton<IGrainFactory>(test.Cluster.Client);
        builder.Services.AddUiEdgeServices();

        var app = builder.Build();
        app.MapUiHost();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private static readonly Uri LaunchSettingsUiBase = new("http://localhost:5080/");

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<FlutterModule>();
    }
}
