using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DigitalBrain.Broker.Hosting;

// The banned-API verification step for code apps: a package's assemblies are read as metadata only
// (never loaded) and every type reference is checked against the banned list. A reference to
// System.IO.File, System.Diagnostics.Process, AssemblyLoadContext and friends fails certification.
public sealed class BannedApiAnalyzer(IEnumerable<string>? bannedApis = null)
{
    public static IReadOnlyList<string> DefaultBannedApis { get; } = AssemblyTrustVerifier.DefaultBannedApis;

    private readonly IReadOnlySet<string> bannedApis = new HashSet<string>(
        bannedApis ?? DefaultBannedApis, StringComparer.Ordinal);

    public IReadOnlyList<string> Analyze(byte[] assemblyBytes)
    {
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        try
        {
            using var stream = new MemoryStream(assemblyBytes, writable: false);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
            {
                return [];
            }

            var metadata = pe.GetMetadataReader();
            var findings = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var handle in metadata.TypeReferences)
            {
                var reference = metadata.GetTypeReference(handle);
                var name = metadata.GetString(reference.Name);
                var @namespace = metadata.GetString(reference.Namespace);
                var fullName = string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
                if (bannedApis.Contains(fullName))
                {
                    findings.Add(fullName);
                }
            }
            return [.. findings];
        }
        catch (BadImageFormatException)
        {
            return [];
        }
    }
}