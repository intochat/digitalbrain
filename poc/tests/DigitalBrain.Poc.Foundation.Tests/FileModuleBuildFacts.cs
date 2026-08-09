using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using Xunit;

namespace DigitalBrain.Poc.Foundation.Tests;

public sealed class FileModuleBuildFacts
{
    [Fact]
    public async Task SingleFileLibraryBuildsWithoutCandidateProject()
    {
        await using var run = CandidateTestRun.Create();
        var result = await FileModuleBuilder.BuildAsync(run, FixturePaths.ProbeNeuron);

        Assert.True(result.Succeeded, result.Diagnostics);
        Assert.Single(Directory.EnumerateFiles(
            result.CandidateDirectory,
            "*.cs",
            SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(
            result.CandidateDirectory,
            "*.csproj",
            SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CandidateOutputIsManagedIl()
    {
        await using var run = CandidateTestRun.Create();
        var result = await FileModuleBuilder.BuildAsync(run, FixturePaths.ProbeNeuron);

        using var stream = File.OpenRead(result.AssemblyPath);
        using var reader = new PEReader(stream);
        Assert.True(reader.HasMetadata);
    }

    [Fact]
    public async Task CandidateUsesOnlyTheFixedHeaderAndOneSourceFile()
    {
        await using var run = CandidateTestRun.Create();
        var candidate = await FileModuleBuilder.BuildAsync(run, FixturePaths.ProbeNeuron);

        Assert.True(candidate.FixedHeaderVerified);
        Assert.Single(candidate.DeclaredTypes, type => type.Name == "ProbeNeuron");
        Assert.Single(candidate.DeclaredTypes, type => type.Name == "ProbeSynapse");
    }

    [Fact]
    public async Task CandidateDefinedSynapseHasTheExpectedGeneratedSerializerAlias()
    {
        await using var run = CandidateTestRun.Create();
        var candidate = await FileModuleBuilder.BuildAsync(run, FixturePaths.ProbeNeuron);

        var alias = Assert.Single(
            candidate.ContractAliases,
            alias =>
                alias.DeclaringNamespace == "DigitalBrain.Poc.Candidate.cf_cccccccccccccccccccccccccc" &&
                alias.DeclaringType == "ProbeSynapse" &&
                alias.Alias == "db.poc.family.cf_cccccccccccccccccccccccccc.matched.v1");

        Assert.Equal("db.poc.family.cf_cccccccccccccccccccccccccc.matched.v1", alias.Alias);
    }

    [Fact]
    public async Task CandidateNeuronConstructorsExposeOnlyTypedStateDependencies()
    {
        await using var run = CandidateTestRun.Create();
        var candidate = await FileModuleBuilder.BuildAsync(run, FixturePaths.ProbeNeuron);

        var emitterConstructor = Assert.Single(
            candidate.Constructors,
            constructor =>
                constructor.DeclaringNamespace == "DigitalBrain.Poc.Candidate.cf_cccccccccccccccccccccccccc" &&
                constructor.DeclaringType == "ProbeEmitterNeuron" &&
                constructor.IsPublic);
        var statefulConstructor = Assert.Single(
            candidate.Constructors,
            constructor =>
                constructor.DeclaringNamespace == "DigitalBrain.Poc.Candidate.cf_cccccccccccccccccccccccccc" &&
                constructor.DeclaringType == "ProbeNeuron" &&
                constructor.IsPublic);

        Assert.Equal(
            ["DigitalBrain.Poc.Abstractions.IDigitalBrain"],
            emitterConstructor.ParameterTypes);
        Assert.Equal(
            [
                "DigitalBrain.Poc.Abstractions.IDigitalBrain",
                "DigitalBrain.Poc.Abstractions.IDurableState<System.String>",
            ],
            statefulConstructor.ParameterTypes);

        var publicNeuronParameterTypes = candidate.Constructors
            .Where(constructor =>
                constructor.IsPublic &&
                constructor.DeclaringNamespace == "DigitalBrain.Poc.Candidate.cf_cccccccccccccccccccccccccc" &&
                constructor.DeclaringType.EndsWith("Neuron", StringComparison.Ordinal))
            .SelectMany(constructor => constructor.ParameterTypes)
            .ToArray();
        Assert.DoesNotContain(
            publicNeuronParameterTypes,
            parameterType =>
                parameterType.Contains("System.Func", StringComparison.Ordinal) ||
                parameterType.Equals("System.Object", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AliasOnAnotherCandidateTypeCannotSatisfyProbeSynapseContract()
    {
        await using var run = CandidateTestRun.Create();
        var source = await File.ReadAllTextAsync(
            FixturePaths.ProbeNeuron,
            TestContext.Current.CancellationToken);
        var alternateAlias = "db.poc.family.cf_cccccccccccccccccccccccccc.other.v1";
        var expectedAlias = "db.poc.family.cf_cccccccccccccccccccccccccc.matched.v1";
        var mutatedSource = source
            .Replace(
                $"[Alias(\"{expectedAlias}\")]\npublic sealed record ProbeSynapse",
                $"[Alias(\"{alternateAlias}\")]\npublic sealed record ProbeSynapse",
                StringComparison.Ordinal)
            .Replace(
                "public sealed class ProbeEmitterNeuron",
                $"[Alias(\"{expectedAlias}\")]\npublic sealed class AliasCarrier {{ }}\n\npublic sealed class ProbeEmitterNeuron",
                StringComparison.Ordinal);
        var proposedSource = Path.Combine(run.ControlPlaneRoot, "wrong-owner-probe-neuron.cs");
        Directory.CreateDirectory(run.ControlPlaneRoot);
        await File.WriteAllTextAsync(
            proposedSource,
            mutatedSource,
            TestContext.Current.CancellationToken);

        var candidate = await FileModuleBuilder.BuildAsync(run, proposedSource);

        Assert.Contains(
            candidate.ContractAliases,
            alias => alias.DeclaringType == "AliasCarrier" && alias.Alias == expectedAlias);
        Assert.DoesNotContain(
            candidate.ContractAliases,
            alias =>
                alias.DeclaringNamespace == "DigitalBrain.Poc.Candidate.cf_cccccccccccccccccccccccccc" &&
                alias.DeclaringType == "ProbeSynapse" &&
                alias.Alias == expectedAlias);
    }

    [Theory]
    [MemberData(nameof(DirectiveMutations))]
    public async Task RejectsDirectiveMutationBeforeCreatingCandidateDirectory(
        string original,
        string replacement)
    {
        await using var run = CandidateTestRun.Create();
        var source = await File.ReadAllTextAsync(
            FixturePaths.ProbeNeuron,
            TestContext.Current.CancellationToken);
        var mutatedSource = source.Replace(original, replacement, StringComparison.Ordinal);
        var proposedSource = Path.Combine(run.ControlPlaneRoot, "mutated-probe-neuron.cs");
        Directory.CreateDirectory(run.ControlPlaneRoot);
        await File.WriteAllTextAsync(
            proposedSource,
            mutatedSource,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => FileModuleBuilder.BuildAsync(run, proposedSource));

        Assert.False(
            Directory.Exists(run.CandidateRoot),
            $"Rejected candidate directory was created: {run.CandidateRoot}");
    }

    public static IEnumerable<object[]> DirectiveMutations()
    {
        yield return
        [
            "#:sdk Microsoft.NET.Sdk",
            "#:sdk Microsoft.NET.Sdk.Web",
        ];
        yield return
        [
            "#:property TargetFramework=net11.0",
            "#:property TargetFramework=net10.0",
        ];
        yield return
        [
            "#:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj",
            "#:project ../../../src/DigitalBrain.Poc.Runtime/DigitalBrain.Poc.Runtime.csproj",
        ];
        yield return
        [
            "#:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj",
            "#:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj\n#:include injected.cs",
        ];
        yield return
        [
            "#:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj",
            "#:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj\n#:package Contoso.Untrusted@1.0.0",
        ];
    }
}
