using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Flutter.Collection;
using DigitalBrain.Files;
using DigitalBrain.Sdk.Identity;
using DigitalBrain.Testing.Unit;
using DigitalBrain.Flutter;
using Microsoft.Extensions.DependencyInjection;
namespace DigitalBrain.Modules.Files.Tests.Unit;

public sealed class LocalAppNeuronFacts
{
    [Fact(Timeout = 180_000)]
    public async Task RealApplicationNeuronsShareDocumentStateAndKeepSavesBoundToTheirRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        var blobs = new MemoryAssetBlobStore();
        await using var brain = await UnitTest.Create().WithModule<FilesModule>().WithModule<FlutterModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IAssetBlobStore>(blobs)).StartAsync(ct);
        const string scope = "files-owner";
        var store = new WorkspaceFileStore(blobs, brain.Grains);
        await store.UploadImageAsync(scope, "image.png", new MemoryStream(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==")), ct);
        var files = brain.Get<IFileExplorer>(scope);
        var surface = await files.Navigate();
        Assert.Equal("surface", surface.Surface!.Kind);
        var collection = await brain.Get<ICollectionView>(scope + "/apps/files/items").Read();
        var entry = Assert.Single(collection.Definition.Items, item => item.Kind == "image").Id;
        var opened = await files.OpenImage(entry);
        Assert.Equal(opened.Id, (await files.OpenImage(entry)).Id);
        var document = brain.Get<IImageDocument>(scope + "/images/" + opened.Id);
        await brain.DeactivateAsync(document, ct);
        Assert.Equal(opened.Versions, (await document.Read()).Versions);
        var operation = Guid.NewGuid().ToString();
        var command = new ImageEditCommand("crop", Crop: new(0, 0, 1, 1));
        var edited = await document.Apply(command, 0, operation);
        Assert.Equal(1, edited.Revision);
        Assert.Equal(1, (await document.Apply(command, 0, operation)).Revision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => document.Apply(new("reset"), 0, Guid.NewGuid().ToString()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => document.Apply(new("reset"), 1, operation));
        var save = await document.PrepareSave(1, Guid.NewGuid().ToString());
        await document.Apply(new("reset"), 1, Guid.NewGuid().ToString());
        var saved = await document.CompleteSave(save.OperationId, new("entry", "image-edited.png", "checksum", 100));
        var replayed = await document.CompleteSave(save.OperationId, new("entry", "image-edited.png", "checksum", 100));
        Assert.Equal(saved.LastSavedRevision, replayed.LastSavedRevision);
        Assert.Equal(2, saved.Revision);
        Assert.Equal(1, saved.LastSavedRevision);
        Assert.NotNull(save.Recipe.Crop);
        var stroke = new DigitalBrain.Flutter.ImageCanvas.PenStroke(0xffff0000, 3, [new(0, 0), new(1, 1)]);
        await document.Apply(new("stroke", Stroke: stroke), 2, Guid.NewGuid().ToString());
        await brain.DeactivateAsync(document, ct);
        var restoredStroke = Assert.Single((await document.Read()).Recipe.Strokes);
        Assert.Equal(stroke.Points, restoredStroke.Points);
        Assert.Equal(stroke.ColorArgb, restoredStroke.ColorArgb);
        Assert.Null((await brain.Get<IImageDocument>("files-other" + "/images/" + opened.Id).Read()).Asset);

    }
}


