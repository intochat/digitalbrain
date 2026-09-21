using DigitalBrain.Flutter.ImageCanvas;
using IntoChat.Apps;
using Xunit;

namespace IntoChat.Tests.E2E.LocalApps;

public sealed class ImageEditFacts
{
    [Fact]
    public void CroppingDoesNotChangeStrokeCoordinatesAndResetClearsBoth()
    {
        var stroke = new PenStroke(0xffff0000, 3, [new(20, 20), new(30, 30)]);
        var drawn = ImageEdits.Apply(new(), new("stroke", Stroke: stroke), 120, 80);
        var cropped = ImageEdits.Apply(drawn, new("crop", Crop: new(10, 10, 60, 40)), 120, 80);
        Assert.Equal(stroke, Assert.Single(cropped.Strokes));
        Assert.Equal(new CropRect(10, 10, 60, 40), cropped.Crop);
        Assert.Throws<ArgumentException>(() => ImageEdits.Apply(cropped, new("crop", Crop: new(110, 10, 60, 40)), 120, 80));
        var reset = ImageEdits.Apply(cropped, new("reset"), 120, 80);
        Assert.Null(reset.Crop);
        Assert.Empty(reset.Strokes);
    }
}
