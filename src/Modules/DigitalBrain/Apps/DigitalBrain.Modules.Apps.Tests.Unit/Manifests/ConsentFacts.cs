using DigitalBrain.Apps;
using DigitalBrain.Testing.Unit;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

public sealed class ConsentFacts
{
    [Fact]
    public async Task ApprovingACataloguedAppRecordsConsentAndInstallsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        var manifest = ManifestGenerator.FromInterface<IWidgetNeuron>(new AppManifestSeed
        {
            Version = "1.0.0",
            Publisher = "tests",
            Kind = AppKind.Declarative,
            DescriptionForPeople = "A test widget.",
            DescriptionForModel = "Read and rename a test widget.",
        });
        await brain.Get<IAppManifestDirectory>(AppManifestDirectoryGrains.Key).Publish(manifest, "workspace-a");

        var consent = brain.Get<IAppConsent>("workspace-a");
        var sheet = await consent.Approve("test.widget");

        Assert.True(sheet.Approved);
        Assert.True(await consent.IsApproved("test.widget"));
        var installed = Assert.Single(await brain.Get<IAppCatalog>("workspace-a").List());
        Assert.Equal("test.widget", installed.Manifest.Id);
    }

    [Fact]
    public async Task AnotherWorkspacesPublicationIsNotConsentableAcrossTheBoundary()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        var manifest = ManifestGenerator.FromInterface<IWidgetNeuron>(new AppManifestSeed
        {
            Version = "1.0.0",
            Publisher = "tests",
            Kind = AppKind.Declarative,
            DescriptionForPeople = "A test widget.",
            DescriptionForModel = "Read and rename a test widget.",
        });
        await brain.Get<IAppManifestDirectory>(AppManifestDirectoryGrains.Key).Publish(manifest, "workspace-a");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => brain.Get<IAppConsent>("workspace-b").Review("test.widget"));
    }

    [Fact]
    public async Task AnUncataloguedAppIsRefusedConsent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => brain.Get<IAppConsent>("workspace-a").Review("nobody.nothing"));
    }
}
