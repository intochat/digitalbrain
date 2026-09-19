namespace DigitalBrain.Contracts;

public interface INeuron : IGrainWithStringKey
{
    Task Ping();
    Task Sleep();
}

public interface ISynapse
{
    IReadOnlyList<Signal> GetSignals();
}
