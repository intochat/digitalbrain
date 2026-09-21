using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Collection;
using DigitalBrain.Flutter.ImageCanvas;
using IntoChat.LocalFiles;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.Tabs;
namespace IntoChat.Apps;

internal sealed class AppSurfaceComposer(IGrainFactory grains)
{
    public async Task<UiChildRef> Files(string name, DirectoryPage page, int offset = 0, string sort = "name", string filter = "")
    {
        var collection = grains.GetGrain<ICollectionView>(name + "/items");
        await collection.Set(new(page.Items, Cursor: page.NextOffset?.ToString(System.Globalization.CultureInfo.InvariantCulture)), (await collection.Read()).Revision);
        var toolbar = new List<UiChildRef>();
        async Task AddButton(string id, string label, string action, bool enabled = true)
        {
            var button = grains.GetGrain<IButton>(name + "/" + id);
            await button.Set(label, action, enabled);
            toolbar.Add(new("button", name + "/" + id));
        }
        await AddButton("parent", "Up", "folder:" + page.ParentId, page.ParentId is not null);
        var index = 0;
        foreach (var crumb in page.Breadcrumbs ?? []) { await AddButton("crumb-" + index++, crumb.Label, "folder:" + crumb.Id); }
        var field = grains.GetGrain<ITextField>(name + "/filter");
        await field.Configure("Filter files", "text"); await field.SetValue(filter);
        toolbar.Add(new("textfield", name + "/filter"));
        await AddButton("sort", "Sort: " + sort, "sort");
        await AddButton("refresh", "Refresh files", "refresh");
        var bar = grains.GetGrain<ILayout>(name + "/toolbar");
        await bar.Set(new("row", toolbar, 4), (await bar.Read()).Revision);
        var status = grains.GetGrain<IText>(name + "/status");
        await status.Set("Local filesystem · " + page.Items.Count + " items");
        var previous = grains.GetGrain<IButton>(name + "/previous"); await previous.Set("Previous", "previous", offset > 0);
        var next = grains.GetGrain<IButton>(name + "/next"); await next.Set("Next", "next", page.NextOffset is not null);
        var footer = grains.GetGrain<ILayout>(name + "/footer");
        await footer.Set(new("row", [new("text", name + "/status"), new("button", name + "/previous"), new("button", name + "/next")], 4, [0, 90, 90]), (await footer.Read()).Revision);
        return await Compose(name, "Files", [new("layout", name + "/toolbar"), new("collection", name + "/items"), new("layout", name + "/footer")], [56, 0, 38]);
    }
    public async Task RegisterDocument(string scope, ImageDocumentState document)
    {
        var name = scope + "/apps/image-editor";
        var tabs = grains.GetGrain<ITabs>(name + "/tabs");
        var state = await tabs.Read();
        var items = state.Tabs.ToList();
        if (!items.Any(x => x.Id == document.Id)) { items.Add(new(document.Id, document.Asset!.Name, document.Surface!)); }
        await tabs.Set(items, document.Id);
        await Compose(name, "Image Editor", [new("tabs", name + "/tabs")]);
    }
    public async Task<UiChildRef> Image(string name, ImageAsset asset, ImageRecipe recipe, long revision)
    {
        var canvas = grains.GetGrain<IImageCanvas>(name + "/canvas");
        await canvas.Set(new(asset.Id, asset.Width, asset.Height, recipe, revision), (await canvas.Read()).Revision);
        return await Compose(name, "Image Editor", [new("imagecanvas", name + "/canvas")]);
    }
    private async Task<UiChildRef> Compose(string name, string title, UiChildRef[] children, IReadOnlyList<double>? extents = null)
    {
        var layout = grains.GetGrain<ILayout>(name + "/layout");
        await layout.Set(new("column", children, 4, extents), (await layout.Read()).Revision);
        var surface = grains.GetGrain<ISurface>(name + "/surface");
        await surface.Set(new(title, [new("layout", name + "/layout")]), (await surface.Read()).Revision);
        return new("surface", name + "/surface");
    }
}
