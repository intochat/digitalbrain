using DigitalBrain.Os.UI;

namespace DigitalBrain.Os;

public interface ISurfaceFanout
{
    void Publish(UiSurface surface);
}

public static class SurfaceFanout
{
    public static ISurfaceFanout Instance { get; set; } = new Noop();

    private sealed class Noop : ISurfaceFanout
    {
        public void Publish(UiSurface surface) { }
    }
}