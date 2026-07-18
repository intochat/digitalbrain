using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Os.UI;
using Hex1b;
using Hex1b.Widgets;

namespace DigitalBrain.Clients.ConsoleClient;

public static class SurfaceRenderer
{
    public static Hex1bWidget Render<TParent>(WidgetContext<TParent> ctx, UiWidget widget, Func<Synapse, Task> fire) where TParent : Hex1bWidget
        => widget switch
        {
            Text t => ctx.Text(t.Value),
            Button b when b.OnTap is { } onTap => ctx.Button(b.Label).OnClick(_ => fire(onTap)),
            Button b => ctx.Text($"[{b.Label}]"),
            Card c => ctx.VStack(v => [v.Text($"── {c.Title} ──"), Render(v, c.Body, fire)]),
            Column col => ctx.VStack(v => col.Children.Where(ch => ch is not null).Select(ch => Render(v, ch!, fire)).ToArray()),
            Row row => ctx.HStack(h => row.Children.Where(ch => ch is not null).Select(ch => Render(h, ch!, fire)).ToArray()),
            Markdown m => ctx.Markdown(m.Value),
            Hyperlink h => ctx.Text($"🔗 {h.Label}"),
            MainPane mp => Render(ctx, mp.Content, fire),
            _ => ctx.Text("?")
        };
}
