using DigitalBrain.Contracts;

namespace DigitalBrain.Time;

/// <summary>One owner's registration of a schedule with the time neuron.</summary>
/// <param name="Owner">Who registered the schedule.</param>
/// <param name="Schedule">The schedule text, for example <c>07:00</c>.</param>
public sealed record ScheduleRegistration(string Owner, string Schedule);

/// <summary>The time neuron of a brain: turns registered schedules into durable <see cref="Tick"/> signals.</summary>
[NeuronAlias("time")]
public interface ITime
{
    /// <summary>Registers <paramref name="schedule"/> for <paramref name="owner"/>; registering the same pair again does nothing.</summary>
    /// <param name="owner">Who is registering.</param>
    /// <param name="schedule">The schedule text.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentException">The schedule text is not a schedule.</exception>
    Task Register(string owner, string schedule, CancellationToken ct);

    /// <summary>Removes every registration of <paramref name="owner"/>; a schedule no owner needs any more stops firing.</summary>
    /// <param name="owner">Whose registrations to remove.</param>
    /// <param name="ct">Cancellation.</param>
    Task Unregister(string owner, CancellationToken ct);

    /// <summary>Every registration.</summary>
    /// <param name="ct">Cancellation.</param>
    [ReadOnlyCall]
    Task<IReadOnlyList<ScheduleRegistration>> Registered(CancellationToken ct);
}
