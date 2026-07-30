using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Flutter.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

internal static class FlutterHostingProjectionSupport
{
    public const string JournalConnectionEnvironmentKey =
        "ConnectionStrings__" + DigitalBrainHostingExtensions.JournalConnectionName;

    public static string UIProjectPath => RepositoryAssets.Path(
        "os",
        "DigitalBrain.OS.Ui",
        "DigitalBrain.OS.Ui.csproj");

    public static string FlutterClientDirectory => RepositoryAssets.Path("clients", "flutter", "core");

    public static string FlutterShellDirectory => RepositoryAssets.Path("clients", "flutter", "shell");

    public static async Task AssertShellDesktopLayoutAsync(string shellDirectory, CancellationToken cancellationToken = default)
    {
        Assert.True(
            File.Exists(Path.Combine(shellDirectory, "pubspec.yaml")),
            "clients/flutter/shell must exist for desktop chrome.");
        Assert.True(
            File.Exists(Path.Combine(shellDirectory, "lib", "main.dart")),
            "shell Windows chrome requires lib/main.dart (Desktop host uses clients/flutter/shell).");
        Assert.True(
            Directory.Exists(Path.Combine(shellDirectory, FlutterHostingExtensions.DefaultDeviceTarget)),
            "shell Windows chrome requires windows/ (Desktop host uses clients/flutter/shell).");
        Assert.False(
            File.Exists(Path.Combine(
                shellDirectory,
                FlutterHostingExtensions.HeadlessHostEntry.Replace('/', Path.DirectorySeparatorChar))),
            "shell is desktop-only — headless entry stays on pure-Dart core.");
        var pubspec = await File.ReadAllTextAsync(
            Path.Combine(shellDirectory, "pubspec.yaml"),
            cancellationToken).ConfigureAwait(true);
        Assert.Contains("sdk: flutter", pubspec, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task AssertPureDartClientLayoutAsync(string clientDirectory, CancellationToken cancellationToken = default)
    {
        Assert.True(
            File.Exists(Path.Combine(clientDirectory, "pubspec.yaml")),
            "clients/flutter/core must exist.");
        Assert.True(
            File.Exists(Path.Combine(
                clientDirectory,
                FlutterHostingExtensions.HeadlessHostEntry.Replace('/', Path.DirectorySeparatorChar))),
            $"pure-Dart package hosts {FlutterHostingExtensions.HeadlessHostEntry}.");
        Assert.False(
            File.Exists(Path.Combine(clientDirectory, "lib", "main.dart")),
            "desktop entry lives in clients/flutter/shell — core must not claim lib/main.dart.");
        Assert.False(
            Directory.Exists(Path.Combine(clientDirectory, FlutterHostingExtensions.DefaultDeviceTarget)),
            "desktop runner lives in clients/flutter/shell — core must not claim windows/.");
        var pubspec = await File.ReadAllTextAsync(
            Path.Combine(clientDirectory, "pubspec.yaml"),
            cancellationToken).ConfigureAwait(true);
        Assert.DoesNotContain("sdk: flutter", pubspec, StringComparison.OrdinalIgnoreCase);
    }

    public static void AssertNoOSSurfaceResources(IDistributedApplicationBuilder builder)
    {
        var surface = builder.Resources
            .Where(static resource => resource.Name is FlutterHostingExtensions.DefaultUIResourceName
                or FlutterHostingExtensions.DefaultFlutterResourceName)
            .Select(static resource => $"{resource.GetType().Name}:{resource.Name}")
            .ToArray();
        Assert.True(
            surface.Length == 0,
            $"OS surface resources projected without With*: {string.Join(", ", surface)}");
    }

    public static void AssertNoFlutterHost(IDistributedApplicationBuilder builder)
    {
        Assert.DoesNotContain(
            builder.Resources,
            resource => resource.Name == FlutterHostingExtensions.DefaultFlutterResourceName);
    }

    public static void AssertUIHasNamedHttpEndpoint(IResource ui)
    {
        var http = Assert.Single(
            ui.Annotations.OfType<EndpointAnnotation>(),
            endpoint => string.Equals(
                endpoint.Name,
                FlutterHostingExtensions.UiEdgeEndpointName,
                StringComparison.Ordinal));
        Assert.Equal(FlutterHostingExtensions.UiEdgeEndpointName, http.UriScheme, StringComparer.OrdinalIgnoreCase);
    }

    public static void AssertExclusiveFlutterHostEnvironment(HashSet<string> environment)
    {
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                FlutterHostingExtensions.UIBaseEnvironmentVariable,
                FlutterHostingExtensions.ShellEnvironmentVariable,
                FlutterHostingExtensions.ChatEnvironmentVariable,
            },
            environment);
    }

    public static void AssertClientSafeUIProductEnvironment(
        IReadOnlyDictionary<string, object> environment,
        IReadOnlyList<string> modules)
    {
        var productKeys = environment.Keys
            .Where(static key =>
                key.StartsWith("DigitalBrain", StringComparison.Ordinal)
                || key.StartsWith("DIGITALBRAIN", StringComparison.Ordinal)
                || string.Equals(key, JournalConnectionEnvironmentKey, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            FlutterHostingExtensions.OwnerEnvironmentVariable,
        };
        for (var index = 0; index < modules.Count; index++)
        {
            expected.Add(
                $"{DigitalBrainHostingExtensions.ModulesConfigurationKey.Replace(":", "__", StringComparison.Ordinal)}__{index}");
        }

        Assert.Equal(expected, productKeys);
        for (var index = 0; index < modules.Count; index++)
        {
            Assert.Equal(
                modules[index],
                environment[
                    $"{DigitalBrainHostingExtensions.ModulesConfigurationKey.Replace(":", "__", StringComparison.Ordinal)}__{index}"]
                    ?.ToString());
        }
    }

    public static async Task<HashSet<string>> EnvironmentKeysOf(IResource resource)
    {
        var environment = await EnvironmentOf(resource).ConfigureAwait(true);
        return environment.Keys.ToHashSet(StringComparer.Ordinal);
    }

    public static async Task<Dictionary<string, object>> EnvironmentOf(IResource resource)
    {
        var execution = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run);
        var context = new EnvironmentCallbackContext(execution, resource);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context).ConfigureAwait(true);
        }

        return new Dictionary<string, object>(context.EnvironmentVariables, StringComparer.Ordinal);
    }

    public static async Task<List<string>> ResolvedArgsOf(ExecutableResource resource)
    {
        var args = new List<object>();
        var context = new CommandLineArgsCallbackContext(args, resource, CancellationToken.None);
        foreach (var annotation in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
        {
            await annotation.Callback(context).ConfigureAwait(true);
        }

        return args.Select(static arg => arg?.ToString() ?? string.Empty).ToList();
    }
}
