using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Form.Signals;

[GenerateSerializer, Alias("ui.form-changed")]
public sealed record FormChanged(
    [property: Id(0)] string FormId,
    [property: Id(1)] int Revision) : Signal;
