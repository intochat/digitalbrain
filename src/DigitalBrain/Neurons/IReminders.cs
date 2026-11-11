namespace DigitalBrain.Neurons;

/// <summary>Durable reminders that reactivate the neuron. Ticks run as turns.</summary>
public interface IReminders
{
    /// <summary>Starts or replaces the named reminder.</summary>
    Task Start(string name, TimeSpan due, TimeSpan period);

    /// <summary>Stops the named reminder.</summary>
    Task Stop(string name);

    /// <summary>The names of the registered reminders.</summary>
    Task<IReadOnlyList<string>> Names();
}
