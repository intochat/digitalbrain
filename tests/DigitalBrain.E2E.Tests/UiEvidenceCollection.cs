using Xunit;

namespace DigitalBrain.E2E.Tests;

// The env-gated UI evidence leg (UiEvidenceTests) cannot join E2ECollection: it needs a
// DIFFERENT AppHost boot (AppHost:UiHost=web selects the web shell; AppHost.cs:27-38) than the
// classic collection's shared fixture, and the kernel's HTTP endpoint is unproxied on a fixed
// port (AppHost.cs UiHttpPort=5080, spared by BrainAppHostFixture.RandomizeProxiedPorts) -- two
// live AppHosts on that port collide regardless of container-name isolation. So this collection
// carries no ICollectionFixture at all; UiEvidenceTests boots and disposes its own dedicated
// BrainAppHostFixture entirely inside the test body, only once DIGITALBRAIN_UI_EVIDENCE is set.
//
// Safety against overlapping E2ECollection's warm boot rests on two layers:
// 1. E2ECollection.DisableParallelization already forbids that collection from running
//    alongside ANY other collection in the assembly (xunit.v3's own documented behavior --
//    CollectionDefinitionAttribute.DisableParallelization: "Determines whether tests in this
//    collection runs in parallel with any other collections" -- the same guarantee
//    BddBrainHost's feature-scoped boot relies on; task-2-report.md observed it hold in both
//    execution orders xunit picked).
// 2. This collection also sets DisableParallelization, so the guarantee holds even if
//    E2ECollection's own flag is ever weakened.
//
// In ungated runs this costs nothing: DIGITALBRAIN_UI_EVIDENCE is unset, so the gated body never
// executes -- Assert.Skip fires before anything boots. When a human runs it gated, they run
// ONLY this test (e.g. `dotnet test --filter UiEvidenceTests` or the built exe's `--filter`
// equivalent) so no other collection is even scheduled in the same process.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiEvidenceCollection
{
    public const string Name = "ui-evidence";
}
