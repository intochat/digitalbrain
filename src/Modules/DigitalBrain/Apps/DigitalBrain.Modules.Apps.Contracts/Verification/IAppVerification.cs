using DigitalBrain.Contracts;
using DigitalBrain.Specs;
using Orleans.Concurrency;

namespace DigitalBrain.Apps;

// Keyed "{owner}/{name}@{revision}". Runs a revision's app.feature against a scratch installation of that
// revision. A revision that carries a spec reaches the marketplace only after a green verification.
[Alias("apps.verification"), Orleans.Metadata.DefaultGrainType("apps.verification")]
public interface IAppVerification : INeuron
{
    // Scenarios start from the revision's declared defaults and change settings only through visible steps.
    [ResponseTimeout("01:00:00")] Task<AppVerification> Verify();
    [ReadOnly, AlwaysInterleave] Task<AppVerification?> Read();

    static string Key(PackageRevisionRef revision) => $"{revision.Package}@{revision.Revision}";
    // The feature neuron keeps the bound spec and its last run, which a client shows with highlighting.
    static string FeatureKey(PackageRevisionRef revision) => "apps/" + Key(revision);
}

[GenerateSerializer, Alias("apps.app-verification")]
public sealed record AppVerification(
    [property: Id(0)] PackageRevisionRef Revision,
    [property: Id(1)] FeatureRun Run,
    [property: Id(2)] DateTimeOffset VerifiedAt)
{
    public bool Green => Run.Green;
}
