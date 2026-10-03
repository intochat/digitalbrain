using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Expander;
using DigitalBrain.Flutter.Expander.Signals;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Expander;

[Collection(FlutterHostCollection.Name)]
public sealed class ExpanderHttpFacts(FlutterHostFixture host)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task ToggleHttpFlipsNeuronAndGet()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = host.Brain;
        var ws = host.Workspace();
        var expander = brain.Get<IExpander>(UiScope.Key(BrainScope.Create("owner", ws).Id, "more"));
        await using var changed = await brain.Observe<ExpanderChanged>(expander, ct);

        using var collapsed = await brain.HttpClient.PostAsJsonAsync($"/brains/{ws}/ui/expanders/more",
            new { header = "More", expanded = false, children = Array.Empty<object>() }, ct);
        Assert.Equal(HttpStatusCode.Accepted, collapsed.StatusCode);
        Assert.False((await changed.NextAsync(ct: ct)).Expanded);
        Assert.False((await expander.Read()).Expanded);
        Assert.False((await brain.HttpClient.GetFromJsonAsync<ExpanderState>($"/brains/{ws}/ui/expanders/more", Json, ct))!.Expanded);

        using var toggle = await brain.HttpClient.PostAsync($"/brains/{ws}/ui/expanders/more/toggle", null, ct);
        Assert.Equal(HttpStatusCode.Accepted, toggle.StatusCode);
        Assert.True((await changed.NextAsync(ct: ct)).Expanded);
        Assert.True((await expander.Read()).Expanded);
        Assert.True((await brain.HttpClient.GetFromJsonAsync<ExpanderState>($"/brains/{ws}/ui/expanders/more", Json, ct))!.Expanded);
    }
}
