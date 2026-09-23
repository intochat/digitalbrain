using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DigitalBrain.Microsoft.DotNet;

namespace DigitalBrain.Coding;

internal sealed class CodeValidationService(CodeExecutionOptions options)
{
    public BuildEnvironment Environment()
    {
        var assemblies = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in options.ReferencePaths.Concat(options.TestReferencePaths).Concat(options.Modules.Values.SelectMany(x => x)).Distinct(StringComparer.OrdinalIgnoreCase))
        { assemblies.Add(Path.GetFullPath(path), ArtifactStore.Hash(File.ReadAllBytes(path))); }
        return new(options.SdkVersion, RuntimeInformation.RuntimeIdentifier,
            ArtifactStore.Hash(Encoding.UTF8.GetBytes(BehaviorBuildTemplate.Version + BehaviorBuildTemplate.Project([], false)
                + BehaviorBuildTemplate.Project([], true) + BehaviorBuildTemplate.Conformance)), "validator-1", assemblies);
    }

    public ArtifactStore Store() => new(Path.Combine(RequiredRoot(), "artifacts"), Environment, options.MaximumArtifactBytes);
    public string RequiredRoot() => Path.GetFullPath(options.Root ?? throw new InvalidOperationException("Configure DigitalBrain:Coding:Execution:Root to enable managed drafts."));

    public async Task<CodeCheckSnapshot> ValidateAsync(CodeDraftSnapshot draft, CodeCheckSnapshot operation,
        Func<CodeCheckStatus, Task> progress, CancellationToken ct)
    {
        var environment = Environment();
        var references = options.ReferencePaths.ToList();
        foreach (var module in draft.ModuleIds)
        {
            if (!options.Modules.TryGetValue(module, out var paths)) { throw new ArgumentException($"Module '{module}' is not installed for code execution."); }
            references.AddRange(paths);
        }
        var directory = Path.Combine(RequiredRoot(), "builds", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Empty boundary files prevent arbitrary ancestor build/package settings from being imported.
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.props"), "<Project />", ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.targets"), "<Project />", ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Packages.props"), "<Project />", ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(directory, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>", ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(directory, "global.json"), JsonSerializer.Serialize(new { sdk = new { version = options.SdkVersion, rollForward = "disable", allowPrerelease = true }, test = new { runner = "Microsoft.Testing.Platform" } }), ct).ConfigureAwait(false);
            var app = Path.Combine(directory, "app");
            var tests = Path.Combine(directory, "tests");
            Directory.CreateDirectory(app);
            Directory.CreateDirectory(tests);
            await File.WriteAllTextAsync(Path.Combine(app, "Program.cs"), draft.Source, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(app, "Behavior.csproj"), BehaviorBuildTemplate.Project(references, false), ct).ConfigureAwait(false);
            var runner = new ContainedProcessRunner();
            await progress(CodeCheckStatus.Building).ConfigureAwait(false);
            var built = await runner.RunAsync(options.DotnetPath, ["build", "Behavior.csproj", "-c", "Release", "--nologo"], app, options.BuildTimeout, ct).ConfigureAwait(false);
            RequireSuccess(built, "Build");
            var payload = Path.Combine(app, "bin", "Release", "net11.0");
            var behaviorAssembly = Path.Combine(payload, "Behavior.dll");
            var payloadHashes = await ArtifactStore.HashFiles(payload, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(tests, "Tests.cs"), draft.Tests, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(tests, "Conformance.cs"), BehaviorBuildTemplate.Conformance, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(tests, "Behavior.Tests.csproj"), BehaviorBuildTemplate.Project(references.Concat(options.TestReferencePaths).Append(behaviorAssembly), true), ct).ConfigureAwait(false);
            await progress(CodeCheckStatus.Testing).ConfigureAwait(false);
            var testBuild = await runner.RunAsync(options.DotnetPath, ["build", "Behavior.Tests.csproj", "-c", "Release", "--nologo"], tests, options.BuildTimeout, ct).ConfigureAwait(false);
            RequireSuccess(testBuild, "Test build");
            var result = await runner.RunAsync(options.DotnetPath,
                [Path.Combine(tests, "bin", "Release", "net11.0", "Behavior.Tests.dll"), "--report-xunit-xml", "--report-xunit-xml-filename", "report.xml", "--results-directory", tests],
                tests, options.TestTimeout, ct).ConfigureAwait(false);
            RequireSuccess(result, "Tests");
            var report = CodeTestReportReader.Read(await File.ReadAllTextAsync(Path.Combine(tests, "report.xml"), ct).ConfigureAwait(false));
            if (!payloadHashes.SequenceEqual(await ArtifactStore.HashFiles(payload, ct).ConfigureAwait(false))
                || ArtifactStore.EnvironmentHash(environment) != ArtifactStore.EnvironmentHash(Environment()))
            { throw new InvalidDataException("Build inputs changed during validation."); }
            var artifact = await Store().SealAsync(new(draft.Source, draft.Tests, environment, payload, "Behavior.dll", report), ct).ConfigureAwait(false);
            return operation with
            {
                Status = CodeCheckStatus.Passed,
                CompletedAt = DateTimeOffset.UtcNow,
                Artifact = artifact,
                Tests = new(report.Discovered, report.Passed, report.Failed, report.Skipped)
            };
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void RequireSuccess(ProcessResult result, string phase)
    {
        if (result.TimedOut) { throw new TimeoutException(phase + " exceeded its execution deadline."); }
        if (result.ExitCode != 0) { throw new CodeValidationException(phase, result.Output + result.Error); }
    }
}