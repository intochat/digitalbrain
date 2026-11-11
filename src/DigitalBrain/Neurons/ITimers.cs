namespace DigitalBrain.Neurons;

/// <summary>Timers that live with the activation. Ticks run as turns.</summary>
public interface ITimers
{
    /// <summary>Starts or replaces the named timer. A null period fires once.</summary>
    void Start(string name, TimeSpan due, TimeSpan? period);

    /// <summary>
    /// Starts or replaces the named timer. A null period fires once. When <paramref name="keepAlive"/> is true the host keeps
    /// the neuron's activation alive until a one-shot timer fires or the timer is stopped; false is the three-argument behaviour.
    /// </summary>
    void Start(string name, TimeSpan due, TimeSpan? period, bool keepAlive);

    /// <summary>Stops the named timer.</summary>
    void Stop(string name);
}
