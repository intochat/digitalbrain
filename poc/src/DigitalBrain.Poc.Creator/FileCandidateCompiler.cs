using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Charting.Contracts;
using DigitalBrain.Poc.Runtime;
using DigitalBrain.Poc.Social.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.Poc.Creator;

public sealed class FileCandidateCompiler
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly CandidateRepository _repository;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<string>> _runDotNet;

    public FileCandidateCompiler(CandidateRepository repository)
        : this(repository, RunDotNetAsync)
    {
    }

    internal FileCandidateCompiler(
        CandidateRepository repository,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<string>> runDotNet)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _runDotNet = runDotNet ?? throw new ArgumentNullException(nameof(runDotNet));
    }

    public async Task<CompiledCandidate> CompileAsync(
        ElonChartAuthoringIntent intent,
        PocDataRoot root,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(root);
        var shape = new ElonChartSyntaxFactory().Create(intent);
        var sourceBytes = Utf8.GetBytes(shape.Source);
        var id = CandidateRepository.Hash(sourceBytes);
        var directory = _repository.DirectoryFor(id, root);
        var sourcePath = Path.Combine(directory, "elon-chart.cs");
        var scratch = Path.Combine(root.RootPath, "candidate-build", id);
        var createdDirectory = false;
        try
        {
            var validation = new CandidateSourceValidator().Validate(intent, sourceBytes);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(
                    $"Generated candidate failed authoring policy: {validation.Error}: {validation.Detail}");
            }

            if (Directory.Exists(directory))
            {
                try
                {
                    _repository.RequireCanonicalContents(id, root);
                    throw new IOException("The immutable candidate source identity is already published.");
                }
                catch (InvalidDataException)
                {
                    _repository.RemoveIncompleteCandidate(id, root);
                }
            }

            Directory.CreateDirectory(directory);
            createdDirectory = true;
            await WriteNewAsync(sourcePath, sourceBytes, cancellationToken);
            var sdkVersion = await _runDotNet(
                directory,
                ["--version"],
                cancellationToken);
            _ = await _runDotNet(
                directory,
                [
                    "build",
                    sourcePath,
                    "-c",
                    "Release",
                    $"-p:BaseIntermediateOutputPath={Path.Combine(scratch, "obj", "candidate")}{Path.DirectorySeparatorChar}",
                    $"-p:BaseOutputPath={Path.Combine(scratch, "bin", "candidate")}{Path.DirectorySeparatorChar}",
                    $"-p:DigitalBrainCandidateBuildScratch={scratch}",
                ],
                cancellationToken);
            var assemblyName = $"DigitalBrain.Poc.Candidate.{intent.Family.Value}";
            var builtAssembly = Directory.EnumerateFiles(
                    Path.Combine(scratch, "bin"),
                    $"{assemblyName}.dll",
                    SearchOption.AllDirectories)
                .Single(path => !path.Split(Path.DirectorySeparatorChar).Contains("ref"));
            var assemblyPath = Path.Combine(directory, "module.dll");
            await CopyNewAsync(builtAssembly, assemblyPath, cancellationToken);
            var assemblyBytes = await File.ReadAllBytesAsync(assemblyPath, cancellationToken);
            var assemblyReferences = VerifyManagedAssembly(assemblyPath, intent.Family.Value);

            var headerBytes = FixedCandidateHeader.CreateUtf8(intent.Family);
            var sourceBody = shape.Source[FixedCandidateHeader.Create(intent.Family).Length..];
            var normalizedAst = CSharpSyntaxTree.ParseText(sourceBody)
            .GetCompilationUnitRoot()
            .NormalizeWhitespace(indentation: "    ", eol: "\n")
            .ToFullString();
            var referenceEvidence = (await ReferenceEvidenceAsync(directory, cancellationToken))
            .Concat(await BuildOutputEvidenceAsync(builtAssembly, cancellationToken))
            .Concat(assemblyReferences.Select(reference => $"assembly|{reference}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
            var manifest = new CandidateManifest
            {
            SchemaVersion = 1,
            Id = id,
            RunId = root.RunId,
            FamilyId = intent.Family.Value,
            Status = CandidateStatus.AwaitingQuarantine,
            Source = "elon-chart.cs",
            Assembly = "module.dll",
            SourceHash = CandidateRepository.Hash(sourceBytes),
            NormalizedAstHash = HashText(normalizedAst),
            FixedHeaderHash = CandidateRepository.Hash(headerBytes),
            CompilerHash = CandidateRepository.Hash(await File.ReadAllBytesAsync(
                typeof(CSharpCompilation).Assembly.Location,
                cancellationToken)),
            SdkHash = HashText(sdkVersion.Trim()),
            ReferencesHash = HashText(string.Join("\n", referenceEvidence)),
            ResolvedReferences = referenceEvidence,
            CapabilitiesHash = HashText(string.Join(
                "\n",
                intent.AttestedTriggerAlias,
                "db.poc.chart.add-point.v1",
                intent.ChartId)),
            ContractsHash = HashText(string.Join(
                "\n",
                intent.AttestedTriggerAlias,
                $"db.poc.family.{intent.Family.Value}.matched.v{intent.LocalSynapseSchemaVersion}",
                "db.poc.chart.add-point.v1")),
            StateSchemaHash = HashText(
                $"ElonPostRuleState|AcceptedCount:int|v{intent.LocalSynapseSchemaVersion}"),
            AssemblyHash = CandidateRepository.Hash(assemblyBytes),
            SourceHashVerified = true,
            AssemblyHashVerified = true,
            };
            manifest = await _repository.WriteEvidenceMirrorAsync(
                manifest,
                root,
                cancellationToken);
            _repository.RequireCanonicalContents(id, root);
            File.SetAttributes(sourcePath, File.GetAttributes(sourcePath) | FileAttributes.ReadOnly);
            File.SetAttributes(assemblyPath, File.GetAttributes(assemblyPath) | FileAttributes.ReadOnly);
            return new CompiledCandidate(
                id,
                directory,
                sourcePath,
                assemblyPath,
                scratch,
                manifest,
                intent);
        }
        catch (Exception exception)
        {
            try
            {
                if (createdDirectory)
                {
                    _repository.RemoveIncompleteCandidate(id, root);
                }
            }
            finally
            {
                await WriteBuildDiagnosticAsync(root, id, exception);
            }

            throw;
        }
    }

    internal async Task<bool> VerifyDerivedEvidenceAsync(
        CompiledCandidate compiled,
        PocDataRoot root,
        CancellationToken cancellationToken)
    {
        RequireCanonicalLocations(compiled, root);
        _repository.RequireCanonicalContents(compiled.Id, root);
        var sourceBytes = await File.ReadAllBytesAsync(compiled.SourcePath, cancellationToken);
        var assemblyBytes = await File.ReadAllBytesAsync(compiled.AssemblyPath, cancellationToken);
        var assemblyReferences = VerifyManagedAssembly(
            compiled.AssemblyPath,
            compiled.Intent.Family.Value);
        var builtAssembly = Directory.EnumerateFiles(
                Path.Combine(compiled.ScratchDirectory, "bin"),
                $"DigitalBrain.Poc.Candidate.{compiled.Intent.Family.Value}.dll",
                SearchOption.AllDirectories)
            .Single(path => !path.Split(Path.DirectorySeparatorChar).Contains("ref"));
        var references = (await ReferenceEvidenceAsync(compiled.Directory, cancellationToken))
            .Concat(await BuildOutputEvidenceAsync(builtAssembly, cancellationToken))
            .Concat(assemblyReferences.Select(reference => $"assembly|{reference}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var source = Utf8.GetString(sourceBytes);
        var header = FixedCandidateHeader.Create(compiled.Intent.Family);
        if (!source.StartsWith(header, StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedAst = CSharpSyntaxTree.ParseText(source[header.Length..])
            .GetCompilationUnitRoot()
            .NormalizeWhitespace(indentation: "    ", eol: "\n")
            .ToFullString();
        var manifest = compiled.Manifest;
        return string.Equals(manifest.Id, CandidateRepository.Hash(sourceBytes), StringComparison.Ordinal) &&
            string.Equals(manifest.SourceHash, CandidateRepository.Hash(sourceBytes), StringComparison.Ordinal) &&
            string.Equals(manifest.AssemblyHash, CandidateRepository.Hash(assemblyBytes), StringComparison.Ordinal) &&
            string.Equals(manifest.NormalizedAstHash, HashText(normalizedAst), StringComparison.Ordinal) &&
            string.Equals(manifest.FixedHeaderHash, CandidateRepository.Hash(FixedCandidateHeader.CreateUtf8(
                compiled.Intent.Family)), StringComparison.Ordinal) &&
            string.Equals(manifest.CompilerHash, CandidateRepository.Hash(await File.ReadAllBytesAsync(
                typeof(CSharpCompilation).Assembly.Location,
                cancellationToken)), StringComparison.Ordinal) &&
            string.Equals(manifest.SdkHash, HashText((await _runDotNet(
                compiled.Directory,
                ["--version"],
                cancellationToken)).Trim()), StringComparison.Ordinal) &&
            string.Equals(manifest.ReferencesHash, HashText(string.Join("\n", references)), StringComparison.Ordinal) &&
            manifest.ResolvedReferences.SequenceEqual(references, StringComparer.Ordinal) &&
            string.Equals(manifest.CapabilitiesHash, HashText(string.Join(
                "\n",
                compiled.Intent.AttestedTriggerAlias,
                "db.poc.chart.add-point.v1",
                compiled.Intent.ChartId)), StringComparison.Ordinal) &&
            string.Equals(manifest.ContractsHash, HashText(string.Join(
                "\n",
                compiled.Intent.AttestedTriggerAlias,
                $"db.poc.family.{compiled.Intent.Family.Value}.matched.v{compiled.Intent.LocalSynapseSchemaVersion}",
                "db.poc.chart.add-point.v1")), StringComparison.Ordinal) &&
            string.Equals(manifest.StateSchemaHash, HashText(
                $"ElonPostRuleState|AcceptedCount:int|v{compiled.Intent.LocalSynapseSchemaVersion}"), StringComparison.Ordinal) &&
            manifest.SourceHashVerified &&
            manifest.AssemblyHashVerified;
    }

    private void RequireCanonicalLocations(CompiledCandidate compiled, PocDataRoot root)
    {
        var directory = _repository.DirectoryFor(compiled.Id, root);
        var source = Path.Combine(directory, "elon-chart.cs");
        var assembly = Path.Combine(directory, "module.dll");
        var scratch = Path.Combine(root.RootPath, "candidate-build", compiled.Id);
        if (!SamePath(compiled.Directory, directory) ||
            !SamePath(compiled.SourcePath, source) ||
            !SamePath(compiled.AssemblyPath, assembly) ||
            !SamePath(compiled.ScratchDirectory, scratch))
        {
            throw new InvalidDataException("Candidate paths are outside their canonical run-owned locations.");
        }
    }

    private static bool SamePath(string actual, string expected) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)),
            StringComparison.OrdinalIgnoreCase);

    private static async Task<IReadOnlyList<string>> ReferenceEvidenceAsync(
        string candidateDirectory,
        CancellationToken cancellationToken)
    {
        var references = new[]
        {
            "../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj",
            "../../../src/DigitalBrain.Poc.Social.Contracts/DigitalBrain.Poc.Social.Contracts.csproj",
            "../../../src/DigitalBrain.Poc.Charting.Contracts/DigitalBrain.Poc.Charting.Contracts.csproj",
        };
        var evidence = new List<string>();
        foreach (var reference in references)
        {
            var path = Path.GetFullPath(Path.Combine(candidateDirectory, reference));
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            evidence.Add($"{reference}|{CandidateRepository.Hash(bytes)}");
        }

        evidence.Add($"{typeof(Synapse).Assembly.GetName().Name}|{AssemblyHash(typeof(Synapse).Assembly)}");
        evidence.Add($"{typeof(SocialPostObserved).Assembly.GetName().Name}|{AssemblyHash(typeof(SocialPostObserved).Assembly)}");
        evidence.Add($"{typeof(AddChartPoint).Assembly.GetName().Name}|{AssemblyHash(typeof(AddChartPoint).Assembly)}");
        return evidence;
    }

    private static string AssemblyHash(Assembly assembly) =>
        CandidateRepository.Hash(File.ReadAllBytes(assembly.Location));

    private static async Task<IReadOnlyList<string>> BuildOutputEvidenceAsync(
        string candidateAssembly,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(candidateAssembly)!;
        var candidateName = Path.GetFileName(candidateAssembly);
        var evidence = new List<string>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFileName(path), candidateName, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            evidence.Add(
                $"resolved-file|{Path.GetFileName(path)}|{CandidateRepository.Hash(await File.ReadAllBytesAsync(path, cancellationToken))}");
        }

        return evidence;
    }

    private static string HashText(string value) => CandidateRepository.Hash(Utf8.GetBytes(value));

    private static async Task<string> RunDotNetAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start the .NET SDK candidate build.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            _ = await Task.WhenAll(output, error);
            throw;
        }

        var diagnostics = string.Join(Environment.NewLine, await output, await error);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Candidate SDK command failed with exit code {process.ExitCode}:{Environment.NewLine}{diagnostics}");
        }

        return diagnostics;
    }

    private static IReadOnlyList<string> VerifyManagedAssembly(string path, string family)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
        {
            throw new InvalidDataException("Candidate output is not managed IL.");
        }

        var metadata = pe.GetMetadataReader();
        var generatedSerializer = metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Any(type =>
                metadata.GetString(type.Namespace).StartsWith("OrleansCodeGen", StringComparison.Ordinal) &&
                metadata.GetString(type.Name).Contains("ElonPostMatched", StringComparison.Ordinal));
        if (!generatedSerializer)
        {
            throw new InvalidDataException("Candidate output has no build-time local-synapse serializer.");
        }

        var expected = $"DigitalBrain.Poc.Candidate.{family}";
        if (!string.Equals(AssemblyName.GetAssemblyName(path).Name, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Candidate assembly identity is not derived from its family.");
        }

        return metadata.AssemblyReferences
            .Select(metadata.GetAssemblyReference)
            .Select(reference =>
                $"{metadata.GetString(reference.Name)}|{reference.Version}|{Convert.ToHexString(metadata.GetBlobBytes(reference.PublicKeyOrToken)).ToLowerInvariant()}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task WriteNewAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task WriteBuildDiagnosticAsync(
        PocDataRoot root,
        string id,
        Exception exception)
    {
        var diagnosticPath = Path.Combine(root.CandidateEvidencePath, $"{id}-build.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(diagnosticPath)!);
        if (File.Exists(diagnosticPath))
        {
            return;
        }

        await WriteNewAsync(
            diagnosticPath,
            Utf8.GetBytes($"{exception.GetType().Name}: {exception.Message}\n"),
            CancellationToken.None);
        File.SetAttributes(
            diagnosticPath,
            File.GetAttributes(diagnosticPath) | FileAttributes.ReadOnly);
    }

    private static async Task CopyNewAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using var input = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public sealed record CompiledCandidate(
        string Id,
        string Directory,
        string SourcePath,
        string AssemblyPath,
        string ScratchDirectory,
        CandidateManifest Manifest,
        ElonChartAuthoringIntent Intent);
}
