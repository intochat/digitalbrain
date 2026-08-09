using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DigitalBrain.Poc.Foundation.Tests;

internal static class FileModuleBuilder
{
    internal const string FixedHeader = """
        #:sdk Microsoft.NET.Sdk
        #:property TargetFramework=net11.0
        #:property OutputType=Library
        #:property PublishAot=false
        #:property ImplicitUsings=disable
        #:property AssemblyName=DigitalBrain.Poc.Candidate.cf_cccccccccccccccccccccccccc
        #:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj

        """;

    private const string CandidateAssemblyName =
        "DigitalBrain.Poc.Candidate.cf_cccccccccccccccccccccccccc";

    public static async Task<CandidateBuildResult> BuildAsync(
        CandidateTestRun run,
        string proposedSource)
    {
        var proposedBytes = await File.ReadAllBytesAsync(proposedSource);
        ValidateProposedSource(proposedSource, proposedBytes);
        const bool fixedHeaderVerified = true;
        var canonicalHash = Convert.ToHexString(SHA256.HashData(proposedBytes)).ToLowerInvariant();
        var candidateDirectory = Path.Combine(run.CandidateRoot, canonicalHash);
        var candidateSource = Path.Combine(candidateDirectory, "probe-neuron.cs");
        var buildRoot = Path.Combine(run.BuildScratch, canonicalHash);
        var intermediateRoot = EnsureTrailingSeparator(Path.Combine(buildRoot, "obj", "candidate"));
        var outputRoot = EnsureTrailingSeparator(Path.Combine(buildRoot, "bin", "candidate"));

        Directory.CreateDirectory(candidateDirectory);
        await File.WriteAllBytesAsync(candidateSource, proposedBytes);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = candidateDirectory,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(candidateSource);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add($"-p:BaseIntermediateOutputPath={intermediateRoot}");
        startInfo.ArgumentList.Add($"-p:BaseOutputPath={outputRoot}");
        startInfo.ArgumentList.Add($"-p:DigitalBrainCandidateBuildScratch={buildRoot}");

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start dotnet build.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var diagnostics = string.Join(
            Environment.NewLine,
            new[] { await standardOutput, await standardError }.Where(value => value.Length != 0));

        if (process.ExitCode != 0)
        {
            return new CandidateBuildResult(
                false,
                diagnostics,
                candidateDirectory,
                Path.Combine(candidateDirectory, "module.dll"),
                fixedHeaderVerified,
                Array.Empty<CandidateDeclaredType>(),
                Array.Empty<CandidateConstructor>(),
                Array.Empty<CandidateContractAlias>());
        }

        var builtAssembly = FindBuiltAssembly(Path.Combine(buildRoot, "bin"));
        var metadata = ReadMetadata(builtAssembly);
        if (!metadata.Types.Any(type =>
                type.Namespace.StartsWith("OrleansCodeGen", StringComparison.Ordinal) &&
                type.Name.Contains("ProbeSynapse", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "The candidate assembly does not contain the generated ProbeSynapse serializer. " +
                $"Declared metadata types: {string.Join(", ", metadata.Types.Select(type => $"{type.Namespace}.{type.Name}"))}");
        }

        var stagedAssembly = Path.Combine(candidateDirectory, "module.dll");
        File.Copy(builtAssembly, stagedAssembly, overwrite: false);

        var evidence = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                runId = run.RunId,
                sourceSha256 = canonicalHash,
                source = "probe-neuron.cs",
                assembly = "module.dll",
                fixedHeaderVerified,
                declaredTypes = metadata.Types,
                constructors = metadata.Constructors,
                contractAliases = metadata.Aliases,
            },
            new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(
            Path.Combine(candidateDirectory, "candidate.json"),
            evidence + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new CandidateBuildResult(
            true,
            diagnostics,
            candidateDirectory,
            stagedAssembly,
            fixedHeaderVerified,
            metadata.Types,
            metadata.Constructors,
            metadata.Aliases);
    }

    private static bool HasExactFixedHeader(byte[] proposedBytes)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(FixedHeader);
        return proposedBytes.AsSpan().StartsWith(expectedBytes);
    }

    private static void ValidateProposedSource(string proposedSource, byte[] proposedBytes)
    {
        if (!Path.GetExtension(proposedSource).Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A candidate proposal must be exactly one C# source file.");
        }

        if (!HasExactFixedHeader(proposedBytes))
        {
            throw new InvalidDataException("The candidate source does not begin with the exact fixed header.");
        }

        string source;
        try
        {
            source = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(proposedBytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The candidate source must be valid UTF-8.", exception);
        }

        var expectedDirectives = FixedHeader
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var actualDirectives = source
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.TrimStart().StartsWith("#:", StringComparison.Ordinal))
            .ToArray();
        if (!actualDirectives.SequenceEqual(expectedDirectives, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The candidate source contains a changed or additional directive.");
        }
    }

    private static string FindBuiltAssembly(string outputRoot)
    {
        var matches = Directory.EnumerateFiles(
                outputRoot,
                $"{CandidateAssemblyName}.dll",
                SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Contains("ref", StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path.Length)
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected one built candidate assembly, found {matches.Length} under {outputRoot}.");
    }

    private static CandidateMetadata ReadMetadata(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            throw new InvalidDataException($"Candidate output is not managed IL: {assemblyPath}");
        }

        var reader = peReader.GetMetadataReader();
        var typeDefinitions = reader.TypeDefinitions
            .Select(handle => (Handle: handle, Definition: reader.GetTypeDefinition(handle)))
            .ToArray();
        var types = typeDefinitions
            .Select(type => new CandidateDeclaredType(
                reader.GetString(type.Definition.Namespace),
                reader.GetString(type.Definition.Name)))
            .ToArray();
        var constructors = typeDefinitions
            .SelectMany(type => type.Definition.GetMethods()
                .Select(handle => reader.GetMethodDefinition(handle))
                .Where(method => reader.GetString(method.Name).Equals(".ctor", StringComparison.Ordinal))
                .Select(method => new CandidateConstructor(
                    reader.GetString(type.Definition.Namespace),
                    reader.GetString(type.Definition.Name),
                    (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public,
                    method.DecodeSignature(CandidateTypeNameProvider.Instance, genericContext: null)
                        .ParameterTypes
                        .ToArray())))
            .ToArray();
        var aliases = reader.CustomAttributes
            .Select(handle => ReadAlias(reader, reader.GetCustomAttribute(handle)))
            .OfType<CandidateContractAlias>()
            .ToArray();
        return new CandidateMetadata(types, constructors, aliases);
    }

    private static CandidateContractAlias? ReadAlias(MetadataReader reader, CustomAttribute attribute)
    {
        if (!GetAttributeTypeName(reader, attribute.Constructor)
            .Equals("AliasAttribute", StringComparison.Ordinal))
        {
            return null;
        }

        var value = reader.GetBlobReader(attribute.Value);
        if (value.ReadUInt16() != 1 || attribute.Parent.Kind != HandleKind.TypeDefinition)
        {
            return null;
        }

        var alias = value.ReadSerializedString();
        if (alias is null)
        {
            return null;
        }

        var declaringType = reader.GetTypeDefinition((TypeDefinitionHandle)attribute.Parent);
        return new CandidateContractAlias(
            reader.GetString(declaringType.Namespace),
            reader.GetString(declaringType.Name),
            alias);
    }

    private static string GetAttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        EntityHandle parent = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor)
                .GetDeclaringType(),
            _ => default,
        };

        return parent.Kind switch
        {
            HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name),
            HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)parent).Name),
            _ => string.Empty,
        };
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private sealed record CandidateMetadata(
        IReadOnlyList<CandidateDeclaredType> Types,
        IReadOnlyList<CandidateConstructor> Constructors,
        IReadOnlyList<CandidateContractAlias> Aliases);

    private sealed class CandidateTypeNameProvider : ISignatureTypeProvider<string, object?>
    {
        public static CandidateTypeNameProvider Instance { get; } = new();

        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";

        public string GetByReferenceType(string elementType) => $"{elementType}&";

        public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            $"{RemoveGenericArity(genericType)}<{string.Join(",", typeArguments)}>";

        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";

        public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;

        public string GetPointerType(string elementType) => $"{elementType}*";

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => $"System.{typeCode}";

        public string GetSZArrayType(string elementType) => $"{elementType}[]";

        public string GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            return Qualify(reader.GetString(type.Namespace), reader.GetString(type.Name));
        }

        public string GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            return Qualify(reader.GetString(type.Namespace), reader.GetString(type.Name));
        }

        public string GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        private static string Qualify(string @namespace, string name) =>
            string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";

        private static string RemoveGenericArity(string typeName)
        {
            var separator = typeName.IndexOf('`');
            return separator < 0 ? typeName : typeName[..separator];
        }
    }
}
