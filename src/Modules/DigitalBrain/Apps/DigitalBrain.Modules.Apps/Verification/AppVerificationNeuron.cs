using DigitalBrain.Apps.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.verification-state")]
public sealed record AppVerificationState
{
    [Id(0)] public AppVerification? Last { get; init; }
}

[GrainType("apps.verification")]
internal sealed class AppVerificationNeuron(
    // A fresh storage name: the pre-tests state carried a FeatureRun and would not deserialize.
    [PersistentState("apps.test-verification", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppVerificationState> store,
    ITestScriptRunner runner,
    TimeProvider clock, AppRequirements requirements)
    : Neuron<AppVerificationState>(store), IAppVerification
{
    public async Task<AppVerification> Verify()
    {
        var revisionRef = Parse(this.GetPrimaryKeyString());
        var revision = await GrainFactory.GetGrain<IPackage>(revisionRef.Package.ToString()).ReadRevision(revisionRef.Revision);
        requirements.Check(revision.Content);
        var tests = revision.Content.File(PackageContent.TestsPath)
            ?? throw new InvalidOperationException($"{revisionRef.Package}@{revisionRef.Revision} has no {PackageContent.TestsPath}, so there is nothing to verify.");
        var run = await runner.RunAsync(revisionRef, tests, CancellationToken.None);
        var verification = new AppVerification(revisionRef, run, clock.GetUtcNow());
        await Save(new AppVerificationState { Last = verification }, new AppVerified(revisionRef, verification.Green));
        return verification;
    }

    public Task<AppVerification?> Read() => Task.FromResult(Snapshot.Last);

    private static PackageRevisionRef Parse(string key)
    {
        var at = key.LastIndexOf('@');
        if (at <= 0 || at == key.Length - 1) { throw new ArgumentException($"'{key}' is not '{{owner}}/{{name}}@{{revision}}'."); }
        return new(PackageId.Parse(key[..at]), key[(at + 1)..]);
    }
}
