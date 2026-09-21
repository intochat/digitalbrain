using DigitalBrain.Flutter.Collection;
using IntoChat.Apps;
using IntoChat.Workspace;
namespace IntoChat.Tests.E2E.LocalApps;

public sealed class LocalAppNeuronFacts
{
    [Fact(Timeout = 180_000)]
    public async Task RealApplicationNeuronsShareDocumentStateAndKeepSavesBoundToTheirRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("intochat-neurons-").FullName;
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "image.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg=="), ct);
            await using var brain = await IntoChatE2ETest.Create(privateConfiguration: new() { ["IntoChat:LocalFiles:Roots:downloads"] = root, ["IntoChat:LocalFiles:AssetDirectory"] = Path.Combine(root, "assets") }).StartAsync(ct);
            var scope = WorkspaceScope.Create("owner", "local-neurons").Id;
            var files = brain.Get<IFileExplorer>(scope);
            var surface = await files.Navigate(null);
            Assert.Equal("surface", surface.Surface!.Kind);
            var collection = await brain.Get<ICollectionView>(scope + "/apps/files/items").Read();
            var entry = Assert.Single(collection.Definition.Items).Id;
            var opened = await files.OpenImage(entry);
            Assert.Equal(opened.Id, (await files.OpenImage(entry)).Id);
            var document = brain.Get<IImageDocument>(scope + "/images/" + opened.Id);
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
            Assert.Null((await brain.Get<IImageDocument>(WorkspaceScope.Create("owner", "another").Id + "/images/" + opened.Id).Read()).Asset);
        }
        finally { Directory.Delete(root, true); }
    }
}
