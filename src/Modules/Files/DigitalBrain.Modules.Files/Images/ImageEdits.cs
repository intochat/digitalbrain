using DigitalBrain.Flutter.ImageCanvas;

namespace DigitalBrain.Files;

internal static class ImageEdits
{
    public static ImageRecipe Apply(ImageRecipe current, ImageEditCommand command, int width, int height)
    {
        var next = command.Kind switch
        {
            "stroke" when command.Stroke is not null => current with { Strokes = [.. current.Strokes, command.Stroke] },
            "crop" when command.Crop is not null => current with { Crop = command.Crop },
            "reset" => new ImageRecipe(),
            "replace" when command.Recipe is not null => command.Recipe,
            _ => throw new ArgumentException("Unknown image edit command.")
        };
        next.Validate(width, height);
        return next;
    }
}