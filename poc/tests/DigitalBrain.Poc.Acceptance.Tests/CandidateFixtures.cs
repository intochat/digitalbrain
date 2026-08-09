using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Acceptance.Tests;

internal static class CandidateFixtures
{
    public static Task<CandidateFixture> BuildProbeCandidateAsync(
        PocDataRoot root,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default) =>
        BuildAsync(root, family, handlesProbeIngress: true, cancellationToken);

    public static Task<CandidateFixture> BuildChangedProbeCandidateAsync(
        PocDataRoot root,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default) =>
        BuildAsync(root, family, handlesProbeIngress: true, cancellationToken, variant: "changed");

    public static Task<CandidateFixture> BuildOtherTriggerCandidateAsync(
        PocDataRoot root,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default) =>
        BuildAsync(root, family, handlesProbeIngress: false, cancellationToken);

    public static Task<CandidateFixture> BuildChartPointCandidateAsync(
        PocDataRoot root,
        CandidateFamilyId family,
        string ownerId,
        IReadOnlyList<string> grantedTargetScopes,
        CancellationToken cancellationToken = default) =>
        BuildAsync(
            root,
            family,
            handlesProbeIngress: true,
            cancellationToken,
            ownerId: ownerId,
            emitsChartPoint: true,
            grantedTargetScopes: grantedTargetScopes,
            trustedCharts: [new TrustedChartFixture("owner-a", "elon-chart")]);

    private static async Task<CandidateFixture> BuildAsync(
        PocDataRoot root,
        CandidateFamilyId family,
        bool handlesProbeIngress,
        CancellationToken cancellationToken,
        string? variant = null,
        string ownerId = "owner-a",
        bool emitsChartPoint = false,
        IReadOnlyList<string>? grantedTargetScopes = null,
        IReadOnlyList<TrustedChartFixture>? trustedCharts = null)
    {
        const string seedFamily = "cf_cccccccccccccccccccccccccc";
        var sourcePath = Path.Combine(
            HostProcess.FindPocRoot(),
            "tests",
            "DigitalBrain.Poc.Foundation.Tests",
            "Fixtures",
            "probe-neuron.cs");
        var source = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        source = source.Replace(seedFamily, family.Value, StringComparison.Ordinal);
        if (!handlesProbeIngress)
        {
            source = source
                .Replace("IHandle<ProbeIngress>", "IHandle<OtherProbeIngress>", StringComparison.Ordinal)
                .Replace(
                    "HandleAsync(ProbeIngress synapse",
                    "HandleAsync(OtherProbeIngress synapse",
                    StringComparison.Ordinal);
        }

        if (variant is not null)
        {
            source = source.Replace(
                "public sealed class ProbeEmitterNeuron",
                $"public static class CandidateBuildVariant {{ public const string Name = \"{variant}\"; }}{Environment.NewLine}{Environment.NewLine}public sealed class ProbeEmitterNeuron",
                StringComparison.Ordinal);
        }

        if (emitsChartPoint)
        {
            source = source.Replace(
                "DigitalBrain.FireSynapse(new ProbeSynapse(synapse.Value), cancellationToken);",
                "EmitChartPointAsync(synapse, cancellationToken);" + Environment.NewLine + Environment.NewLine +
                "    private Task EmitChartPointAsync(ProbeIngress synapse, CancellationToken cancellationToken)" + Environment.NewLine +
                "    {" + Environment.NewLine +
                "        var draftType = System.Type.GetType(\"DigitalBrain.Poc.Charting.Contracts.ChartPointDraft, DigitalBrain.Poc.Charting.Contracts\", throwOnError: true)!;" + Environment.NewLine +
                "        var commandType = System.Type.GetType(\"DigitalBrain.Poc.Charting.Contracts.AddChartPoint, DigitalBrain.Poc.Charting.Contracts\", throwOnError: true)!;" + Environment.NewLine +
                "        var draft = System.Activator.CreateInstance(" + Environment.NewLine +
                "            draftType," + Environment.NewLine +
                "            new object[] { synapse.Value, System.DateTimeOffset.UnixEpoch })!;" + Environment.NewLine +
                "        var command = System.Activator.CreateInstance(" + Environment.NewLine +
                "            commandType," + Environment.NewLine +
                "            new object[] { \"elon-chart\", draft })!;" + Environment.NewLine +
                "        return DigitalBrain.FireSynapse((Synapse)command, cancellationToken);" + Environment.NewLine +
                "    }",
                StringComparison.Ordinal);
        }

        ValidateFixedHeader(source, family);

        var candidateDirectory = Path.Combine(
            root.CandidateRoot,
            variant is null ? family.Value : $"{family.Value}-{variant}");
        var candidateSource = Path.Combine(candidateDirectory, "probe-neuron.cs");
        var scratch = Path.Combine(
            root.RootPath,
            "candidate-build",
            variant is null ? family.Value : $"{family.Value}-{variant}");
        Directory.CreateDirectory(candidateDirectory);
        await File.WriteAllTextAsync(
            candidateSource,
            source,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = candidateDirectory,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(candidateSource);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add($"-p:BaseIntermediateOutputPath={Path.Combine(scratch, "obj", "candidate")}{Path.DirectorySeparatorChar}");
        startInfo.ArgumentList.Add($"-p:BaseOutputPath={Path.Combine(scratch, "bin", "candidate")}{Path.DirectorySeparatorChar}");
        startInfo.ArgumentList.Add($"-p:DigitalBrainCandidateBuildScratch={scratch}");
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start the fixed candidate build.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var diagnostics = string.Join(Environment.NewLine, await standardOutput, await standardError);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Fixed candidate build failed:{Environment.NewLine}{diagnostics}");
        }

        var builtAssembly = Directory.EnumerateFiles(
                Path.Combine(scratch, "bin"),
                $"DigitalBrain.Poc.Candidate.{family.Value}.dll",
                SearchOption.AllDirectories)
            .Single(path => !path.Split(Path.DirectorySeparatorChar).Contains("ref"));
        var stagedAssembly = Path.Combine(candidateDirectory, "module.dll");
        File.Copy(builtAssembly, stagedAssembly, overwrite: true);

        bool managedIl;
        bool generatedSerializer;
        await using (var stream = File.OpenRead(stagedAssembly))
        using (var pe = new PEReader(stream))
        {
            managedIl = pe.HasMetadata;
            var metadata = pe.GetMetadataReader();
            generatedSerializer = metadata.TypeDefinitions
                .Select(handle => metadata.GetTypeDefinition(handle))
                .Any(type =>
                    metadata.GetString(type.Namespace).StartsWith("OrleansCodeGen", StringComparison.Ordinal) &&
                    metadata.GetString(type.Name).Contains("ProbeSynapse", StringComparison.Ordinal));
        }

        if (!managedIl || !generatedSerializer)
        {
            throw new InvalidDataException("Fixed candidate output did not preserve Task-2 IL/serializer evidence.");
        }

        var localAlias = $"db.poc.family.{family.Value}.matched.v1";
        var inputAlias = handlesProbeIngress
            ? "db.poc.probe.ingress.v1"
            : "db.poc.other.ingress.v1";
        var grantedTrustedOutputAliases = emitsChartPoint
            ? new[] { "db.poc.chart.add-point.v1" }
            : [];
        var evidencePath = Path.Combine(candidateDirectory, "candidate.json");
        var sourceBytes = await File.ReadAllBytesAsync(candidateSource, cancellationToken);
        await File.WriteAllTextAsync(
            evidencePath,
            JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    familyId = family.Value,
                    fixedHeaderVerified = true,
                    managedIlVerified = managedIl,
                    generatedSerializerVerified = generatedSerializer,
                    source = "probe-neuron.cs",
                    assembly = "module.dll",
                    sourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant(),
                    contractAliases = new[] { inputAlias, localAlias },
                },
                new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        var module = new VerifiedCandidateModule(
            ownerId,
            family,
            "revision-1",
            stagedAssembly,
            evidencePath,
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
                stagedAssembly,
                cancellationToken))).ToLowerInvariant(),
            [inputAlias],
            [localAlias],
            grantedTrustedOutputAliases,
            grantedTargetScopes ?? []);
        return new CandidateFixture(
            family,
            module,
            new CandidateManifest([inputAlias, localAlias]),
            localAlias,
            trustedCharts ?? []);
    }

    private static void ValidateFixedHeader(string source, CandidateFamilyId family)
    {
        var expectedDirectives = new[]
        {
            "#:sdk Microsoft.NET.Sdk",
            "#:property TargetFramework=net11.0",
            "#:property OutputType=Library",
            "#:property PublishAot=false",
            "#:property ImplicitUsings=disable",
            $"#:property AssemblyName=DigitalBrain.Poc.Candidate.{family.Value}",
            "#:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj",
        };
        var actualDirectives = source.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.TrimStart().StartsWith("#:", StringComparison.Ordinal))
            .ToArray();
        var exactHeader = string.Join("\n", expectedDirectives) + "\n\n";
        if (!source.StartsWith(exactHeader, StringComparison.Ordinal) ||
            !actualDirectives.SequenceEqual(expectedDirectives, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "The test fixture source does not preserve Task 2's exact family-derived fixed header.");
        }
    }
}
