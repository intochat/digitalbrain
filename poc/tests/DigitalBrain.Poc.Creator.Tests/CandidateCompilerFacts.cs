using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Poc.Runtime;
using Xunit;

namespace DigitalBrain.Poc.Creator.Tests;

public sealed class CandidateCompilerFacts
{
    [Fact]
    public async Task CandidateIdentityRejectsNonCanonicalUppercaseHash()
    {
        await using var run = PocDataRoot.Create(FindPocRoot());

        Assert.Throws<FormatException>(() =>
            new CandidateRepository().DirectoryFor(new string('A', 64), run));
    }

    [Fact]
    public async Task ValidCandidateCreatesOneSourceAndVerifiedManagedAssembly()
    {
        await using var run = PocDataRoot.Create(FindPocRoot());
        var repository = new CandidateRepository();
        var compiler = new FileCandidateCompiler(repository);

        var compiled = await compiler.CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            run,
            TestContext.Current.CancellationToken);

        Assert.Equal("elon-chart.cs", Path.GetFileName(compiled.SourcePath));
        Assert.Single(Directory.EnumerateFiles(compiled.Directory, "*.cs", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(compiled.Directory, "*.csproj", SearchOption.AllDirectories));
        Assert.True(compiled.Manifest.SourceHashVerified);
        Assert.True(compiled.Manifest.AssemblyHashVerified);
        Assert.Equal(CandidateStatus.AwaitingQuarantine, compiled.Manifest.Status);
        Assert.True(File.Exists(compiled.AssemblyPath));
        Assert.False(compiled.ScratchDirectory.StartsWith(compiled.Directory, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            HashText(string.Join(
                "\n",
                CandidateSemanticPolicy.SocialPostObservedAlias,
                "db.poc.family.cf_aaaaaaaaaaaaaaaaaaaaaaaaaa.matched.v1",
                "db.poc.chart.add-point.v1")),
            compiled.Manifest.ContractsHash);
        Assert.Equal(
            HashText("ElonPostRuleState|AcceptedCount:int|v1"),
            compiled.Manifest.StateSchemaHash);
    }

    [Fact]
    public async Task SdkDiscoveryFailureRecordsExternalDiagnosticRemovesPartialCandidateAndAllowsRetry()
    {
        await using var run = PocDataRoot.Create(FindPocRoot());
        var repository = new CandidateRepository();
        var intent = ElonChartAuthoringIntent.DefaultTrustedFixture;
        var source = new ElonChartSyntaxFactory().Create(intent).Source;
        var id = Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(source))).ToLowerInvariant();
        var compiler = new FileCandidateCompiler(
            repository,
            static (_, arguments, _) => arguments.SequenceEqual(["--version"], StringComparer.Ordinal)
                ? Task.FromException<string>(new InvalidOperationException("SDK discovery failed."))
                : Task.FromResult(string.Empty));

        await Assert.ThrowsAsync<InvalidOperationException>(() => compiler.CompileAsync(
            intent,
            run,
            TestContext.Current.CancellationToken));

        Assert.True(File.Exists(Path.Combine(run.CandidateEvidencePath, $"{id}-build.txt")));
        Assert.False(Directory.Exists(repository.DirectoryFor(id, run)));

        var retry = await new FileCandidateCompiler(repository).CompileAsync(
            intent,
            run,
            TestContext.Current.CancellationToken);

        Assert.Equal(id, retry.Id);
    }

    [Fact]
    public async Task SdkDiscoveryCancellationRecordsExternalDiagnosticAndRemovesPartialCandidate()
    {
        await using var run = PocDataRoot.Create(FindPocRoot());
        var repository = new CandidateRepository();
        var intent = ElonChartAuthoringIntent.DefaultTrustedFixture;
        var source = new ElonChartSyntaxFactory().Create(intent).Source;
        var id = Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(source))).ToLowerInvariant();
        var compiler = new FileCandidateCompiler(
            repository,
            static (_, _, _) => Task.FromCanceled<string>(new CancellationToken(canceled: true)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compiler.CompileAsync(
            intent,
            run,
            TestContext.Current.CancellationToken));

        Assert.True(File.Exists(Path.Combine(run.CandidateEvidencePath, $"{id}-build.txt")));
        Assert.False(Directory.Exists(repository.DirectoryFor(id, run)));
    }

    private static string FindPocRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var solution = Path.Combine(current.FullName, "poc", "DigitalBrain.Poc.slnx");
            if (File.Exists(solution))
            {
                return Path.GetDirectoryName(solution)!;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the POC root.");
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(value)))
            .ToLowerInvariant();
}
