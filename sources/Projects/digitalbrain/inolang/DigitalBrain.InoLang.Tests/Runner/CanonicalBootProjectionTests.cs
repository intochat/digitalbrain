using System.Runtime.CompilerServices;
using DigitalBrain.Runtime;
using DigitalBrain.InoLang.Linking;

namespace DigitalBrain.InoLang.Tests.Runner;

// End-to-end proof of MTP scenario projection: every Genesis scenario in
// src/boot/DigitalBrain.ino must surface as its own addressable [Theory] row. If
// `dotnet test --filter "DisplayName~boot spawns the cluster"` ever returns
// zero matches, the projection has regressed (or Genesis lost its scenario,
// which is the v3 §L6 invariant the runtime refuses to activate against).
public sealed class CanonicalBootProjectionTests
{
    static string BootDir => Path.Combine(SolutionRoot(), "kernel", "DigitalBrain.Runtime", "Genesis");

    static IContractCatalog BootCatalog() => BootstrapCatalog.Default;

    public static IEnumerable<TheoryDataRow<string, string, string>> BootScenarios()
        => InoScenarioProjection.Discover(BootDir);

    [Theory]
    [MemberData(nameof(BootScenarios))]
    public async Task Genesis_scenario_passes(string relativePath, string scenarioName, string scenarioKey)
    {
        var report = await InoScenarioProjection.RunAsync(
            BootDir, relativePath, scenarioName, scenarioKey,
            BootCatalog(), CancellationToken.None);

        Assert.True(report.Passed, report.Message);
    }

    [Fact]
    public void Genesis_is_discoverable_as_at_least_one_addressable_row()
    {
        InoScenarioProjection.Discover(BootDir).Should().NotBeEmpty(
            "the canonical Genesis neuron must surface at least one addressable scenario row");
    }

    static string SolutionRoot([CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DigitalBrain.slnx")) || File.Exists(Path.Combine(dir.FullName, "DigitalBrain.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("DigitalBrain.slnx or DigitalBrain.slnx not found above " + thisFile);
    }
}
