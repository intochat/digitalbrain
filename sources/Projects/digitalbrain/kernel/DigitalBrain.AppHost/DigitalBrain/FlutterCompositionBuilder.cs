namespace DigitalBrain.Hosting.DigitalBrain;

public static class AddFlutterExtensions
{
    public static FlutterCompositionBuilder AddFlutter(
        this IDistributedApplicationBuilder builder)
        => new(builder);
}

public sealed class FlutterCompositionBuilder(IDistributedApplicationBuilder builder)
{
    readonly List<(IResourceBuilder<ExecutableResource> Resource, bool IsWeb)> _resources = new();

    private static string GetWorkingDir(string appHostDir)
    {
        var pathsToTry = new[]
        {
            Path.Combine(appHostDir, "../../UI/flutter"),
            Path.Combine(appHostDir, "UI/flutter"),
            Path.Combine(appHostDir, "../UI/flutter"),
            "UI/flutter",
            "../../UI/flutter"
        };

        foreach (var path in pathsToTry)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }
        return "../../UI/flutter";
    }

    public FlutterCompositionBuilder WithWeb()
    {
        var workingDir = GetWorkingDir(builder.AppHostDirectory);
        // --wasm (skwasm) replaces the removed HTML renderer: it is the only web
        // path that runs dart:ui FragmentProgram shaders (EarthGlobe) while still
        // rendering Lottie. See docs/redesign Slice 0.
        var web = builder.AddExecutable("flutter-web", "flutter", workingDir,
                "run", "-d", "web-server",
                "--web-hostname=localhost", "--web-port=5800", "--release", "--wasm")
            .WithHttpEndpoint(port: 5800, targetPort: 5800, name: "http", isProxied: false);
        _resources.Add((web, true));
        return this;
    }

    public FlutterCompositionBuilder WithWindows(bool autostart = false)
    {
        var workingDir = GetWorkingDir(builder.AppHostDirectory);
        var windows = builder.AddExecutable("flutter-windows", "flutter", workingDir,
            "run", "-d", "windows", "--vm-service-port=5821", "--print-dtd");
        if (!autostart)
            windows = windows.WithExplicitStart();
        _resources.Add((windows, false));
        return this;
    }

    public void WithReference(DigitalBrainResource digitalbrain)
    {
        var kernel = digitalbrain.Kernel!;
        var kernelHttps = kernel.GetEndpoint("kernel-https");
        var kernelHttp = kernel.GetEndpoint("kernel-http");

        foreach (var (resource, isWeb) in _resources)
        {
            var endpoint = isWeb ? kernelHttp : kernelHttps;
            resource
                .WithArgs(context => context.Args.Add(
                    ReferenceExpression.Create($"--dart-define=KERNEL_ENDPOINT={endpoint}")))
                .WithReference(kernel)
                .WithOtlpExporter();

            if (isWeb)
                resource.WaitFor(kernel);
        }
    }
}
