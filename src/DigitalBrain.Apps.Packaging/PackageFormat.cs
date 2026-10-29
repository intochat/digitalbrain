using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain.Apps.Packaging;

internal static class PackageFormat
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static CompiledAppDescriptor Canonical(CompiledAppDescriptor descriptor) => descriptor with
    {
        Capabilities = [.. descriptor.Capabilities.Order(StringComparer.Ordinal)],
        Neurons = [.. descriptor.Neurons.Select(n => n with
        {
            State = n.State is null ? null : CanonicalContract(n.State),
            LegacyOwnership = [.. n.LegacyOwnership.OrderBy(o => o.Kind, StringComparer.Ordinal).ThenBy(o => o.Value, StringComparer.Ordinal)]
        }).OrderBy(n => n.TypeName, StringComparer.Ordinal)],
        Contracts = [.. descriptor.Contracts.Select(CanonicalContract).OrderBy(n => n.SerializerIdentity, StringComparer.Ordinal)],
        Signals = [.. descriptor.Signals.Select(CanonicalContract).OrderBy(n => n.SerializerIdentity, StringComparer.Ordinal)],
        RequiredHostAdapters = [.. descriptor.RequiredHostAdapters.Order(StringComparer.Ordinal)],
        Presentation = descriptor.Presentation.WithComparers(StringComparer.Ordinal),
        HostMetadata = descriptor.HostMetadata.WithComparers(StringComparer.Ordinal),
        SharedAssemblies = descriptor.SharedAssemblies.WithComparers(StringComparer.Ordinal),
        Payload = [.. descriptor.Payload.OrderBy(p => p.Path, StringComparer.Ordinal)]
    };

    internal static void Validate(CompiledAppDescriptor descriptor, IReadOnlyDictionary<string, string> shared)
    {
        if (descriptor.SchemaVersion != 1 || descriptor.HostAbi != 1 || descriptor.TargetFramework != "net11.0" || descriptor.FrameworkCompatibility != "1")
        { throw new InvalidDataException("Unsupported package schema, host ABI, target or framework contract."); }
        if (string.IsNullOrWhiteSpace(descriptor.Id) || string.IsNullOrWhiteSpace(descriptor.Name) || string.IsNullOrWhiteSpace(descriptor.Version) || string.IsNullOrWhiteSpace(descriptor.EntryType))
        { throw new InvalidDataException("App identity, name, version and entry type are required."); }
        if (descriptor.Capabilities.IsDefault || descriptor.Neurons.IsDefault || descriptor.Contracts.IsDefault || descriptor.Signals.IsDefault || descriptor.RequiredHostAdapters.IsDefault || descriptor.Payload.IsDefault || descriptor.Presentation is null || descriptor.HostMetadata is null || descriptor.SharedAssemblies is null)
        { throw new InvalidDataException("Metadata collections must be present."); }
        if (descriptor.Neurons.Any(n => n is null) || descriptor.Contracts.Any(c => c is null) || descriptor.Signals.Any(c => c is null) || descriptor.Payload.Any(p => p is null))
        { throw new InvalidDataException("Metadata collections cannot contain null entries."); }
        Unique(descriptor.Capabilities);
        Unique(descriptor.RequiredHostAdapters);
        Unique(descriptor.Neurons.Select(n => n.TypeName));
        Unique(descriptor.Neurons.Select(n => n.NeuronType));
        Unique(descriptor.Contracts.Concat(descriptor.Signals).Select(c => c.SerializerIdentity));
        foreach (var contract in descriptor.Contracts.Concat(descriptor.Signals)) { ValidateContract(contract); }
        foreach (var neuron in descriptor.Neurons)
        {
            if (string.IsNullOrWhiteSpace(neuron.StateName) || neuron.HostPolicy is not ("affine" or "reentrant" or "stateless") || neuron.LegacyOwnership.IsDefault)
            { throw new InvalidDataException("Neuron state identity and schema are required."); }
            if (neuron.State is not null) { ValidateContract(neuron.State); }
            foreach (var ownership in neuron.LegacyOwnership)
            {
                if (ownership is null || ownership.Kind is not ("exact-key" or "key-prefix") || string.IsNullOrWhiteSpace(ownership.Value))
                { throw new InvalidDataException("Invalid explicit legacy ownership."); }
            }
        }
        Path(descriptor.EntryAssembly);
        if (!descriptor.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) { throw new InvalidDataException("Entry must be a managed DLL."); }
        foreach (var pair in descriptor.SharedAssemblies)
        {
            if (!shared.TryGetValue(pair.Key, out var version) || version != pair.Value)
            { throw new InvalidDataException($"Incompatible shared assembly '{pair.Key}'."); }
        }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in descriptor.Payload)
        {
            Path(item.Path);
            if (!paths.Add(item.Path) || item.Length < 0 || item.Sha256 is null || item.Sha256.Length != 64 || !item.Sha256.All(char.IsAsciiHexDigit))
            { throw new InvalidDataException("Invalid payload inventory."); }
            if (descriptor.SharedAssemblies.ContainsKey(System.IO.Path.GetFileNameWithoutExtension(item.Path)))
            { throw new InvalidDataException("Shared contracts cannot be private payload."); }
        }
        if (!descriptor.Payload.Any(item => string.Equals(item.Path, descriptor.EntryAssembly, StringComparison.Ordinal)))
        { throw new InvalidDataException("Entry assembly is missing from inventory."); }
    }

    private static CompiledContractDescriptor CanonicalContract(CompiledContractDescriptor contract) => contract with { Fields = [.. contract.Fields.OrderBy(f => f.Id)] };

    private static void ValidateContract(CompiledContractDescriptor contract)
    {
        if (contract is null || string.IsNullOrWhiteSpace(contract.TypeName) || string.IsNullOrWhiteSpace(contract.SerializerIdentity) || contract.SerializerIdentity.Contains(',') || string.IsNullOrWhiteSpace(contract.SchemaVersion) || contract.Fields.IsDefault)
        { throw new InvalidDataException("Invalid contract identity."); }
        if (contract.Fields.Any(f => f is null)) { throw new InvalidDataException("Contract fields cannot contain null entries."); }
        Unique(contract.Fields.Select(f => f.Name));
        if (contract.Fields.Select(f => f.Id).Distinct().Count() != contract.Fields.Length || contract.Fields.Any(f => string.IsNullOrWhiteSpace(f.TypeName)))
        { throw new InvalidDataException("Invalid contract fields."); }
        foreach (var field in contract.Fields)
        { if (field.DefaultJson is not null) { using var document = JsonDocument.Parse(field.DefaultJson); } }
    }

    internal static void Unique(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        { if (string.IsNullOrWhiteSpace(value) || !seen.Add(value)) { throw new InvalidDataException("Invalid or duplicate metadata declaration."); } }
    }

    internal static void Path(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') || path.EndsWith('/') || path.Any(char.IsControl))
        { throw new InvalidDataException("Unsafe package path."); }
        foreach (var component in path.Split('/'))
        {
            if (component is "" or "." or ".." || component.EndsWith('.') || component.EndsWith(' ') || component.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
            { throw new InvalidDataException("Unsafe package path component."); }
            var stem = component.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '0' and <= '9'))
            { throw new InvalidDataException("Reserved package path."); }
        }
    }

    internal static bool Excluded(string path)
    {
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var name = System.IO.Path.GetFileName(path);
        return extension is ".cs" or ".csproj" or ".sln" or ".slnx" or ".pdb" or ".key" or ".pem" or ".pfx" or ".user" or ".env"
            || name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || path.Split('/').Any(p => p is "obj" or "bin" or ".git");
    }

    internal static void PayloadFile(string path, Stream content)
    {
        if (Excluded(path) || System.IO.Path.GetExtension(path).ToLowerInvariant() is ".so" or ".dylib" or ".a" or ".lib" or ".exe")
        { throw new InvalidDataException($"Unsupported payload '{path}'."); }
        if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) { return; }
        try
        {
            using var pe = new PEReader(content, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null || (pe.PEHeaders.CorHeader.Flags & CorFlags.ILOnly) == 0)
            { throw new InvalidDataException("Native or mixed-mode assembly is unsupported."); }
            var metadata = pe.GetMetadataReader();
            foreach (var method in metadata.MethodDefinitions)
            {
                if ((metadata.GetMethodDefinition(method).Attributes & System.Reflection.MethodAttributes.PinvokeImpl) != 0)
                { throw new InvalidDataException("Native imports are unsupported."); }
            }
        }
        catch (BadImageFormatException exception) { throw new InvalidDataException("Invalid managed assembly.", exception); }
    }
}
