namespace DigitalBrain.Flutter;

internal sealed record VideoLoad(string Url, double Duration);

internal sealed record VideoSeek(double Seconds);
