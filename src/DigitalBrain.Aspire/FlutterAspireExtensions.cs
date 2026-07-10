using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire;

public static class FlutterAspireExtensions
{
    public const string V2RuntimeEnvironmentVariable = "DIGITALBRAIN_RUNTIME";
    public const string V2TransportEndpointEnvironmentVariable = "DIGITALBRAIN_V2_UI_ENDPOINT";
    public const string V2BootstrapSecretEnvironmentVariable = "DIGITALBRAIN_V2_UI_BOOTSTRAP_SECRET";

    /// <summary>
    /// Starts the legacy V1 Flutter shell wired to the kernel for WatchHomeFeed/RfwCards.
    /// Runtime V2 must use <see cref="AddV2FlutterClient"/> instead.
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

    /// <summary>
    /// Starts Flutter against the authenticated Runtime V2 UI transport. The bootstrap secret is
    /// a local, scope-limited exchange credential; the client exchanges it for an audience-bound
    /// V2 session and never uses it as a bearer token.
    /// </summary>
    public static IResourceBuilder<ExecutableResource> AddV2FlutterClient(
        this DigitalBrainContext ctx,
        string name,
        string flutterAppPath,
        IResourceBuilder<ProjectResource> v2Transport,
        IResourceBuilder<ParameterResource> bootstrapSecret,
        string endpointName = "https",
        string target = "windows")
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(flutterAppPath);
        ArgumentNullException.ThrowIfNull(v2Transport);
        ArgumentNullException.ThrowIfNull(bootstrapSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

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
            .WithEnvironment(V2RuntimeEnvironmentVariable, "V2")
            .WithEnvironment(V2TransportEndpointEnvironmentVariable, v2Transport.GetEndpoint(endpointName))
            .WithEnvironment(V2BootstrapSecretEnvironmentVariable, bootstrapSecret)
            .WithReference(v2Transport.GetEndpoint(endpointName))
            .WaitFor(v2Transport);
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

    /// <summary>
    /// Resolves the repository Flutter app and starts the Runtime V2 shell against the supplied
    /// authenticated transport. Returns <see langword="null"/> when the app is not present.
    /// </summary>
    public static IResourceBuilder<ExecutableResource>? AddDefaultDevV2FlutterClient(
        this DigitalBrainContext ctx,
        IResourceBuilder<ProjectResource> v2Transport,
        IResourceBuilder<ParameterResource> bootstrapSecret,
        string endpointName = "https")
    {
        var flutterPath = ResolveDevFlutterAppPath(ctx.ApplicationBuilder.AppHostDirectory);
        if (string.IsNullOrEmpty(flutterPath))
        {
            return null;
        }

        return ctx.AddV2FlutterClient(
            "flutter-ui",
            flutterPath,
            v2Transport,
            bootstrapSecret,
            endpointName,
            "windows");
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
