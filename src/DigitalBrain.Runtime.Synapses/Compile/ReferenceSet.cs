using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>The metadata references a synapse compiles against, with a fingerprint that feeds the build hash.</summary>
public sealed class ReferenceSet
{
    private static readonly Lazy<ReferenceSet> Default = new(Build);

    /// <summary>Creates a set over the given file-backed references.</summary>
    /// <param name="references">The references; each must be a <see cref="PortableExecutableReference"/> with a file path.</param>
    internal ReferenceSet(IReadOnlyList<MetadataReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        References = references;
        var identities = references
            .Select(reference => AssemblyName.GetAssemblyName(((PortableExecutableReference)reference).FilePath!))
            .Select(name => $"{name.Name}|{name.Version}")
            .Order(StringComparer.Ordinal);
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', identities))));
    }

    /// <summary>The metadata references.</summary>
    public IReadOnlyList<MetadataReference> References { get; }

    /// <summary>SHA-256 hex over the sorted <c>name|version</c> of every reference.</summary>
    public string Fingerprint { get; }

    /// <summary>
    /// The default-context set: every assembly loaded in <see cref="AssemblyLoadContext.Default"/> (the framework, its
    /// contracts, loaded capability contracts) plus the trusted platform assemblies, closed by PE assembly references.
    /// Built once per process.
    /// </summary>
    /// <returns>The shared default set.</returns>
    public static ReferenceSet FromDefaultContext() => Default.Value;

    private static ReferenceSet Build()
    {
        var available = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
        {
            if (!assembly.IsDynamic && assembly.Location.Length > 0 && assembly.GetName().Name is { } name)
            {
                available.TryAdd(name, assembly.Location);
            }
        }

        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(File.Exists);
        foreach (var path in trusted)
        {
            available.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }

        // Every candidate is a seed; the walk keeps only real assemblies and follows their references within the candidates.
        var carried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(available.Keys);
        while (pending.TryPop(out var name))
        {
            if (!visited.Add(name))
            {
                continue;
            }

            List<string> dependencies;
            try
            {
                using var image = new PEReader(File.OpenRead(available[name]));
                if (!image.HasMetadata)
                {
                    continue;
                }

                var metadata = image.GetMetadataReader();
                if (!metadata.IsAssembly)
                {
                    continue;
                }

                dependencies = [.. metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))];
            }
            catch (BadImageFormatException)
            {
                // A non-PE file on the trusted list is not a reference.
                continue;
            }

            carried.Add(name);
            foreach (var dependency in dependencies)
            {
                if (available.ContainsKey(dependency) && !visited.Contains(dependency))
                {
                    pending.Push(dependency);
                }
            }
        }

        return new ReferenceSet([.. carried.Order(StringComparer.OrdinalIgnoreCase).Select(name => (MetadataReference)MetadataReference.CreateFromFile(available[name]))]);
    }
}
