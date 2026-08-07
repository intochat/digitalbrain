namespace DigitalBrain.Product.Time;

public interface IProposalDeadlineScheduler
{
    /// <summary>
    /// Schedules or reconciles the immutable deadline. Implementations must make
    /// repeated calls with the same <see cref="ProposalDeadline.ProposalId"/> and
    /// <see cref="ProposalDeadline.ProposalFingerprint"/> converge on one logical
    /// external deadline, including when a host record fails after this call
    /// succeeds and the pending input is redelivered. A same identity with a
    /// different due time is an integrity failure, not a replacement schedule.
    /// </summary>
    Task ScheduleAsync(ProposalDeadline deadline, CancellationToken cancellationToken);
}
