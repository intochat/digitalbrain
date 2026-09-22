namespace DigitalBrain.Coding;

public sealed record BuildEnvironment(string Sdk, string Platform, string TemplateHash,
    string ValidatorVersion, IReadOnlyDictionary<string, string> Assemblies);

public sealed record CodeTestReport(int Discovered, int Passed, int Failed, int Skipped, string ReportHash);

public sealed record VerifiedBuild(string Source, string Tests, BuildEnvironment Environment,
    string PayloadDirectory, string EntryAssembly, CodeTestReport Report);

public sealed record ArtifactManifest(string Source, string Tests, BuildEnvironment Environment,
    string EntryAssembly, CodeTestReport Report, IReadOnlyDictionary<string, string> Files);

public sealed record VerifiedArtifact(CodeArtifactRef Reference, ArtifactManifest Manifest,
    string LaunchDirectory, string EntryAssembly);

public interface ICodeArtifactStore
{
    Task<CodeArtifactRef> SealAsync(VerifiedBuild build, CancellationToken cancellationToken);
    Task<VerifiedArtifact> OpenVerifiedAsync(CodeArtifactRef reference, CancellationToken cancellationToken);
}
