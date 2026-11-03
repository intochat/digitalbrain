namespace DigitalBrain.Apps.Hosting;

public interface IAppHost
{
    Task Assign(ProgramAssignment assignment, CancellationToken ct);
    Task Retire(string owner, CancellationToken ct);
    Task<BehaviorView[]> Describe(string owner, CancellationToken ct);
}
