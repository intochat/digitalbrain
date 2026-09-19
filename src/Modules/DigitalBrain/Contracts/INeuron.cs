namespace DigitalBrain.Contracts;

public interface INeuron : IGrainWithStringKey
{
    Task Ping();
    Task Sleep();
    Task Broadcast(Signal signal);
    Task Bind(INeuron listener);
    Task Receive(Signal signal);
    Task Watch(INeuronObserver observer);
    Task Unwatch(INeuronObserver observer);
    Task<IReadOnlyList<Signal>> Inbox();
}

public interface INeuronObserver : IGrainObserver
{
    Task Hear(Signal signal);
}

public interface ISynapse
{
    IReadOnlyList<Signal> GetSignals();
}
