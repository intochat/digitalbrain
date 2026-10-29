using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DigitalBrain.Templates.Tests.Integration;

// D-07 / APPS-05: `dotnet new intochat-app -n Notes` gives Notes.cs, Notes.spec.md and NotesFacts.cs; the instance builds with
// -warnaserror (so zero analyzer diagnostics) and its generated baseline passes unchanged.
//
// Packages: DIGITALBRAIN_TEMPLATE_PACKAGES names a folder of packed DigitalBrain packages (CI passes the build job's artifacts);
// without it the fact packs the six packages the instance needs into a temporary feed with a unique prerelease version, built
// under a temporary artifacts path so the repository's bin and obj stay untouched. The template is installed into a custom hive.
public sealed class IntochatAppTemplateFacts
{
    private static readonly string[] Packed =
    [
        "DigitalBrain", "DigitalBrain.UI", "DigitalBrain.Testing",
        "DigitalBrain.Capabilities.Storage", "DigitalBrain.Capabilities.Secrets", "DigitalBrain.Capabilities.Identity",
    ];

    [Theory]
    [InlineData("Notes", null, "notes")]
    [InlineData("Foo.Bar", "acme.foo-bar", "acme.foo-bar")]
    public async Task An_instance_builds_with_warnings_as_errors_and_passes_its_baseline(string name, string? appId, string expectedId)
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Root();
        var work = Path.Combine(Path.GetTempPath(), "db-app-template-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            var (feed, version) = await Packages(root, work, ct);
            var hive = Path.Combine(work, "hive");
            var template = Path.Combine(root, "templates", "DigitalBrain.Templates", "content", "intochat-app");
            await Dotnet(work, null, ["new", "install", template, "--debug:custom-hive", hive], ct);

            var app = Path.Combine(work, name);
            string[] create = ["new", "intochat-app", "-n", name, "-o", app, "--DigitalBrainVersion", version, "--debug:custom-hive", hive];
            await Dotnet(work, null, appId is null ? create : [.. create, "--AppId", appId], ct);
            var source = Path.Combine(app, name, name + ".cs");
            Assert.True(File.Exists(source), name + ".cs");
            Assert.True(File.Exists(Path.Combine(app, name, name + ".spec.md")), name + ".spec.md");
            Assert.True(File.Exists(Path.Combine(app, name + ".Tests.Unit", name + "Facts.cs")), name + "Facts.cs");
            Assert.Contains($"[App(\"{expectedId}\")]", await File.ReadAllTextAsync(source, ct), StringComparison.Ordinal);

            await File.WriteAllTextAsync(Path.Combine(app, "nuget.config"), Config(feed), ct);
            var packages = Path.Combine(work, "packages");
            await Dotnet(app, packages, ["build", name + ".slnx", "-c", "Release", "-warnaserror"], ct);
            var test = await Dotnet(app, packages, ["test", "--project", Path.Combine(name + ".Tests.Unit", name + ".Tests.Unit.csproj"), "-c", "Release", "--no-build"], ct);
            Assert.Contains("succeeded: 4", test, StringComparison.Ordinal);
            Assert.Contains("failed: 0", test, StringComparison.Ordinal);

            // The analyzers run in the instance: an operation the spec does not describe fails the build with DB0024.
            var code = await File.ReadAllTextAsync(source, ct);
            var at = code.LastIndexOf('}');
            await File.WriteAllTextAsync(source, code[..at] + "    [On]\n    public Task Close(Opened signal) => Task.CompletedTask;\n}\n", ct);
            var probe = await Dotnet(app, packages, ["build", Path.Combine(name, name + ".csproj"), "-c", "Release"], ct, expectSuccess: false);
            Assert.Contains("DB0024", probe, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<(string Feed, string Version)> Packages(string root, string work, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("DIGITALBRAIN_TEMPLATE_PACKAGES") is { Length: > 0 } given)
        {
            var testing = Directory.GetFiles(given, "DigitalBrain.Testing.*.nupkg").Where(path => !path.EndsWith(".snupkg", StringComparison.Ordinal))
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                ?? throw new InvalidOperationException($"No DigitalBrain.Testing package in {given}.");
            return (Path.GetFullPath(given), Path.GetFileNameWithoutExtension(testing)["DigitalBrain.Testing.".Length..]);
        }
        var suffix = "templatetest." + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var feed = Path.Combine(work, "feed");
        var artifacts = Path.Combine(work, "artifacts");
        // DigitalBrain bundles the code-fix assembly, which none of the packed projects references, so pack's restore misses it.
        await Dotnet(root, null, ["restore", Path.Combine(root, "src", "DigitalBrain.Analyzers.CodeFixes", "DigitalBrain.Analyzers.CodeFixes.csproj"),
            "--artifacts-path", artifacts], ct);
        foreach (var id in Packed)
        {
            await Dotnet(root, null, ["pack", Path.Combine(root, "src", id, id + ".csproj"), "-c", "Release", "-o", feed,
                "--artifacts-path", artifacts, "-p:VersionSuffix=" + suffix, "-p:IncludeSymbols=false"], ct);
        }
        var version = (await File.ReadAllTextAsync(Path.Combine(root, "VERSION"), ct)).Trim() + "-" + suffix;
        return (feed, version);
    }

    // The local feed for DigitalBrain*, the machine's package cache as an offline source, then nuget.org.
    private static string Config(string feed)
    {
        var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } set
            ? set
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{feed}" />
                <add key="cache" value="{cache}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="local">
                  <package pattern="DigitalBrain*" />
                </packageSource>
                <packageSource key="cache">
                  <package pattern="*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """;
    }

    private static async Task<string> Dotnet(string directory, string? packages, string[] arguments, CancellationToken ct, bool expectSuccess = true)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) { start.ArgumentList.Add(argument); }
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        // The test host's own MSBuild and runner settings must not leak into the child build.
        foreach (var name in new[] { "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "DOTNET_HOST_PATH", "TESTINGPLATFORM_DOTNETTEST_EXECUTIONID" })
        {
            start.Environment.Remove(name);
        }
        if (packages is not null) { start.Environment["NUGET_PACKAGES"] = packages; }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
        var output = new StringBuilder();
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        output.Append(await stdout).Append(await stderr);
        Assert.True(process.ExitCode == 0 == expectSuccess, $"dotnet {string.Join(' ', arguments)} exited {process.ExitCode} (expected {(expectSuccess ? "success" : "failure")}) in {directory}:\n{output}");
        return output.ToString();
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx"))) { return directory.FullName; }
        }
        throw new InvalidOperationException("DigitalBrain.slnx not found above " + AppContext.BaseDirectory);
    }
}
