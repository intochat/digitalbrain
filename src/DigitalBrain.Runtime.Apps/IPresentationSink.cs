using DigitalBrain.UI;

namespace DigitalBrain.Runtime.Apps;

public interface IPresentationSink
{
    Task Present(string owner, View? side, View? main, CancellationToken ct);
}
