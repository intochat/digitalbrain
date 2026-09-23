using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Slider;

namespace IntoChat.Tests.E2E.Security;

public sealed class CrossWorkspaceFacts
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact(Timeout = 180_000)]
    public async Task SecondWorkspaceCannotReadTheFirstsValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);

        var slider = brain.Get<ISlider>(UiScope.Key("workspace-a", "volume"));
        await slider.Configure(0, 10, 1);
        await slider.SetValue(7);

        var owner = await brain.HttpClient.GetFromJsonAsync<SliderState>("/workspaces/workspace-a/ui/sliders/volume", Json, ct);
        Assert.Equal(7, owner!.Value);

        var foreign = await brain.HttpClient.GetFromJsonAsync<SliderState>("/workspaces/workspace-b/ui/sliders/volume", Json, ct);
        Assert.Equal(0, foreign!.Value);
    }

    [Fact(Timeout = 180_000)]
    public async Task UnscopedValueRoutesAreGone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);

        using var slider = await brain.HttpClient.GetAsync("/ui/sliders/volume", ct);
        using var textfield = await brain.HttpClient.GetAsync("/ui/textfields/name", ct);
        Assert.Equal(HttpStatusCode.NotFound, slider.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, textfield.StatusCode);
    }
}