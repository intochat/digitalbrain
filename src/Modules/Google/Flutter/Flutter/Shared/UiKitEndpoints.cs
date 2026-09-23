using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Calendar;
using DigitalBrain.Flutter.Card;
using DigitalBrain.Flutter.Chart;
using DigitalBrain.Flutter.Clock;
using DigitalBrain.Flutter.Collection;
using DigitalBrain.Flutter.Color;
using DigitalBrain.Flutter.Expander;
using DigitalBrain.Flutter.Graph;
using DigitalBrain.Flutter.Image;
using DigitalBrain.Flutter.ImageCanvas;
using DigitalBrain.Flutter.InfoBar;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Map;
using DigitalBrain.Flutter.Person;
using DigitalBrain.Flutter.Progress;
using DigitalBrain.Flutter.Rating;
using DigitalBrain.Flutter.Sheet;
using DigitalBrain.Flutter.Slider;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.Table;
using DigitalBrain.Flutter.Tabs;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Flutter.Toggle;
using DigitalBrain.Flutter.Tree;
using DigitalBrain.Flutter.Video;
using DigitalBrain.Flutter.WebBrowser;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Flutter;

internal static class UiKitEndpoints
{
    public static void MapUiKit(this IEndpointRouteBuilder endpoints)
    {
        UiHttp.MapGet<ISurface, SurfaceState>(endpoints, "surfaces", neuron => neuron.Read());
        UiHttp.MapGet<ILayout, LayoutState>(endpoints, "layouts", neuron => neuron.Read());
        UiHttp.MapGet<ICollectionView, CollectionState>(endpoints, "collections", neuron => neuron.Read());
        UiHttp.MapGet<IImageCanvas, ImageCanvasState>(endpoints, "imagecanvass", neuron => neuron.Read());
        UiHttp.MapGet<IButton, ButtonState>(endpoints, "buttons", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "buttons/{name}/set", async (string name, ButtonSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IButton>(name).Set(body.Label, body.Action, body.Enabled).WaitAsync(ct);
            return Results.Accepted();
        });
        UiHttp.MapPost(endpoints, "buttons/{name}/click", async (string name, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IButton>(name).Click().WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IToggle, ToggleState>(endpoints, "toggles", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "toggles/{name}/set", async (string name, ToggleSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IToggle>(name).Set(body.Label, body.On).WaitAsync(ct);
            return Results.Accepted();
        });
        UiHttp.MapPost(endpoints, "toggles/{name}/flip", async (string name, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IToggle>(name).Flip().WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IText, TextState>(endpoints, "texts", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "texts/{name}", async (string name, TextSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IText>(name).Set(body.Markdown).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapPost(endpoints, "textfields/{name}/configure", async (string name, TextFieldConfigure body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<ITextField>(name).Configure(body.Label, body.Kind).WaitAsync(ct);
            return Results.Accepted();
        });
        UiHttp.MapPost(endpoints, "textfields/{name}/value", async (string name, TextFieldValue body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<ITextField>(name).SetValue(body.Value).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<ISlider, SliderState>(endpoints, "sliders", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "sliders/{name}/configure", async (string name, SliderConfigure body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<ISlider>(name).Configure(body.Min, body.Max, body.Step).WaitAsync(ct);
            return Results.Accepted();
        });
        UiHttp.MapPost(endpoints, "sliders/{name}/value", async (string name, SliderValue body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<ISlider>(name).SetValue(body.Value).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IProgress, ProgressState>(endpoints, "progress", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "progress/{name}", async (string name, ProgressSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IProgress>(name).Set(body.Determinate, body.Value, body.Label).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IInfoBar, InfoBarState>(endpoints, "infobars", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "infobars/{name}/show", async (string name, InfoBarShow body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IInfoBar>(name).Show(body.Severity, body.Title, body.Body).WaitAsync(ct);
            return Results.Accepted();
        });
        UiHttp.MapPost(endpoints, "infobars/{name}/dismiss", async (string name, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IInfoBar>(name).Dismiss().WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<ICard, CardState>(endpoints, "cards", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "cards/{name}", async (string name, CardSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<ICard>(name).Set(body.Title, body.Body, body.Children).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IImage, ImageState>(endpoints, "images", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "images/{name}", async (string name, ImageSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IImage>(name).Set(body.Url, body.MediaType, body.Prompt).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IVideo, VideoState>(endpoints, "videos", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "videos/{name}/load", async (string name, VideoLoad body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IVideo>(name).Load(body.Url, body.Duration).WaitAsync(ct);
            return Results.Accepted();
        });
        UiHttp.MapPost(endpoints, "videos/{name}/play", async (string name, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IVideo>(name).Play().WaitAsync(ct);
            return Results.Accepted();
        });
        UiHttp.MapPost(endpoints, "videos/{name}/pause", async (string name, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IVideo>(name).Pause().WaitAsync(ct);
            return Results.Accepted();
        });
        UiHttp.MapPost(endpoints, "videos/{name}/seek", async (string name, VideoSeek body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IVideo>(name).Seek(body.Seconds).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IWebBrowser, WebBrowserState>(endpoints, "browsers", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "browsers/{name}/navigate", async (string name, BrowserNavigate body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IWebBrowser>(name).Navigate(body.Uri, body.Title).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IChart, ChartState>(endpoints, "charts", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "charts/{name}/render", async (string name, ChartRender body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IChart>(name).Render(body.Title, body.Kind, body.Points).WaitAsync(ct);
            return Results.Accepted();
        });
        UiHttp.MapPost(endpoints, "charts/{name}/append", async (string name, ChartPoint point, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IChart>(name).Append(point).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IGraph, GraphState>(endpoints, "graphs", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "graphs/{name}/render", async (string name, GraphRender body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IGraph>(name).Render(body.Title, body.Nodes, body.Edges).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<ITable, TableState>(endpoints, "tables", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "tables/{name}/replace", async (string name, TableReplace body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<ITable>(name).Replace(body.Title, body.Columns, body.Rows).WaitAsync(ct);
            return Results.Accepted();
        });
        UiHttp.MapPost(endpoints, "tables/{name}/view", async (string name, TableView body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<ITable>(name).SetView(body.Sort, body.Filter).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<ICalendar, CalendarState>(endpoints, "calendars", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "calendars/{name}", async (string name, CalendarSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<ICalendar>(name).Set(body.Mode, body.Selected).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IClock, ClockState>(endpoints, "clocks", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "clocks/{name}", async (string name, ClockSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IClock>(name).Set(body.Label, body.DueAt).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IMap, MapState>(endpoints, "maps", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "maps/{name}", async (string name, MapSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IMap>(name).Set(body.Lat, body.Lng, body.Zoom, body.Markers).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IPerson, PersonState>(endpoints, "people", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "people/{name}", async (string name, PersonSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IPerson>(name).Set(body.DisplayName, body.AvatarUrl).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IRating, RatingState>(endpoints, "ratings", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "ratings/{name}", async (string name, RatingSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IRating>(name).Set(body.Max, body.Value).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IColor, ColorState>(endpoints, "colors", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "colors/{name}", async (string name, ColorSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IColor>(name).Set(body.Hex).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<IExpander, ExpanderState>(endpoints, "expanders", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "expanders/{name}", async (string name, ExpanderSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IExpander>(name).Set(body.Header, body.Expanded, body.Children).WaitAsync(ct);
            return Results.Accepted();
        });
        UiHttp.MapPost(endpoints, "expanders/{name}/toggle", async (string name, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<IExpander>(name).Toggle().WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<ITabs, TabsState>(endpoints, "tabs", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "tabs/{name}", async (string name, TabsSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<ITabs>(name).Set(body.Tabs, body.SelectedId).WaitAsync(ct);
            return Results.Accepted();
        });
        UiHttp.MapPost(endpoints, "tabs/{name}/select", async (string name, TabsSelect body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<ITabs>(name).Select(body.Id).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<ITree, TreeState>(endpoints, "trees", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "trees/{name}", async (string name, TreeSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<ITree>(name).Set(body.Nodes).WaitAsync(ct);
            return Results.Accepted();
        });
        UiHttp.MapPost(endpoints, "trees/{name}/select", async (string name, TreeSelect body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<ITree>(name).Select(body.Id).WaitAsync(ct);
            return Results.Accepted();
        });

        UiHttp.MapGet<ISheet, SheetState>(endpoints, "sheets", neuron => neuron.Read());
        UiHttp.MapPost(endpoints, "sheets/{name}", async (string name, SheetSet body, IGrainFactory grains, CancellationToken ct) =>
        {
            await grains.GetGrain<ISheet>(name).Set(body.Title, body.Cells).WaitAsync(ct);
            return Results.Accepted();
        });
    }
}

internal sealed record ButtonSet(string Label, string Action, bool Enabled = true);
internal sealed record ToggleSet(string Label, bool On);
internal sealed record TextSet(string Markdown);
internal sealed record TextFieldConfigure(string Label, string Kind);
internal sealed record TextFieldValue(string Value);
internal sealed record SliderConfigure(double Min, double Max, double Step);
internal sealed record SliderValue(double Value);
internal sealed record ProgressSet(bool Determinate, double Value, string Label);
internal sealed record InfoBarShow(string Severity, string Title, string Body);
internal sealed record CardSet(string Title, string Body, IReadOnlyList<UiChildRef>? Children);
internal sealed record ImageSet(string Url, string MediaType, string Prompt);
internal sealed record VideoLoad(string Url, double Duration);
internal sealed record VideoSeek(double Seconds);
internal sealed record BrowserNavigate(string Uri, string? Title);
internal sealed record ChartRender(string Title, string Kind, IReadOnlyList<ChartPoint> Points);
internal sealed record GraphRender(string Title, IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);
internal sealed record TableReplace(string Title, IReadOnlyList<TableColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);
internal sealed record TableView(string Sort, string Filter);
internal sealed record CalendarSet(string Mode, IReadOnlyList<string> Selected);
internal sealed record ClockSet(string Label, DateTimeOffset? DueAt);
internal sealed record MapSet(double Lat, double Lng, double Zoom, IReadOnlyList<MapMarker> Markers);
internal sealed record PersonSet(string DisplayName, string AvatarUrl);
internal sealed record RatingSet(int Max, int Value);
internal sealed record ColorSet(string Hex);
internal sealed record ExpanderSet(string Header, bool Expanded, IReadOnlyList<UiChildRef>? Children);
internal sealed record TabsSet(IReadOnlyList<TabItem> Tabs, string SelectedId);
internal sealed record TabsSelect(string Id);
internal sealed record TreeSet(IReadOnlyList<TreeNode> Nodes);
internal sealed record TreeSelect(string Id);
internal sealed record SheetSet(string Title, IReadOnlyList<SheetCell> Cells);