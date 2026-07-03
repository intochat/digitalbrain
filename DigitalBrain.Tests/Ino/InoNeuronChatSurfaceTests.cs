using DigitalBrain.Core;
using DigitalBrain.Core.Ui;
using DigitalBrain.Core.UiKit;
using DigitalBrain.TestKit;
using DigitalBrain.UiKit;

namespace DigitalBrain.Tests.Ino;

public class InoNeuronChatSurfaceTests : NeuronTestBase
{
    [Fact]
    public async Task InoRequest_Emits_Assistant_Reply_Surface_To_FlutterUi()
    {
        var ino = Grain<IInoNeuron>("ino-main");
        await ino.FireAsync(new InoRequest("hello, what can you do?", "session-1"));

        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var timeline = await flutter.GetIncomingTimelineAsync();

        var surface = Assert.Single(timeline.OfType<UiSurface>());
        Assert.Equal(UiSurface.WidgetTreeKind, surface.Kind);
        Assert.Equal("session-1", surface.Props["sessionId"]);
        Assert.Equal("assistant", surface.Props["role"]);

        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Equal(UiKitVocabulary.Text, tree.Type);
        Assert.Contains("no-llm", tree.Props["text"]!.ToString());
    }
}
