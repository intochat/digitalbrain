using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Apps;

namespace DigitalBrain.Broker;

// A `process` app ships as a signed `.nupkg` of package type IntoChatApp. The manifest lives at
// intochat/app.json, the sandbox declaration at intochat/sandbox.json and the publisher signature
// at intochat/signature.json. The signature is verified before activation, never after.
public sealed record SandboxDeclaration
{
    public required string Image { get; init; }
    public IReadOnlyList<string> Command { get; init; } = [];
}

public sealed record ProcessAppAssembly
{
    public required string Name { get; init; }
    public required byte[] Bytes { get; init; }
}

public sealed record ProcessPackageSignature
{
    public required string Publisher { get; init; }
    public required string PublicKey { get; init; }
    public required string Signature { get; init; }
}

public sealed record ProcessAppPackage
{
    public required string PackageType { get; init; }
    public required AppManifest Manifest { get; init; }
    public required SandboxDeclaration Sandbox { get; init; }
    public required byte[] ContentHash { get; init; }
    public ProcessPackageSignature? Signature { get; init; }
    public IReadOnlyList<ProcessAppAssembly> Assemblies { get; init; } = [];
}

public sealed record ProcessPackageVerification
{
    public required bool Allowed { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = [];

    public static ProcessPackageVerification Allow() => new() { Allowed = true };
}

public interface IProcessAppPackageReader
{
    ProcessAppPackage Read(Stream nupkg);
}

public interface IProcessPackageVerifier
{
    ProcessPackageVerification Verify(ProcessAppPackage package);
}

public sealed class ProcessPackageException(string message) : Exception(message);

// The hash the publisher signs. It is stable across packing runs: entries are sorted and
// length-prefixed so two packages with identical content always produce the same hash.
public static class ProcessPackageContentHash
{
    public const string PackageTypeName = "IntoChatApp";
    public const string ManifestPath = "intochat/app.json";
    public const string SandboxPath = "intochat/sandbox.json";
    public const string SignaturePath = "intochat/signature.json";

    public static byte[] Compute(byte[] appJson, byte[] sandboxJson, IReadOnlyList<ProcessAppAssembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(appJson);
        ArgumentNullException.ThrowIfNull(sandboxJson);
        ArgumentNullException.ThrowIfNull(assemblies);

        using var payload = new MemoryStream();
        Write(payload, appJson);
        Write(payload, sandboxJson);
        foreach (var assembly in assemblies.OrderBy(assembly => assembly.Name, StringComparer.Ordinal))
        {
            Write(payload, Encoding.UTF8.GetBytes(assembly.Name));
            Write(payload, assembly.Bytes);
        }
        return SHA256.HashData(payload.ToArray());
    }

    private static void Write(Stream stream, byte[] data)
    {
        stream.Write(BitConverter.GetBytes(data.Length));
        stream.Write(data);
    }
}