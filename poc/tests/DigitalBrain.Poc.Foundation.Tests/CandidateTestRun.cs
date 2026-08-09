using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DigitalBrain.Poc.Foundation.Tests;

internal sealed class CandidateTestRun : IAsyncDisposable
{
    private static readonly string ScratchParent = Path.Combine(
        Path.GetTempPath(),
        "DigitalBrain.Poc.Foundation.Tests");

    private CandidateTestRun(string runId)
    {
        RunId = runId;
        CandidateRoot = Path.Combine(PocPaths.Root, "candidates", runId);
        ControlPlaneRoot = Path.Combine(PocPaths.Root, "control-plane-store", runId);
        BuildScratch = Path.Combine(ScratchParent, runId);
    }

    public string RunId { get; }

    public string CandidateRoot { get; }

    public string ControlPlaneRoot { get; }

    public string BuildScratch { get; }

    public static CandidateTestRun Create() => new($"run-{Guid.NewGuid():N}");

    public ValueTask DisposeAsync()
    {
        var ownedRoots = new[]
        {
            (Parent: Path.Combine(PocPaths.Root, "candidates"), Owned: CandidateRoot),
            (Parent: Path.Combine(PocPaths.Root, "control-plane-store"), Owned: ControlPlaneRoot),
            (Parent: ScratchParent, Owned: BuildScratch),
        };

        foreach (var (parent, owned) in ownedRoots)
        {
            DeleteOwnedDirectory(parent, owned);
        }

        var remnants = ownedRoots
            .Where(root => Directory.Exists(root.Parent))
            .SelectMany(root => Directory.EnumerateFileSystemEntries(
                root.Parent,
                "*",
                SearchOption.AllDirectories))
            .Where(path => path.Contains(RunId, StringComparison.Ordinal))
            .ToArray();

        if (remnants.Length != 0)
        {
            throw new IOException(
                $"Candidate test run cleanup left paths containing {RunId}:{Environment.NewLine}" +
                string.Join(Environment.NewLine, remnants));
        }

        return ValueTask.CompletedTask;
    }

    private static void DeleteOwnedDirectory(string parent, string owned)
    {
        var resolvedParent = PocPaths.ResolvePhysicalPath(parent);
        var resolvedOwned = PocPaths.ResolvePhysicalPath(owned);
        if (resolvedOwned.Equals(resolvedParent, StringComparison.OrdinalIgnoreCase) ||
            !PocPaths.IsInside(resolvedParent, resolvedOwned))
        {
            throw new InvalidOperationException($"Refusing to remove non-owned test path: {resolvedOwned}");
        }

        if (Directory.Exists(resolvedOwned))
        {
            Directory.Delete(resolvedOwned, recursive: true);
        }
    }
}
