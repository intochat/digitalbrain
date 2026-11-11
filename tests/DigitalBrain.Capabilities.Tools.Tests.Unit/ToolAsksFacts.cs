using DigitalBrain.Units;
using System.Text.Json;

namespace DigitalBrain.Capabilities.Tools.Tests.Unit;

public sealed class ToolAsksFacts
{
    [Fact]
    public void RunIsNotRepeatableUnlessSaid()
    {
        var tools = new Asks<ITool>("craft");
        var once = tools.Run("resize", JsonElement.Parse("""{"w":64}"""), key: "f1");
        var again = tools.Run("resize", JsonElement.Parse("""{"w":64}"""), key: "f2", repeatable: true);

        Assert.Equal(("craft", "Run", "f1", false), (once.Slot, once.Operation, once.Key, once.Repeatable));
        Assert.Equal("""{"tool":"resize","args":{"w":64}}""", once.Arguments.GetRawText());
        Assert.True(again.Repeatable);
    }
}
