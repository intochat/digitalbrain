using DigitalBrain.Poc.Runtime;
using Xunit;

namespace DigitalBrain.Poc.Runtime.Tests;

public sealed class PocDataRootFacts
{
    [Fact]
    public async Task DisposalScansEveryOwnedParentForResidualRunArtifacts()
    {
        var pocRoot = Path.Combine(
            Path.GetTempPath(),
            "digitalbrain-poc-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pocRoot);
        var run = PocDataRoot.Create(pocRoot);
        var residualRoots = new[]
        {
            "artifacts",
            "candidates",
            "control-plane-store",
            "pointer-ledger-authority",
        }.Select(parent => Path.Combine(pocRoot, parent, "residual-" + run.RunId)).ToArray();
        try
        {
            foreach (var residual in residualRoots)
            {
                Directory.CreateDirectory(residual);
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => run.DisposeAsync().AsTask());

            var remaining = await PocDataRoot.FindArtifactsForRunAsync(
                pocRoot,
                run.RunId,
                TestContext.Current.CancellationToken);
            Assert.Equal(4, remaining.Count(path => Directory.Exists(path)));
        }
        finally
        {
            foreach (var residual in residualRoots)
            {
                if (Directory.Exists(residual))
                {
                    Directory.Delete(residual, recursive: true);
                }
            }

            if (Directory.Exists(pocRoot))
            {
                Directory.Delete(pocRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DisposingAPocRunErasesAllRegisteredDataAndTestEvidence()
    {
        var pocRoot = TestPocRoot.Find();
        var run = PocDataRoot.Create(pocRoot);
        var runId = run.RunId;
        var rootPath = run.RootPath;
        await File.WriteAllTextAsync(
            Path.Combine(run.JournalPath, "journal-evidence.txt"),
            "journal",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(run.CandidateRoot, "candidate-evidence.txt"),
            "candidate",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(run.ControlPlaneRoot, "control-plane-evidence.txt"),
            "control-plane",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(run.PointerLedgerAuthorityPath, "authority-evidence.txt"),
            "authority",
            TestContext.Current.CancellationToken);

        await run.DisposeAsync();

        Assert.Empty(await PocDataRoot.FindArtifactsForRunAsync(
            pocRoot,
            runId,
            TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(rootPath));
    }
}
