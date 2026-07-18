using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
namespace DigitalBrain.FeatureBuilder;

internal static class FeatureManifestDeriver
{
    private static readonly Dictionary<string, string> CapabilityByContract = new(StringComparer.Ordinal)
    {
        ["DigitalBrain.Integrations.Google.Contracts.IGmailMessageReader"] = "google.gmail.message.read.v1",
        ["DigitalBrain.Integrations.Google.Contracts.IGmailMailboxReader"] = "google.gmail.mailbox.read.v1",
        ["DigitalBrain.Integrations.Google.Contracts.IGmailSendProposer"] = "google.gmail.send.propose.v1",
        ["DigitalBrain.Integrations.Salesforce.Contracts.ISalesforceRecordReader"] = "salesforce.record.read.v1",
        ["DigitalBrain.Integrations.Salesforce.Contracts.ISalesforceAccountSearcher"] = "salesforce.account.search.v1",
        ["DigitalBrain.Integrations.Salesforce.Contracts.ISalesforceUpdateProposer"] = "salesforce.record.update.propose.v1",
        ["DigitalBrain.Integrations.Web.Contracts.IWebSearchReader"] = "web.search.v1"
    };
    internal static string AssemblyName(FeatureSourceSnapshot snapshot, string projectPath)
    {
        var project = snapshot.Files.Single(file =>
            file.Path.Equals(projectPath, StringComparison.OrdinalIgnoreCase));
        var assemblyName = FeatureBuildSource.ParseXml(project.Path, project.Content).Descendants("AssemblyName").Select(static element => element.Value)
            .LastOrDefault();
        return (string.IsNullOrWhiteSpace(assemblyName) ? Path.GetFileNameWithoutExtension(projectPath) : assemblyName) + ".dll";
    }
    internal static FeatureManifest Derive(string buildOutputDirectory, string implementationAssembly)
    {
        var assemblyPaths = Directory.EnumerateFiles(buildOutputDirectory, "*.dll", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToArray();
        var implementationPath = Path.Combine(buildOutputDirectory, implementationAssembly);
        if (!assemblyPaths.Contains(implementationPath, StringComparer.OrdinalIgnoreCase))
        {
            throw new FeatureBuildException(FeatureBuildFailure.CompilationFailed, $"Implementation assembly '{implementationAssembly}' is missing.");
        }
        var analyses = assemblyPaths.Select(path => Analyze(path)).ToArray();
        var localAssemblies = analyses.Select(static analysis => analysis.AssemblyName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var analysis in analyses)
        {
            ValidateAssemblyReferences(analysis.AssemblyReferences, localAssemblies);
        }
        var implementation = analyses.Single(analysis =>
            analysis.AssemblyName.Equals(Path.GetFileNameWithoutExtension(implementationAssembly), StringComparison.Ordinal));
        var featureTypes = analyses.SelectMany(static analysis => analysis.FeatureTypes).ToArray();
        if (featureTypes.Length != 1 || implementation.FeatureTypes.Length != 1)
        {
            throw new FeatureBuildException(FeatureBuildFailure.InvalidSource, "A Feature implementation assembly must contain exactly one direct IFeature implementation.");
        }
        return new FeatureManifest(
            implementationAssembly,
            implementation.SdkVersion ?? throw new FeatureBuildException(FeatureBuildFailure.InvalidSource, "The implementation must reference DigitalBrain.Features.Sdk."),
            featureTypes,
            analyses.SelectMany(static analysis => analysis.Capabilities)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            analyses.SelectMany(static analysis => analysis.AssemblyReferences)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }
    internal static int ValidateScenarioAssembly(string assemblyPath, string implementationAssembly, int sourceScenarioCount)
    {
        var analysis = Analyze(assemblyPath, allowReqnrollGeneratedState: true);
        string[] requiredReferences =
        [Path.GetFileNameWithoutExtension(implementationAssembly), "DigitalBrain.Features.Testing", "Reqnroll"];
        var missing = requiredReferences.FirstOrDefault(required =>
            !analysis.AssemblyReferences.Contains(required, StringComparer.Ordinal));
        if (missing is not null)
        {
            throw new FeatureBuildException(FeatureBuildFailure.InvalidSource, $"The scenario assembly must reference '{missing}'.");
        }
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        var reader = peReader.GetMetadataReader();
        var bddMethods = 0;
        var testMethods = 0;
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            var attributes = method.GetCustomAttributes().Select(attributeHandle => reader.GetCustomAttribute(attributeHandle))
                .ToArray();
            if (attributes.Any(attribute => IsTestAttribute(reader, attribute)))
            {
                testMethods++;
            }
            if (attributes.Any(attribute => IsFeatureTitleTrait(reader, attribute)) &&
                IsReqnrollGeneratedType(reader, method.GetDeclaringType()))
            {
                bddMethods++;
            }
        }
        if (bddMethods == 0 || testMethods != bddMethods || bddMethods != sourceScenarioCount)
        {
            throw new FeatureBuildException(FeatureBuildFailure.InvalidSource, $"The scenario assembly must contain exactly the {sourceScenarioCount} source scenarios and no other tests.");
        }
        return bddMethods;
    }
    internal static void ValidateScenarioDependency(string assemblyPath) =>
        _ = Analyze(assemblyPath);
    private static AssemblyAnalysis Analyze(string path, bool allowReqnrollGeneratedState = false)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!peReader.HasMetadata)
        {
            throw new FeatureBuildException(FeatureBuildFailure.CompilationFailed, $"Build output '{Path.GetFileName(path)}' is not a managed assembly.");
        }
        var reader = peReader.GetMetadataReader();
        RejectForbiddenMetadata(reader, allowReqnrollGeneratedState);
        return new AssemblyAnalysis(reader.GetString(reader.GetAssemblyDefinition().Name), FeatureTypes(reader), Capabilities(reader), AssemblyReferences(reader), TrySdkVersion(reader));
    }
    private static string[] FeatureTypes(MetadataReader reader) => reader.TypeDefinitions.Select(handle => (Handle: handle, Definition: reader.GetTypeDefinition(handle)))
        .Where(item => item.Definition.GetInterfaceImplementations().Select(handle => reader.GetInterfaceImplementation(handle).Interface)
            .Any(handle => TypeName(reader, handle).Equals("DigitalBrain.Features.Sdk.IFeature", StringComparison.Ordinal)))
        .Select(item => TypeName(reader, item.Handle))
        .Order(StringComparer.Ordinal)
        .ToArray();
    private static string[] Capabilities(MetadataReader reader) => reader.MemberReferences.Select(handle => reader.GetMemberReference(handle).Parent)
        .Select(handle => TypeName(reader, handle))
        .Where(CapabilityByContract.ContainsKey)
        .Select(type => CapabilityByContract[type])
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
    private static string[] AssemblyReferences(MetadataReader reader) => reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))
        .Order(StringComparer.Ordinal)
        .ToArray();
    private static void ValidateAssemblyReferences(IEnumerable<string> references, IReadOnlySet<string> localAssemblies)
    {
        var forbidden = references.FirstOrDefault(reference =>
            !reference.StartsWith("System.", StringComparison.Ordinal) && !reference.Equals("System", StringComparison.Ordinal) &&
            !reference.Equals("netstandard", StringComparison.Ordinal) &&
            !localAssemblies.Contains(reference) &&
            !reference.Equals("DigitalBrain.Features.Sdk", StringComparison.Ordinal) &&
            !(reference.StartsWith("DigitalBrain.Integrations.", StringComparison.Ordinal) &&
              reference.EndsWith(".Contracts", StringComparison.Ordinal)));
        if (forbidden is not null)
        {
            throw new FeatureBuildException(FeatureBuildFailure.InvalidSource, $"Assembly reference '{forbidden}' is not allowed in Feature implementations.");
        }
    }
    private static string? TrySdkVersion(MetadataReader reader)
    {
        foreach (var handle in reader.AssemblyReferences)
        {
            var reference = reader.GetAssemblyReference(handle);
            if (reader.GetString(reference.Name).Equals("DigitalBrain.Features.Sdk", StringComparison.Ordinal))
            {
                return reference.Version.ToString();
            }
        }
        return null;
    }
    private static void RejectForbiddenMetadata(MetadataReader reader, bool allowReqnrollGeneratedState)
    {
        foreach (var handle in reader.TypeReferences)
        {
            var type = TypeName(reader, handle);
            if (type.StartsWith("System.IO.", StringComparison.Ordinal) || type.StartsWith("System.Net.", StringComparison.Ordinal) ||
                type.StartsWith("System.Runtime.Loader.", StringComparison.Ordinal) ||
                type.StartsWith("System.Runtime.InteropServices.", StringComparison.Ordinal) ||
                type is
                    "System.Environment" or
                    "System.Random" or
                    "System.Diagnostics.Process" or
                    "System.Diagnostics.Stopwatch" or
                    "System.Security.Cryptography.RandomNumberGenerator")
            {
                throw Nondeterministic(type);
            }
        }
        foreach (var handle in reader.MemberReferences)
        {
            var reference = reader.GetMemberReference(handle);
            var type = TypeName(reader, reference.Parent);
            var member = reader.GetString(reference.Name);
            if ((type is "System.DateTime" or "System.DateTimeOffset" && member is "get_Now" or "get_UtcNow") ||
                (type == "System.Guid" && member == "NewGuid") ||
                (type == "System.Threading.Tasks.Task" && member == "Delay") ||
                (type.StartsWith("System.Reflection.", StringComparison.Ordinal) &&
                 !(type.EndsWith("Attribute", StringComparison.Ordinal) && member == ".ctor")) ||
                (type == "System.Type" && member is not ("GetTypeFromHandle" or "get_Assembly")))
            {
                var displayMember = member.StartsWith("get_", StringComparison.Ordinal) ? member[4..] : member;
                throw Nondeterministic($"{type}.{displayMember}");
            }
        }
        RejectMutableStaticsAndNativeMethods(reader, allowReqnrollGeneratedState);
    }
    private static void RejectMutableStaticsAndNativeMethods(MetadataReader reader, bool allowReqnrollGeneratedState)
    {
        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            foreach (var fieldHandle in definition.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.Static) != 0 && (field.Attributes & (FieldAttributes.Literal | FieldAttributes.InitOnly)) == 0 &&
                    !(allowReqnrollGeneratedState && IsReqnrollGeneratedType(reader, handle)))
                {
                    throw Nondeterministic($"mutable static field {TypeName(reader, handle)}.{reader.GetString(field.Name)}");
                }
            }
            foreach (var methodHandle in definition.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                {
                    throw Nondeterministic($"native method {reader.GetString(method.Name)}");
                }
            }
        }
    }
    private static FeatureBuildException Nondeterministic(string input) =>
        new(FeatureBuildFailure.NondeterministicInput, $"Nondeterministic or authority-bearing input '{input}' is forbidden in Feature implementations.");
    private static string TypeName(MetadataReader reader, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeReference => TypeReferenceName(reader, reader.GetTypeReference((TypeReferenceHandle)handle)),
        HandleKind.TypeDefinition => TypeDefinitionName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)handle)),
        _ => string.Empty
    };
    private static string TypeReferenceName(MetadataReader reader, TypeReference reference)
    {
        var name = reader.GetString(reference.Name);
        var prefix = reference.ResolutionScope.Kind == HandleKind.TypeReference
            ? TypeName(reader, reference.ResolutionScope) + "+"
            : NamespacePrefix(reader.GetString(reference.Namespace));
        return prefix + name;
    }
    private static string TypeDefinitionName(MetadataReader reader, TypeDefinition definition) =>
        NamespacePrefix(reader.GetString(definition.Namespace)) + reader.GetString(definition.Name);
    private static string NamespacePrefix(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value + ".";
    private static bool IsTestAttribute(MetadataReader reader, CustomAttribute attribute)
    {
        var name = AttributeTypeName(reader, attribute);
        return name.EndsWith("FactAttribute", StringComparison.Ordinal) || name.EndsWith("TheoryAttribute", StringComparison.Ordinal);
    }
    private static bool IsFeatureTitleTrait(MetadataReader reader, CustomAttribute attribute)
    {
        if (!AttributeTypeName(reader, attribute).Equals("Xunit.TraitAttribute", StringComparison.Ordinal))
        {
            return false;
        }
        var value = reader.GetBlobReader(attribute.Value);
        return value.ReadUInt16() == 1 && value.ReadSerializedString()?.Equals("FeatureTitle", StringComparison.Ordinal) == true;
    }
    private static bool IsReqnrollGeneratedType(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        foreach (var attributeHandle in definition.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (!AttributeTypeName(reader, attribute).Equals("System.CodeDom.Compiler.GeneratedCodeAttribute", StringComparison.Ordinal))
            {
                continue;
            }
            var value = reader.GetBlobReader(attribute.Value);
            if (value.ReadUInt16() == 1 && value.ReadSerializedString()?.Equals("Reqnroll", StringComparison.Ordinal) == true)
            {
                return true;
            }
        }
        return false;
    }
    private static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute) =>
        attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => TypeName(reader, reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent),
            HandleKind.MethodDefinition => TypeName(reader, reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()),
            _ => string.Empty
        };
    private sealed record AssemblyAnalysis(string AssemblyName, string[] FeatureTypes, string[] Capabilities, string[] AssemblyReferences, string? SdkVersion);
}
