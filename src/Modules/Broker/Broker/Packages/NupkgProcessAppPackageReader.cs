using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DigitalBrain.Apps;

namespace DigitalBrain.Broker.Packages;

// Reads a `.nupkg` produced with package type IntoChatApp. Nothing is loaded: the reader only
// unpacks the manifest, the sandbox declaration, the publisher signature and the code assemblies,
// and computes the signed content hash. Verification is a separate step.
internal sealed class NupkgProcessAppPackageReader : IProcessAppPackageReader
{
    public ProcessAppPackage Read(Stream nupkg)
    {
        ArgumentNullException.ThrowIfNull(nupkg);
        using var archive = new ZipArchive(nupkg, ZipArchiveMode.Read, leaveOpen: true);

        var packageType = ReadPackageType(archive);
        if (!string.Equals(packageType, ProcessPackageContentHash.PackageTypeName, StringComparison.Ordinal))
        {
            throw new ProcessPackageException(
                $"Package type '{packageType}' is not '{ProcessPackageContentHash.PackageTypeName}'.");
        }

        var appJson = ReadRequired(archive, ProcessPackageContentHash.ManifestPath);
        var manifest = AppManifestJson.Deserialize(Encoding.UTF8.GetString(appJson));
        ManifestValidator.Validate(manifest);
        if (manifest.Kind != AppKind.Process)
        {
            throw new ProcessPackageException($"The embedded manifest kind '{manifest.Kind}' is not 'process'.");
        }

        var sandboxJson = ReadRequired(archive, ProcessPackageContentHash.SandboxPath);
        var sandbox = JsonSerializer.Deserialize<SandboxDeclaration>(sandboxJson, AppManifestJson.Options)
            ?? throw new ProcessPackageException("The embedded sandbox declaration is empty.");
        if (string.IsNullOrWhiteSpace(sandbox.Image))
        {
            throw new ProcessPackageException("The sandbox declaration names no container image.");
        }

        var signatureEntry = archive.GetEntry(ProcessPackageContentHash.SignaturePath);
        var signature = signatureEntry is null
            ? null
            : JsonSerializer.Deserialize<ProcessPackageSignature>(ReadAll(signatureEntry), AppManifestJson.Options);

        var assemblies = archive.Entries
            .Where(entry => entry.FullName.StartsWith("lib/", StringComparison.Ordinal)
                && entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
            .Select(entry => new ProcessAppAssembly { Name = entry.FullName, Bytes = ReadAll(entry) })
            .ToArray();

        return new ProcessAppPackage
        {
            PackageType = packageType,
            Manifest = manifest,
            Sandbox = sandbox,
            ContentHash = ProcessPackageContentHash.Compute(appJson, sandboxJson, assemblies),
            Signature = signature,
            Assemblies = assemblies,
        };
    }

    private static string ReadPackageType(ZipArchive archive)
    {
        var nuspec = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (nuspec is null)
        {
            return string.Empty;
        }
        using var stream = nuspec.Open();
        var document = XDocument.Load(stream);
        return document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "packageType")?.Attribute("name")?.Value ?? string.Empty;
    }

    private static byte[] ReadRequired(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path)
            ?? throw new ProcessPackageException($"The package does not embed '{path}'.");
        return ReadAll(entry);
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}