namespace DigitalBrain.SDK.Canvas;

[GenerateSerializer]
public sealed record Atom3D(
    [property: Id(1)] string Symbol,
    [property: Id(2)] double X,
    [property: Id(3)] double Y,
    [property: Id(4)] double Z,
    [property: Id(5)] string Color,
    [property: Id(6)] double Radius
);

[GenerateSerializer]
public sealed record Bond3D(
    [property: Id(1)] int From,
    [property: Id(2)] int To
);
