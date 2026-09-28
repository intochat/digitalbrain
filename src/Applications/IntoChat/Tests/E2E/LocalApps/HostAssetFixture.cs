using DigitalBrain.Contracts;
using DigitalBrain.Flutter.Collection;
using DigitalBrain.Files;

namespace IntoChat.Tests.E2E.LocalApps;

internal static class HostAssetFixture
{
    internal static async Task Import(IDigitalBrain brain, string scope)
    {
        var explorer = brain.Get<IFileExplorer>(scope);
        var collection = brain.Get<ICollectionView>(scope + "/apps/files/items");
        await explorer.Navigate(null);
        var folder = Assert.Single((await collection.Read()).Definition.Items, item => item.Kind == "folder");
        await explorer.Navigate(folder.Id);
        foreach (var image in (await collection.Read()).Definition.Items.Where(item => item.Kind == "image"))
        { await explorer.OpenImage(image.Id); }
        await explorer.Navigate(null);
    }
}
