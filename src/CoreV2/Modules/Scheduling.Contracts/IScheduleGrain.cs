namespace Brain.Modules.Scheduling.Contracts;

public interface IScheduleGrain : IGrainWithStringKey
{
    Task<ScheduleSnapshot> ScheduleAsync(ScheduleRequest request);

    Task<ScheduleSnapshot> ReadAsync();

    Task<ScheduleSnapshot> CancelAsync(string idempotencyKey);
}
