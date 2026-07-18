using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DigitalBrain.Os.Tests;

// Guards the high-severity gate against "tolerant" assertions (Assert.True(true, ...)) creeping back
// into the step bindings: it counts their occurrences and fails if the count exceeds a ceiling that
// A1 ratchets DOWN as stubs become real assertions. The ceiling must never be raised.
public sealed class GateHonestyTests
{
    private const int MaxTolerantAsserts = 0;

    private static string[] BindingsAndSimSources()
    {
        var root = FindRepoRoot();
        var testDir = Path.Combine(root, "src", "DigitalBrain.Os.Tests");
        var files = new List<string>();
        var candidates = new[] { "DistributionSimulationBindings.cs", "Simulation.cs", "DistributionDynamicHandlers.feature.cs", "GoogleAuthU4.feature.cs", "Simulation.feature.cs" };
        foreach (var c in candidates)
        {
            var p = Path.Combine(testDir, c);
            if (File.Exists(p)) files.Add(File.ReadAllText(p));
        }
        // also any other *Bindings.cs for future
        foreach (var f in Directory.EnumerateFiles(testDir, "*Bindings.cs", SearchOption.TopDirectoryOnly))
        {
            if (!files.Any(existing => existing == File.ReadAllText(f))) files.Add(File.ReadAllText(f));
        }
        return files.ToArray();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DigitalBrain.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate DigitalBrain.slnx walking up from " + Directory.GetCurrentDirectory());
    }

    [Fact]
    public void TolerantAssertionCountDoesNotExceedCeiling()
    {
        var sources = BindingsAndSimSources();
        var count = sources.Sum(s => Regex.Matches(s, @"Assert\.True\(true").Count);
        Assert.True(
            count <= MaxTolerantAsserts,
            $"Tolerant Assert.True(true ...) count is {count}, ceiling is {MaxTolerantAsserts}. " +
            "Convert the new stub to a real assertion or @ignore the scenario with a tracking note. " +
            "Never raise the ceiling.");
    }
}
