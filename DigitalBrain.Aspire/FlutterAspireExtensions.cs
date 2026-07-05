using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire;

public static class FlutterAspireExtensions
{
    /// <summary>
    /// Flutter as marketplace pack + Aspire integration. Call from AppHost when the Flutter pack (DigitalBrain.UI.AspireFlutter) is installed.
    /// Starts Flutter (windows or web-server) wired to brain for live surfaces/RfwCards. Enables full packing/distribution/reuse of the UI client as a NeuroPack.
    /// </summary>
    public static IResourceBuilder<ExecutableResource> AddFlutterClient(
        this DigitalBrainContext ctx,
        string name,
        string flutterAppPath,
        string target = "windows")
    {
        var cmd = ctx.ApplicationBuilder.Configuration["DigitalBrain:FlutterCommand"]
            ?? Environment.GetEnvironmentVariable("FLUTTER_COMMAND")
            ?? "flutter";

        return ctx.ApplicationBuilder.AddExecutable(
                name,
                cmd,
                flutterAppPath,
                "run",
                "-d",
                target)
            .WithReference(ctx.OrleansClient)
            .WithReference(ctx.Llm)
            .WithEnvironment("DIGITALBRAIN_UI_PACK", "DigitalBrain.UI.AspireFlutter")
            .WithEnvironment("DIGITALBRAIN_UI_TIER1_RESTART_REQUIRED", "true");
    }

    // Dev default helper (item 12). Path resolve + AddFlutterClient + kernel ref.
    // The DigitalBrain.UI.AspireFlutter (or equivalent) pack can later provide/override these resource bits.
    public static IResourceBuilder<ExecutableResource>? AddDefaultDevFlutterClient(this DigitalBrainContext ctx, IResourceBuilder<ProjectResource> kernel)
    {
        var flutterPath = ResolveDevFlutterAppPath(ctx.ApplicationBuilder.AppHostDirectory);
        if (string.IsNullOrEmpty(flutterPath))
            return null;
        return ctx.AddFlutterClient("flutter-ui", flutterPath, "windows")
            .WithReference(kernel);
    }

    // Public so packs / other extensions can reuse the dev path resolution logic or provide alternatives.
    // Takes the app host directory directly (rather than IDistributedApplicationBuilder) so it's testable
    // without any Aspire builder/test-double machinery.
    public static string? ResolveDevFlutterAppPath(string appHostDirectory)
    {
        var flutterPathEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_FLUTTER_APP_PATH");
        if (!string.IsNullOrWhiteSpace(flutterPathEnv) && Directory.Exists(flutterPathEnv))
            return Path.GetFullPath(flutterPathEnv);

        var canonicalPath = Path.GetFullPath(Path.Combine(appHostDirectory, "..", "app"));
        return Directory.Exists(canonicalPath) && File.Exists(Path.Combine(canonicalPath, "pubspec.yaml"))
            ? canonicalPath
            : null;
    }
}
