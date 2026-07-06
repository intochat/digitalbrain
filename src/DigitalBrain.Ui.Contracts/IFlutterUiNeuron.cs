namespace DigitalBrain.Ui.Contracts;

using DigitalBrain.Core;

[Alias("DigitalBrain.Ui.Contracts.IFlutterUiNeuron")]
public interface IFlutterUiNeuron : IChannelNeuron, IHandle<UiSurface>
{
}
