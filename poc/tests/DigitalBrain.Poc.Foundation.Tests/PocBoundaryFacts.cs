using System;
using System.IO;
using Xunit;

namespace DigitalBrain.Poc.Foundation.Tests;

public sealed class PocBoundaryFacts
{
    [Fact]
    public void EveryProjectReferenceStaysInsidePoc()
    {
        var references = ProjectReferenceScanner.ReadAll(PocPaths.Root);

        Assert.All(
            references,
            path => Assert.True(
                PocPaths.IsInside(PocPaths.Root, path),
                $"Project reference escapes the POC root: {path}"));
    }

    [Fact]
    public void RuntimeCandidateDataIsIgnored()
    {
        var ignore = File.ReadAllText(Path.Combine(PocPaths.Root, ".gitignore"));

        Assert.Contains("candidates/", ignore, StringComparison.Ordinal);
        Assert.Contains("control-plane-store/", ignore, StringComparison.Ordinal);
        Assert.Contains("artifacts/", ignore, StringComparison.Ordinal);
        Assert.Contains("pointer-ledger-authority/", ignore, StringComparison.Ordinal);
        Assert.DoesNotContain("*.csproj", ignore, StringComparison.Ordinal);
    }
}
