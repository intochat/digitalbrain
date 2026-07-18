namespace DigitalBrain.FeatureBuilder;

public sealed class FeatureBuildPipeline
{
    public static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan CompileAndScenarioTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaximumRequestDuration = TimeSpan.FromSeconds(70);
    private readonly TimeProvider _timeProvider;
    private readonly FeatureReleaseWriter _releaseWriter;
    public FeatureBuildPipeline()
        : this(TimeProvider.System, new FeatureReleaseWriter())
    {
    }
    public FeatureBuildPipeline(TimeProvider timeProvider, FeatureReleaseWriter releaseWriter)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _releaseWriter = releaseWriter ?? throw new ArgumentNullException(nameof(releaseWriter));
    }
    public async Task<FeatureRelease> BuildAsync(FeatureBuildRequest request, CancellationToken cancellationToken = default)
    {
        var verification = await VerifyAsync(request, cancellationToken);
        return verification.Release
            ?? throw new FeatureBuildException(
                FeatureBuildFailure.ScenarioFailed,
                "Feature scenarios must contain at least one test and pass with no failures or skips.");
    }
    public async Task<FeatureBuildVerification> VerifyAsync(FeatureBuildRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDeadline(request.Deadline);
        FeatureBuildSource.Validate(request.Source);
        cancellationToken.ThrowIfCancellationRequested();
        var sourceReference = FeatureReleaseWriter.ComputeSourceReference(request.Source);
        var workspace = Path.Combine(Path.GetTempPath(), "digitalbrain-feature-builds", Guid.NewGuid().ToString("N"));
        try
        {
            await FeatureBuildSource.MaterializeAsync(workspace, request.Source, cancellationToken);
            var nugetConfig = await FeatureBuildSource.WriteNuGetConfigAsync(workspace, request.OfflineFeedDirectory, cancellationToken);
            var packagesDirectory = Path.Combine(workspace, ".packages");
            var implementationProject = FeatureBuildSource.LocalPath(workspace, request.Source.ImplementationProjectPath);
            var scenarioProject = FeatureBuildSource.LocalPath(workspace, request.Source.ScenarioProjectPath);
            var process = new FeatureBuildProcess(_timeProvider);
            await process.RunAsync(
                workspace,
                request.Deadline,
                RestoreTimeout,
                FeatureBuildFailure.RestoreFailed,
                cancellationToken,
                "restore",
                scenarioProject,
                "--configfile",
                nugetConfig,
                "--packages",
                packagesDirectory,
                "--no-http-cache",
                "--force-evaluate",
                "--disable-build-servers",
                "-p:NuGetAudit=false",
                "--verbosity",
                "quiet");
            var compileDeadline = Minimum(request.Deadline, _timeProvider.GetUtcNow().Add(CompileAndScenarioTimeout));
            var buildOutput = Path.Combine(workspace, "build", "implementation");
            await process.RunAsync(
                workspace,
                compileDeadline,
                CompileAndScenarioTimeout,
                FeatureBuildFailure.CompilationFailed,
                cancellationToken,
                "build",
                implementationProject,
                "--configuration",
                "Release",
                "--no-restore",
                "--disable-build-servers",
                "--output",
                buildOutput,
                "-p:Deterministic=true",
                "-p:ContinuousIntegrationBuild=true",
                "-p:DebugType=None",
                "-p:DebugSymbols=false",
                $"-p:PathMap={workspace}=/_/src",
                "-p:UseSharedCompilation=false",
                "--nologo",
                "--verbosity",
                "quiet");
            var implementationAssembly = FeatureManifestDeriver.AssemblyName(request.Source, request.Source.ImplementationProjectPath);
            var manifest = FeatureManifestDeriver.Derive(buildOutput, implementationAssembly);
            await process.RunAsync(
                workspace,
                compileDeadline,
                CompileAndScenarioTimeout,
                FeatureBuildFailure.CompilationFailed,
                cancellationToken,
                "build",
                scenarioProject,
                "--configuration",
                "Release",
                "--no-restore",
                "--disable-build-servers",
                "-p:Deterministic=true",
                "-p:ContinuousIntegrationBuild=true",
                "-p:DebugType=None",
                "-p:DebugSymbols=false",
                $"-p:PathMap={workspace}=/_/src",
                "-p:UseSharedCompilation=false",
                "--nologo",
                "--verbosity",
                "quiet");
            var scenarioAssembly = FeatureManifestDeriver.AssemblyName(request.Source, request.Source.ScenarioProjectPath);
            var scenarioAssemblyPath = Path.Combine(Path.GetDirectoryName(scenarioProject)!, "bin", "Release", "net11.0", scenarioAssembly);
            var expectedScenarioCount = FeatureManifestDeriver.ValidateScenarioAssembly(scenarioAssemblyPath, implementationAssembly, FeatureBuildSource.ExpectedScenarioCount(request.Source));
            ValidateScenarioDependencies(request.Source, Path.GetDirectoryName(scenarioAssemblyPath)!, scenarioAssembly);
            var resultsDirectory = Path.Combine(workspace, "build", "results");
            Directory.CreateDirectory(resultsDirectory);
            var scenarioProcess = await process.RunForEvidenceAsync(
                workspace,
                compileDeadline,
                CompileAndScenarioTimeout,
                cancellationToken,
                "test",
                scenarioProject,
                "--configuration",
                "Release",
                "--no-restore",
                "--no-build",
                "--disable-build-servers",
                "--logger",
                "trx;LogFileName=scenarios.trx",
                "--results-directory",
                resultsDirectory,
                "-p:Deterministic=true",
                "-p:ContinuousIntegrationBuild=true",
                "-p:DebugType=None",
                "-p:DebugSymbols=false",
                $"-p:PathMap={workspace}=/_/src",
                "-p:UseSharedCompilation=false",
                "--nologo",
                "--verbosity",
                "quiet");
            var scenarios = FeatureScenarioResultReader.Read(Path.Combine(resultsDirectory, "scenarios.trx"), expectedScenarioCount);
            var artifacts = FeatureReleaseWriter.DescribeEvidence(sourceReference, scenarios);
            if (!scenarioProcess.Succeeded || scenarios.Failed != 0 || scenarios.Skipped != 0 || scenarios.Passed != scenarios.Total)
            {
                return new FeatureBuildVerification(sourceReference, scenarios, artifacts, null);
            }
            var remaining = request.Deadline - _timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                throw FeatureBuildDeadline.Expired();
            }
            using var releaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            releaseCancellation.CancelAfter(remaining);
            try
            {
                var release = await _releaseWriter.WriteAsync(request.OutputDirectory, sourceReference, buildOutput, manifest, scenarios, releaseCancellation.Token);
                return new FeatureBuildVerification(sourceReference, scenarios, release.Artifacts, release);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw FeatureBuildDeadline.Expired();
            }
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, true);
            }
        }
    }
    private static void ValidateScenarioDependencies(FeatureSourceSnapshot source, string outputDirectory, string scenarioAssembly)
    {
        foreach (var project in source.Files.Where(static file => file.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            var assembly = FeatureManifestDeriver.AssemblyName(source, project.Path);
            if (assembly.Equals(scenarioAssembly, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var assemblyPath = Path.Combine(outputDirectory, assembly);
            if (File.Exists(assemblyPath))
            {
                FeatureManifestDeriver.ValidateScenarioDependency(assemblyPath);
            }
        }
    }
    private void ValidateDeadline(DateTimeOffset deadline)
    {
        var remaining = deadline - _timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw FeatureBuildDeadline.Expired();
        }
        if (remaining > MaximumRequestDuration)
        {
            throw new FeatureBuildException(FeatureBuildFailure.InvalidSource, $"A Feature build deadline cannot exceed {MaximumRequestDuration.TotalSeconds:0} seconds.");
        }
    }
    private static DateTimeOffset Minimum(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;
}
internal static class FeatureBuildDeadline
{
    internal static FeatureBuildException Expired() =>
        new(FeatureBuildFailure.DeadlineExceeded, "The Feature build deadline expired.");
}
