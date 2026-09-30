namespace DigitalBrain.Flutter.ImageCanvas;

[GenerateSerializer, Alias("ui.image-point")]
public sealed record ImagePoint([property: Id(0)] double X, [property: Id(1)] double Y);
[GenerateSerializer, Alias("ui.crop-rect")]
public sealed record CropRect([property: Id(0)] int X, [property: Id(1)] int Y, [property: Id(2)] int Width, [property: Id(3)] int Height);
[GenerateSerializer, Alias("ui.pen-stroke")]
public sealed record PenStroke([property: Id(0)] uint ColorArgb, [property: Id(1)] double Width, [property: Id(2)] IReadOnlyList<ImagePoint> Points);
[GenerateSerializer, Alias("ui.image-recipe")]
public sealed record ImageRecipe
{
    [Id(0)] public CropRect? Crop { get; init; }
    [Id(1)] public IReadOnlyList<PenStroke> Strokes { get; init; } = [];
    public void Validate(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 40_000_000) { throw new ArgumentException("Image dimensions exceed the supported limit."); }
        if (Crop is { } c && (c.X < 0 || c.Y < 0 || c.Width <= 0 || c.Height <= 0 || (long)c.X + c.Width > width || (long)c.Y + c.Height > height)) { throw new ArgumentException("Crop must be inside the image."); }
        if (Strokes.Count > 10000 || Strokes.Sum(s => (long)s.Points.Count) > 500000) { throw new ArgumentException("Drawing limit reached."); }
        foreach (var s in Strokes)
        {
            if (!double.IsFinite(s.Width) || s.Width <= 0 || s.Width > 500 || s.Points.Count == 0) { throw new ArgumentException("Invalid pen stroke."); }
            if (s.Points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X < 0 || p.Y < 0 || p.X > width || p.Y > height)) { throw new ArgumentException("Pen points must be inside the image."); }
        }
    }
}
