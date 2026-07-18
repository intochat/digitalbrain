using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;

namespace DigitalBrain.Os.UI;

[GenerateSerializer]
public union UiWidget(Button, Text, Card, Column, Row, Markdown, Hyperlink, MainPane, Graph3D, Divider, Icon, TextField, Progress, Toggle, ImageWidget, Container, WindowFrame, BarChart);

[GenerateSerializer]
public sealed record Button(string Label, Synapse? OnTap = null);

[GenerateSerializer]
public sealed record Text(string Value);

[GenerateSerializer]
public sealed record Column(UiWidget[] Children);

[GenerateSerializer]
public sealed record Row(UiWidget[] Children, bool IsRow = true);

[GenerateSerializer]
public sealed record Card(string Title, UiWidget Body);

[GenerateSerializer]
public sealed record Markdown(string Value);

[GenerateSerializer]
public sealed record Hyperlink(string Label, string Url);

[GenerateSerializer]
public sealed record MainPane(UiWidget Content);

[GenerateSerializer]
public sealed record GraphNode(string Id, string Label, string Type);

[GenerateSerializer]
public sealed record GraphEdge(string SourceId, string TargetId, string Type);

[GenerateSerializer]
public sealed record Graph3D(GraphNode[] Nodes, GraphEdge[] Edges);

[GenerateSerializer]
public sealed record Divider(bool IsDivider = true);

[GenerateSerializer]
public sealed record Icon(string Name);

[GenerateSerializer]
public sealed record TextField(string Label, string Value, Synapse? OnChanged = null);

[GenerateSerializer]
public sealed record Progress(double Value, string? Label = null);

[GenerateSerializer]
public sealed record Toggle(string Label, bool Value, Synapse? OnChanged = null);

[GenerateSerializer]
public sealed record ImageWidget(string Url);

[GenerateSerializer]
public sealed record Container(UiWidget Child, double Padding = 0, string? Decoration = null);

[GenerateSerializer]
public sealed record WindowFrame(string Title, UiWidget Content, string WindowId, double X, double Y, double Width, double Height, int ZIndex, string State);

[GenerateSerializer]
public sealed record BarChart(string Title, Bar[] Bars);

[GenerateSerializer]
public sealed record Bar(string Label, double Value, string? Color = null);

public sealed record LiquidGlassDesignTokens(
    string PrimaryColor,
    string SecondaryColor,
    string BackgroundColor,
    string CardColor,
    string ButtonColor,
    string TextColor,
    double BlurSigma,
    double BorderOpacity,
    double BackgroundOpacity,
    double BorderRadiusLarge,
    double BorderRadiusMedium,
    double BorderRadiusSmall,
    double SpacingTiny,
    double SpacingSmall,
    double SpacingMedium,
    double SpacingLarge,
    double MotionShortDurationMs,
    double ShadowBlurRadius,
    double ShadowOffsetY
);

public static class LiquidGlassTheme
{
    public static LiquidGlassDesignTokens Tokens { get; } = new(
        PrimaryColor: "#00E5D1",
        SecondaryColor: "#9E00FF",
        BackgroundColor: "#0A0E1A",
        CardColor: "#121A2A",
        ButtonColor: "#1A2333",
        TextColor: "#E0F2F1",
        BlurSigma: 14.0,
        BorderOpacity: 0.15,
        BackgroundOpacity: 0.60,
        BorderRadiusLarge: 18.0,
        BorderRadiusMedium: 12.0,
        BorderRadiusSmall: 8.0,
        SpacingTiny: 4.0,
        SpacingSmall: 8.0,
        SpacingMedium: 16.0,
        SpacingLarge: 24.0,
        MotionShortDurationMs: 200.0,
        ShadowBlurRadius: 16.0,
        ShadowOffsetY: 5.0
    );
}

[GenerateSerializer]
public sealed record SurfacePlacement(
    [property: Id(0)] string Region,
    [property: Id(1)] bool Pinned,
    [property: Id(2)] int Order);

[GenerateSerializer]
public sealed record UiSurface(
    [property: Id(0)] string SurfaceId,
    [property: Id(1)] NeuronId Emitter,
    [property: Id(2)] UiWidget Root,
    [property: Id(3)] SurfacePlacement? Placement = null) : Synapse;

public static class WidgetTree
{
    public static string Render(UiWidget widget, int depth = 0, bool highlight = false)
    {
        var pad = new string(' ', depth * 2);
        string Color(string name) => highlight ? name switch
        {
            "Text" => "\u001b[36m",
            "Button" => "\u001b[32m",
            "Card" => "\u001b[33m",
            "Column" => "\u001b[35m",
            "Row" => "\u001b[34m",
            "Markdown" => "\u001b[31m",
            "MainPane" => "\u001b[35m",
            _ => "\u001b[0m"
        } : "";
        string Reset = highlight ? "\u001b[0m" : "";
        string Type(string n) => highlight ? $"{Color(n)}[{n}]{Reset}" : n;
        return widget switch
        {
            Text t => $"{pad}{Type("Text")}: {t.Value}",
            Button b => $"{pad}{Type("Button")}: {b.Label}",
            Card c => $"{pad}{Type("Card")} \"{c.Title}\"\n{Render(c.Body, depth + 1, highlight)}",
            Column col => $"{pad}{Type("Column")}\n{string.Join("\n", col.Children.Where(ch => ch is not null).Select(ch => Render(ch!, depth + 1, highlight)))}",
            Row row => $"{pad}{Type("Row")}\n{string.Join("\n", row.Children.Where(ch => ch is not null).Select(ch => Render(ch!, depth + 1, highlight)))}",
            Markdown m => $"{pad}{Type("Markdown")}: {(m.Value ?? string.Empty).ReplaceLineEndings(" ⏎ ")}",
            MainPane mp => $"{pad}{Type("MainPane")}\n{Render(mp.Content, depth + 1, highlight)}",
            Graph3D g => $"{pad}{Type("Graph3D")} nodes:{g.Nodes?.Length ?? 0} edges:{g.Edges?.Length ?? 0}",
            Divider => $"{pad}{Type("Divider")}",
            Icon i => $"{pad}{Type("Icon")}: {i.Name}",
            TextField tf => $"{pad}{Type("TextField")} \"{tf.Label}\": {tf.Value}",
            Progress p => $"{pad}{Type("Progress")} {p.Value} ({(p.Label ?? "")})",
            Toggle tg => $"{pad}{Type("Toggle")} \"{tg.Label}\": {tg.Value}",
            ImageWidget img => $"{pad}{Type("ImageWidget")}: {img.Url}",
            Container ctr => $"{pad}{Type("Container")} padding:{ctr.Padding} deco:{ctr.Decoration ?? "none"}\n{Render(ctr.Child, depth + 1, highlight)}",
            WindowFrame wf => $"{pad}{Type("WindowFrame")} \"{wf.Title}\" ({wf.WindowId}) state:{wf.State}\n{Render(wf.Content, depth + 1, highlight)}",
            BarChart bc => $"{pad}{Type("BarChart")} \"{bc.Title}\" bars:{bc.Bars?.Length ?? 0}",
            _ => $"{pad}?"
        };
    }
}