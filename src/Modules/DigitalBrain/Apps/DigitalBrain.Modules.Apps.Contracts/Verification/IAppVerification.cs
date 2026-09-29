using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Apps;

// Keyed "{owner}/{name}@{revision}". Runs a revision's tests.cs as a sandbox script; the script
// installs its own scratch apps, drives them through contracts, and reports one dbtest line per
// scenario. A revision that carries a spec or tests reaches the marketplace only after a green run.
[Alias("apps.verification"), Orleans.Metadata.DefaultGrainType("apps.verification")]
public interface IAppVerification : INeuron
{
    [ResponseTimeout("01:00:00")] Task<AppVerification> Verify();
    [ReadOnly, AlwaysInterleave] Task<AppVerification?> Read();

    static string Key(PackageRevisionRef revision) => $"{revision.Package}@{revision.Revision}";
}

[GenerateSerializer, Alias("apps.scenario-verdict")]
public sealed record AppScenarioVerdict(
    [property: Id(0)] string Name,
    [property: Id(1)] bool Passed,
    [property: Id(2)] string Message);

// The tests script's whole verdict: its dbtest lines and its exit code. A run that reported no
// scenario is never green, so a script that crashes early cannot pass by silence.
[GenerateSerializer, Alias("apps.test-run")]
public sealed record AppTestRun(
    [property: Id(0)] AppScenarioVerdict[] Scenarios,
    [property: Id(1)] int? ExitCode)
{
    public bool Green => ExitCode == 0 && Scenarios.Length > 0 && Scenarios.All(scenario => scenario.Passed);
}

[GenerateSerializer, Alias("apps.app-verification")]
public sealed record AppVerification(
    [property: Id(0)] PackageRevisionRef Revision,
    [property: Id(1)] AppTestRun Run,
    [property: Id(2)] DateTimeOffset VerifiedAt)
{
    public bool Green => Run.Green;
}
