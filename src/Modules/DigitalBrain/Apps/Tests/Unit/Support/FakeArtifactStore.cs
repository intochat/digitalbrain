using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Apps;
using DigitalBrain.Coding;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

// Stands in for the Coding check: sealing records exactly the source and tests an artifact was built from.
internal sealed class FakeArtifactStore : ICodeArtifactStore
{
    private readonly ConcurrentDictionary<string, VerifiedArtifact> _sealed = new(StringComparer.Ordinal);

    public CodeArtifactRef Seal(PackageContent content)
    {
        var sourceHash = Hash(content.Source);
        var reference = new CodeArtifactRef("artifact-" + Hash(content.Source + "\0" + content.Tests), sourceHash, "environment");
        var manifest = new ArtifactManifest(content.Source, content.Tests,
            new BuildEnvironment("sdk", "platform", "template", "validator", new Dictionary<string, string>()),
            "Behavior.dll", new CodeTestReport(1, 1, 0, 0, "report"), new Dictionary<string, string>());
        _sealed[reference.Id] = new VerifiedArtifact(reference, manifest, "payload", "Behavior.dll");
        return reference;
    }

    public Task<CodeArtifactRef> SealAsync(VerifiedBuild build, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<VerifiedArtifact> OpenVerifiedAsync(CodeArtifactRef reference, CancellationToken cancellationToken)
        => _sealed.TryGetValue(reference.Id, out var artifact) && artifact.Reference == reference
            ? Task.FromResult(artifact)
            : throw new InvalidDataException("The artifact is missing or was modified.");

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
