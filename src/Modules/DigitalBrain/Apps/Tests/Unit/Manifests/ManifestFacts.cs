using System.ComponentModel;
using DigitalBrain.Apps;
using DigitalBrain.Apps.Manifests;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

[Alias("test.widget")]
public interface IWidgetNeuron : INeuron
{
    [Orleans.Concurrency.ReadOnly, Description("Read the widget.")]
    Task<WidgetView> Read();

    [Description("Rename the widget.")]
    Task<WidgetView> Rename(string name, int revision);
}

[GenerateSerializer, Alias("test.widget-view")]
public sealed record WidgetView
{
    [Id(0)] public required string Name { get; init; }
}

public sealed class ManifestFacts
{
    private static AppManifestSeed Seed() => new()
    {
        Version = "1.0.0",
        Publisher = "tests",
        Kind = AppKind.Declarative,
        DescriptionForPeople = "A test widget.",
        DescriptionForModel = "Read and rename a test widget.",
    };

    [Fact]
    public void GeneratedManifestCannotDriftFromTheInterface()
    {
        var manifest = ManifestGenerator.FromInterface<IWidgetNeuron>(Seed());

        Assert.Equal("test.widget", manifest.Id);
        Assert.Equal("WidgetNeuron", manifest.Name);
        Assert.Equal(["Read", "Rename"], manifest.Operations.Select(operation => operation.Name));

        var read = manifest.Operations.Single(operation => operation.Name == "Read");
        Assert.True(read.ReadOnly);
        Assert.Equal("Read the widget.", read.DescriptionForModel);
        Assert.Equal("reference", read.OutputTypeId);

        var rename = manifest.Operations.Single(operation => operation.Name == "Rename");
        Assert.False(rename.ReadOnly);
        Assert.Equal("Rename the widget.", rename.DescriptionForModel);
        Assert.Equal("plain-text", rename.InputTypeIds["name"]);
        Assert.Equal("number", rename.InputTypeIds["revision"]);
    }

    [Fact]
    public void GeneratedManifestIsTheSameOnEveryRun()
    {
        var first = ManifestGenerator.FromInterface<IWidgetNeuron>(Seed());
        var second = ManifestGenerator.FromInterface<IWidgetNeuron>(Seed());
        Assert.Equal(AppManifestJson.Serialize(first), AppManifestJson.Serialize(second));
    }

    [Fact]
    public void AppJsonRoundTripsThroughTheWireForm()
    {
        var manifest = ManifestGenerator.FromInterface<IWidgetNeuron>(Seed());
        var json = AppManifestJson.Serialize(manifest);
        Assert.Contains("\"id\": \"test.widget\"", json);
        Assert.Equal(json, AppManifestJson.Serialize(AppManifestJson.Deserialize(json)));
    }

    [Fact]
    public void ValidationRejectsATypeOutsideTheCatalog()
    {
        var manifest = ManifestGenerator.FromInterface<IWidgetNeuron>(Seed()) with
        {
            Operations =
            [
                new AppOperation
                {
                    Name = "Broken",
                    DescriptionForModel = "References a type the catalog does not know.",
                    InputTypeIds = new Dictionary<string, string> { ["value"] = "not-a-catalog-type" },
                },
            ],
        };

        var error = Assert.Throws<AppManifestException>(() => ManifestValidator.Validate(manifest));
        Assert.Contains("not-a-catalog-type", error.Message);
    }

    [Fact]
    public void EveryFirstPartyAppCarriesAValidGeneratedManifest()
    {
        var manifests = FirstPartyApps.All();
        Assert.Equal(5, manifests.Count);
        Assert.All(manifests, ManifestValidator.Validate);
        foreach (var id in new[] { "intochat.files", "intochat.forms", "intochat.image-editor", "intochat.customer-tables", "intochat.leadgenerator" })
        {
            Assert.True(FirstPartyApps.Contains(id), $"Missing first-party manifest '{id}'.");
            Assert.NotEmpty(FirstPartyApps.Get(id).Operations);
        }
    }

    [Fact]
    public async Task InstallKeepsVersionsImmutableAndRollsBack()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        var catalog = brain.Get<IAppCatalog>("workspace-a");
        var v1 = await catalog.Install(Widget("1.0.0"));
        await catalog.Install(Widget("2.0.0"));

        Assert.Equal("2.0.0", (await catalog.Read("test.widget"))!.Manifest.Version);
        Assert.Equal(v1.InstalledAt, (await catalog.Install(Widget("1.0.0"))).InstalledAt);

        var rolledBack = await catalog.Rollback("test.widget");
        Assert.Equal("1.0.0", rolledBack.Manifest.Version);
        Assert.Equal("1.0.0", (await catalog.Read("test.widget"))!.Manifest.Version);
    }

    [Fact]
    public async Task UninstallStatesWhichDataIsKept()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        var catalog = brain.Get<IAppCatalog>("workspace-a");
        await catalog.Install(Widget("1.0.0") with
        {
            UiEntry = "app-widget",
            Permissions = [new AppPermission { SemanticTypeId = "plain-text", Reason = "Show a name." }],
        });

        var outcome = await catalog.Uninstall("test.widget");

        Assert.Equal("test.widget", outcome.AppId);
        Assert.Contains("test.widget:data", outcome.KeptData);
        Assert.Contains("plain-text", outcome.KeptData);
        Assert.Contains("test.widget:install", outcome.RemovedData);
        Assert.Contains("app-widget", outcome.RemovedData);
        Assert.Null(await catalog.Read("test.widget"));
        Assert.Empty(await catalog.List());
    }

    private static AppManifest Widget(string version) => ManifestGenerator.FromInterface<IWidgetNeuron>(Seed() with { Version = version });
}
