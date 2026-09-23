using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Apps;
using DigitalBrain.Broker;

namespace DigitalBrain.Tests;

// Builds signed `process` packages both directly and as real `.nupkg` archives, so the reader,
// the verifier and the gateway can all be exercised on the same deterministic fixture.
internal static class ProcessAppFixtures
{
    public const string Image = "acme/process-app:1.0.0";

    public static AppManifest Manifest(string id = "acme.sample-process", string publisher = "acme") => new()
    {
        Id = id,
        Version = "1.0.0",
        Publisher = publisher,
        Kind = AppKind.Process,
        Name = "Acme Sample",
        DescriptionForPeople = "A sandboxed sample process app.",
        DescriptionForModel = "Echoes its input from inside a per-app sandbox.",
        Operations =
        [
            new AppOperation
            {
                Name = "Echo",
                DescriptionForModel = "Echo the input.",
                ReadOnly = true,
                InputTypeIds = new Dictionary<string, string> { ["text"] = "plain-text" },
            },
        ],
        Permissions = [new AppPermission { SemanticTypeId = "plain-text", Reason = "Echoes text." }],
        Meters = [new AppMeter { MeterId = "sample.call", Unit = "call", Aggregation = "sum" }],
        Scenarios = [new AppScenario { Name = "echo", Given = "text", When = "Echo runs", Then = "the text comes back" }],
    };

    public static ProcessAppPackage Signed(
        AppManifest? manifest = null,
        ProcessAppAssembly[]? assemblies = null,
        string? image = null,
        string? command = null)
    {
        var app = manifest ?? Manifest();
        var sandbox = new SandboxDeclaration
        {
            Image = image ?? Image,
            Command = command is null ? ["run"] : [command],
        };
        var contentHash = Hash(app, sandbox, assemblies ?? []);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        var signature = Convert.ToBase64String(ecdsa.SignHash(contentHash));
        return new ProcessAppPackage
        {
            PackageType = ProcessPackageContentHash.PackageTypeName,
            Manifest = app,
            Sandbox = sandbox,
            ContentHash = contentHash,
            Signature = new ProcessPackageSignature
            {
                Publisher = app.Publisher,
                PublicKey = publicKey,
                Signature = signature,
            },
            Assemblies = assemblies ?? [],
        };
    }

    public static Dictionary<string, string> TrustedKeys(ProcessAppPackage package) =>
        new(StringComparer.Ordinal) { [package.Manifest.Publisher] = package.Signature!.PublicKey };

    public static (MemoryStream Nupkg, Dictionary<string, string> TrustedKeys, AppManifest Manifest) BuildNupkg(
        AppManifest? manifest = null,
        ProcessAppAssembly[]? assemblies = null,
        bool signed = true,
        bool trusted = true,
        string packageType = ProcessPackageContentHash.PackageTypeName)
    {
        var app = manifest ?? Manifest();
        var sandbox = new SandboxDeclaration { Image = Image, Command = ["run"] };
        var appJson = Encoding.UTF8.GetBytes(AppManifestJson.Serialize(app));
        var sandboxJson = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(sandbox, AppManifestJson.Options));
        var code = assemblies ?? [];
        var contentHash = ProcessPackageContentHash.Compute(appJson, sandboxJson, code);

        string? publicKey = null;
        string? signature = null;
        if (signed)
        {
            using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            publicKey = trusted
                ? Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())
                : Convert.ToBase64String(ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportSubjectPublicKeyInfo());
            signature = Convert.ToBase64String(signer.SignHash(contentHash));
        }

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(archive, $"{app.Id}.nuspec",
                $"<?xml version=\"1.0\" encoding=\"utf-8\"?><package><metadata><id>{app.Id}</id>" +
                $"<packageTypes><packageType name=\"{packageType}\" /></packageTypes></metadata></package>");
            AddBytes(archive, ProcessPackageContentHash.ManifestPath, appJson);
            AddBytes(archive, ProcessPackageContentHash.SandboxPath, sandboxJson);
            if (publicKey is not null)
            {
                AddText(archive, ProcessPackageContentHash.SignaturePath,
                    JsonSerializer.Serialize(new ProcessPackageSignature
                    {
                        Publisher = app.Publisher,
                        PublicKey = publicKey,
                        Signature = signature!,
                    }, AppManifestJson.Options));
            }
            foreach (var assembly in code)
            {
                AddBytes(archive, assembly.Name, assembly.Bytes);
            }
        }
        stream.Position = 0;

        var keys = publicKey is not null && trusted
            ? new Dictionary<string, string>(StringComparer.Ordinal) { [app.Publisher] = publicKey }
            : new Dictionary<string, string>(StringComparer.Ordinal);
        return (stream, keys, app);
    }

    public static byte[] TestAssemblyBytes() => File.ReadAllBytes(typeof(ProcessAppFixtures).Assembly.Location);

    private static byte[] Hash(AppManifest manifest, SandboxDeclaration sandbox, IReadOnlyList<ProcessAppAssembly> assemblies) =>
        ProcessPackageContentHash.Compute(
            Encoding.UTF8.GetBytes(AppManifestJson.Serialize(manifest)),
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(sandbox, AppManifestJson.Options)),
            assemblies);

    private static void AddText(ZipArchive archive, string path, string content) =>
        AddBytes(archive, path, Encoding.UTF8.GetBytes(content));

    private static void AddBytes(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        stream.Write(content);
    }
}