using DigitalBrain.Apps.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Specs;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.verification-state")]
public sealed record AppVerificationState
{
    [Id(0)] public AppVerification? Last { get; init; }
}

[GrainType("apps.verification")]
internal sealed class AppVerificationNeuron(
    [PersistentState("apps.verification", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppVerificationState> store,
    TimeProvider clock)
    : Neuron<AppVerificationState>(store), IAppVerification
{
    public async Task<AppVerification> Verify()
    {
        var revisionRef = Parse(this.GetPrimaryKeyString());
        var revision = await GrainFactory.GetGrain<IPackage>(revisionRef.Package.ToString()).ReadRevision(revisionRef.Revision);
        var spec = revision.Content.File(PackageContent.SpecPath)
            ?? throw new InvalidOperationException($"{revisionRef.Package} has no {PackageContent.SpecPath}, so there is nothing to verify.");
        var feature = GrainFactory.GetGrain<IFeature>(IAppVerification.FeatureKey(revisionRef));
        var bound = await feature.Set(spec);
        if (bound.Problem is { } problem) { throw new ArgumentException($"{PackageContent.SpecPath} line {problem.Line}: {problem.Message}"); }

        // Every scenario gets its own fresh installation, so nothing another scenario or an earlier run
        // changed, such as a setting, can make it pass or fail.
        var scratchPrefix = $"specs/{revisionRef.Package}/{revisionRef.Revision}/{Guid.NewGuid():N}/";
        var scratches = bound.Scenarios.Select(scenario => GrainFactory.GetGrain<IApp>(scratchPrefix + scenario.Line.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        FeatureRun run;
        try
        {
            foreach (var scratch in scratches) { await scratch.Install(new InstallApp(Guid.NewGuid(), revisionRef, new Dictionary<string, string>())); }
            run = await feature.Run(scratchPrefix + FeatureSnapshot.ScenarioPlaceholder);
        }
        finally
        {
            foreach (var scratch in scratches) { await Discard(scratch); }
        }
        var verification = new AppVerification(revisionRef, run, clock.GetUtcNow());
        await Save(new AppVerificationState { Last = verification }, new AppVerified(revisionRef, verification.Green));
        return verification;
    }

    // A scratch app that cannot be removed must not hide the run's outcome or keep the others installed.
    private static async Task Discard(IApp scratch)
    {
        try
        {
            if ((await scratch.Read()).Status == AppStatus.Installed) { await scratch.Uninstall(new UninstallApp(Guid.NewGuid())); }
        }
        catch (Exception error) when (error is not OperationCanceledException) { }
    }

    public Task<AppVerification?> Read() => Task.FromResult(Snapshot.Last);

    private static PackageRevisionRef Parse(string key)
    {
        var at = key.LastIndexOf('@');
        if (at <= 0 || at == key.Length - 1) { throw new ArgumentException($"'{key}' is not '{{owner}}/{{name}}@{{revision}}'."); }
        return new(PackageId.Parse(key[..at]), key[(at + 1)..]);
    }
}
