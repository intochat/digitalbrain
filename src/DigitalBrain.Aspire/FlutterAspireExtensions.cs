using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire;

public static class FlutterAspireExtensions
{
    /// <summary>
    /// Starts Flutter (windows or web-server) wired to the kernel for live surfaces/RfwCards.
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
            .WithReference(ctx.Llm);
    }

    // Dev default helper (item 12). Path resolve + AddFlutterClient + kernel ref.
    // Keep this as a dev convenience only; production wiring should choose the client explicitly.
    public static IResourceBuilder<ExecutableResource>? AddDefaultDevFlutterClient(this DigitalBrainContext ctx, IResourceBuilder<ProjectResource> kernel)
    {
        var flutterPath = ResolveDevFlutterAppPath(ctx.ApplicationBuilder.AppHostDirectory);
        if (string.IsNullOrEmpty(flutterPath))
        {
            return null;
        }

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
        {
            return Path.GetFullPath(flutterPathEnv);
        }

        var candidatePaths = new[]
        {
            // Current repo layout: /hosts/DigitalBrain.AppHost -> /app.
            Path.GetFullPath(Path.Combine(appHostDirectory, "..", "..", "app")),
            // Backward-compatible fallback for older layouts where /app sat beside the AppHost parent.
            Path.GetFullPath(Path.Combine(appHostDirectory, "..", "app"))
        };

        return candidatePaths.FirstOrDefault(path =>
            Directory.Exists(path) && File.Exists(Path.Combine(path, "pubspec.yaml")));
    }
}
