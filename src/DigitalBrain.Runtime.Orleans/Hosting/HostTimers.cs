using DigitalBrain.Spi;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>What the host grain gives its context for ticks: activation timers and durable reminders. Every tick is a turn on the grain.</summary>
internal interface IHostTicks
{
    void StartTimer(string name, TimeSpan due, TimeSpan? period, bool keepAlive);

    void StopTimer(string name);

    Task StartReminder(string name, TimeSpan due, TimeSpan period);

    Task StopReminder(string name);

    Task<IReadOnlyList<string>> ReminderNames();

    /// <summary>Asks the host grain to deactivate once the current turn ends (runtime services, 04-13).</summary>
    void Deactivate();
}

/// <summary>
/// <see cref="ITimers"/> and <see cref="IReminders"/> of a hosted neuron. Timer and reminder ticks both reach
/// <see cref="DigitalBrain.Neuron.OnReminder"/> as host grain turns, as on TestOS (04-08).
/// </summary>
internal sealed class HostTimers(IHostTicks ticks, NeuronRegistration registration) : ITimers, IReminders
{
    public void Start(string name, TimeSpan due, TimeSpan? period) => Start(name, due, period, keepAlive: false);

    public void Start(string name, TimeSpan due, TimeSpan? period, bool keepAlive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ticks.StartTimer(name, due, period, keepAlive);
    }

    public void Stop(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ticks.StopTimer(name);
    }

    Task IReminders.Start(string name, TimeSpan due, TimeSpan period)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RefuseStateless();
        return ticks.StartReminder(name, due, period);
    }

    Task IReminders.Stop(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RefuseStateless();
        return ticks.StopReminder(name);
    }

    public Task<IReadOnlyList<string>> Names()
    {
        RefuseStateless();
        return ticks.ReminderNames();
    }

    private void RefuseStateless()
    {
        if (registration.Policy == HostPolicy.Stateless)
        {
            throw new InvalidOperationException($"{registration.Class.FullName} is a [StatelessNeuron] class: it has many activations and cannot hold a reminder.");
        }
    }
}
