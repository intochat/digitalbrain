using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire;

public static class FlutterAspireExtensions
{
    public const string TransportEndpointEnvironmentVariable = "DIGITALBRAIN_V2_UI_ENDPOINT";
    public const string BootstrapSecretEnvironmentVariable = "DIGITALBRAIN_V2_UI_BOOTSTRAP_SECRET";
    public const string OidcIssuerEnvironmentVariable = "DIGITALBRAIN_OIDC_ISSUER";
    public const string OidcClientIdEnvironmentVariable = "DIGITALBRAIN_OIDC_CLIENT_ID";
    private static readonly string[] DesktopTargets = ["windows", "linux", "macos"];

    /// <summary>
    /// Starts Flutter against the authenticated UI transport. The bootstrap secret is
    /// a local, scope-limited exchange credential; the client exchanges it for an audience-bound
    /// UI session and never uses it as a bearer token.
    /// </summary>
    public static IResourceBuilder<ExecutableResource> AddFlutterClient(
        this DigitalBrainContext ctx,
        string name,
        string flutterAppPath,
        IResourceBuilder<ProjectResource> transport,
        IResourceBuilder<ParameterResource> bootstrapSecret,
        string endpointName = "https",
        string target = "windows")
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(flutterAppPath);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(bootstrapSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (!DesktopTargets.Contains(target, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                "The secret-bearing Flutter client supports desktop targets only.",
                nameof(target));

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
            .WithEnvironment(TransportEndpointEnvironmentVariable, transport.GetEndpoint(endpointName))
            .WithEnvironment(BootstrapSecretEnvironmentVariable, bootstrapSecret)
            .WithReference(transport.GetEndpoint(endpointName))
            .WaitFor(transport);
    }

    /// <summary>
    /// Starts a secret-free Flutter web server against the authenticated UI transport.
    /// Browser authentication uses public OIDC configuration compiled into the web bundle;
    /// this API intentionally has no bootstrap-secret parameter.
    /// </summary>
    public static IResourceBuilder<ExecutableResource> AddFlutterWebClient(
        this DigitalBrainContext ctx,
        string name,
        string flutterAppPath,
        IResourceBuilder<ProjectResource> transport,
        string oidcIssuer,
        string oidcClientId,
        string endpointName = "https",
        int? port = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(flutterAppPath);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        if (!Uri.TryCreate(oidcIssuer, UriKind.Absolute, out var issuer) ||
            issuer.Scheme != Uri.UriSchemeHttps || issuer.Host.Length == 0 ||
            issuer.UserInfo.Length != 0 || issuer.Query.Length != 0 || issuer.Fragment.Length != 0)
            throw new ArgumentException("The Flutter web OIDC issuer must be an absolute HTTPS origin.", nameof(oidcIssuer));
        if (string.IsNullOrWhiteSpace(oidcClientId) || oidcClientId.Length > 512 || oidcClientId.Any(char.IsControl))
            throw new ArgumentException("The Flutter web OIDC client ID is invalid.", nameof(oidcClientId));
        if (port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        var cmd = ctx.ApplicationBuilder.Configuration["DigitalBrain:FlutterCommand"]
            ?? Environment.GetEnvironmentVariable("FLUTTER_COMMAND")
            ?? "flutter";
        var web = ctx.ApplicationBuilder.AddExecutable(
                name,
                cmd,
                flutterAppPath,
                "run",
                "-d",
                "web-server",
                "--web-hostname",
                "127.0.0.1")
            .WithHttpEndpoint(port: port, name: "http", isProxied: true);
        var webEndpoint = web.GetEndpoint("http");
        var transportEndpoint = transport.GetEndpoint(endpointName);

        return web
            .WithArgs(
                "--web-port",
                webEndpoint.Property(EndpointProperty.TargetPort),
                ReferenceExpression.Create($"--dart-define={TransportEndpointEnvironmentVariable}={transportEndpoint}"),
                ReferenceExpression.Create($"--dart-define={OidcIssuerEnvironmentVariable}={oidcIssuer}"),
                ReferenceExpression.Create($"--dart-define={OidcClientIdEnvironmentVariable}={oidcClientId}"))
            .WithReference(transportEndpoint)
            .WaitFor(transport)
            .WithHttpHealthCheck(path: "/", endpointName: "http")
            .WithUrlForEndpoint("http", url =>
            {
                url.Url = "/#/chat";
                url.DisplayText = "DigitalBrain chat";
            });
    }

    /// <summary>
    /// Resolves the repository Flutter app and starts the authenticated shell against the supplied
    /// authenticated transport. Returns <see langword="null"/> when the app is not present.
    /// </summary>
    public static IResourceBuilder<ExecutableResource>? AddDefaultDevFlutterClient(
        this DigitalBrainContext ctx,
        IResourceBuilder<ProjectResource> transport,
        IResourceBuilder<ParameterResource> bootstrapSecret,
        string endpointName = "https")
    {
        var flutterPath = ResolveDevFlutterAppPath(ctx.ApplicationBuilder.AppHostDirectory);
        if (string.IsNullOrEmpty(flutterPath))
        {
            return null;
        }

        return ctx.AddFlutterClient(
            "flutter-ui",
            flutterPath,
            transport,
            bootstrapSecret,
            endpointName,
            "windows");
    }

    /// <summary>
    /// Resolves the repository Flutter app and starts its OIDC-authenticated browser shell.
    /// Returns <see langword="null"/> when the app is not present.
    /// </summary>
    public static IResourceBuilder<ExecutableResource>? AddDefaultDevFlutterWebClient(
        this DigitalBrainContext ctx,
        IResourceBuilder<ProjectResource> transport,
        string oidcIssuer,
        string oidcClientId,
        string endpointName = "https",
        int? port = null)
    {
        var flutterPath = ResolveDevFlutterAppPath(ctx.ApplicationBuilder.AppHostDirectory);
        if (string.IsNullOrEmpty(flutterPath)) return null;

        return ctx.AddFlutterWebClient(
            "flutter-web",
            flutterPath,
            transport,
            oidcIssuer,
            oidcClientId,
            endpointName,
            port);
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
