using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Flutter.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

internal static class FlutterHostingProjectionSupport
{
    public static readonly string RepositoryRoot = LocateRepositoryRoot();

    public static string UiProjectPath => Path.Combine(
        RepositoryRoot,
        "hosts",
        "DigitalBrain.Ui",
        "DigitalBrain.Ui.csproj");

    public static string FlutterClientDirectory => Path.Combine(
        RepositoryRoot,
        "clients",
        "digitalbrain_flutter");

    public static string FlutterShellDirectory => Path.Combine(
        FlutterClientDirectory,
        "shell");

    public static async Task AssertShellDesktopLayoutAsync(
        string shellDirectory,
        CancellationToken cancellationToken = default)
    {
        Assert.True(
            File.Exists(Path.Combine(shellDirectory, "pubspec.yaml")),
            "clients/digitalbrain_flutter/shell must exist for desktop chrome.");
        Assert.True(
            File.Exists(Path.Combine(shellDirectory, "lib", "main.dart")),
            "shell Windows chrome requires lib/main.dart (Auto discovers shell/ under pure-Dart root).");
        Assert.True(
            Directory.Exists(Path.Combine(shellDirectory, "windows")),
            "shell Windows chrome requires windows/ (Auto discovers shell/ under pure-Dart root).");
        Assert.False(
            File.Exists(Path.Combine(
                shellDirectory,
                FlutterHostingExtensions.HeadlessHostEntry.Replace('/', Path.DirectorySeparatorChar))),
            "shell is desktop-only — headless entry stays on pure-Dart root.");
        var pubspec = await File.ReadAllTextAsync(
            Path.Combine(shellDirectory, "pubspec.yaml"),
            cancellationToken).ConfigureAwait(true);
        Assert.Contains("sdk: flutter", pubspec, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task AssertPureDartClientLayoutAsync(
        string clientDirectory,
        CancellationToken cancellationToken = default)
    {
        Assert.True(
            File.Exists(Path.Combine(clientDirectory, "pubspec.yaml")),
            "clients/digitalbrain_flutter must exist.");
        Assert.True(
            File.Exists(Path.Combine(
                clientDirectory,
                FlutterHostingExtensions.HeadlessHostEntry.Replace('/', Path.DirectorySeparatorChar))),
            "pure-Dart package hosts bin/digitalbrain_host.dart.");
        Assert.False(
            File.Exists(Path.Combine(clientDirectory, "lib", "main.dart")),
            "desktop entry moved to shell/ — root must not claim lib/main.dart.");
        Assert.False(
            Directory.Exists(Path.Combine(clientDirectory, "windows")),
            "desktop runner moved to shell/ — root must not claim windows/.");
        var pubspec = await File.ReadAllTextAsync(
            Path.Combine(clientDirectory, "pubspec.yaml"),
            cancellationToken).ConfigureAwait(true);
        Assert.DoesNotContain("sdk: flutter", pubspec, StringComparison.OrdinalIgnoreCase);
    }

    public static void AssertNoOsSurfaceResources(IDistributedApplicationBuilder builder)
    {
        var surface = builder.Resources
            .Where(static resource => resource.Name is FlutterHostingExtensions.DefaultUiResourceName
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

    public static void AssertUiHasNamedHttpEndpoint(IResource ui)
    {
        var http = Assert.Single(
            ui.Annotations.OfType<EndpointAnnotation>(),
            endpoint => string.Equals(
                endpoint.Name,
                FlutterHostingExtensions.UiHttpEndpointName,
                StringComparison.Ordinal));
        Assert.Equal("http", http.UriScheme, StringComparer.OrdinalIgnoreCase);
    }

    public static void AssertExclusiveFlutterHostEnvironment(HashSet<string> environment)
    {
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                FlutterHostingExtensions.UiBaseEnvironmentVariable,
                FlutterHostingExtensions.ShellEnvironmentVariable,
            },
            environment);
    }

    public static void AssertExclusiveUiProductEnvironment(IReadOnlyDictionary<string, object> environment)
    {
        var productKeys = environment.Keys
            .Where(static key =>
                key.StartsWith("DigitalBrain", StringComparison.Ordinal)
                || key.StartsWith("DIGITALBRAIN", StringComparison.Ordinal)
                || string.Equals(key, "ConnectionStrings__journal", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                FlutterHostingExtensions.OwnerEnvironmentVariable,
            },
            productKeys);
    }

    public static void AssertNoOsSurfaceHandWire(string appHost)
    {
        Assert.DoesNotContain(
            "builder.AddProject<Projects.DigitalBrain_Ui>",
            appHost,
            StringComparison.Ordinal);
        Assert.DoesNotContain("digitalbrain-ui", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("digitalbrain-flutter", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("DIGITALBRAIN_UI_BASE", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("DIGITALBRAIN_SHELL", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("AddExecutable", appHost, StringComparison.Ordinal);
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

    public static string MethodBody(string source, string signatureMarker)
    {
        var signatureIndex = source.IndexOf(signatureMarker, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Signature marker '{signatureMarker}' was not found.");

        var openBrace = source.IndexOf('{', signatureIndex);
        Assert.True(openBrace >= 0, $"Opening brace after '{signatureMarker}' was not found.");

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[(openBrace + 1)..index];
                }
            }
        }

        throw new InvalidOperationException($"Could not balance braces for '{signatureMarker}'.");
    }

    public static int CountOccurrences(string source, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("DigitalBrain.slnx was not found above the test assembly.");
    }
}
