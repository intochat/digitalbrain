using System.Runtime.CompilerServices;
using DigitalBrain.InoLang.Tests;
using DigitalBrain.Runtime;
using DigitalBrain.InoLang.Linking;

namespace DigitalBrain.InoLang.Tests.Runner;

// End-to-end proof: the runner discovers and gates the canonical Genesis
// neuron at src/boot/DigitalBrain.ino. If this ever goes red, the boot floor
// itself has lost its spec-first invariant — that's the kind of breakage
// v3 §L6 says the runtime must refuse to activate against.
public sealed class CanonicalBootScenarioTests
{
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

    static IContractCatalog BootCatalog() => BootstrapCatalog.Default;

    [Fact]
    public async Task DigitalBrain_ino_runs_green_through_the_runner()
    {
        var bootDir = Path.Combine(SolutionRoot(), "kernel", "DigitalBrain.Runtime", "Genesis");

        var report = await InoTestRunner.RunDirectoryAsync(
            bootDir, BootCatalog(), CancellationToken.None);

        var diagnostics = report.Files
            .SelectMany(f => f.CompileDiagnostics.Select(d => $"{f.RelativePath}: {d.Code} {d.Message}"));
        var failures = report.Files
            .Where(f => f.Scenarios is not null)
            .SelectMany(f => f.Scenarios!.Results.SelectMany(r => r.Failures.Select(x => $"{f.RelativePath} :: {r.Name} :: {x}")));

        report.Files.Should().NotBeEmpty("the canonical DigitalBrain.ino must be discoverable");
        report.AllPassed.Should().BeTrue(
            "diagnostics=[" + string.Join(" | ", diagnostics) + "] failures=[" + string.Join(" | ", failures) + "]");
    }
}
