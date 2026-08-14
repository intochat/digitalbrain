namespace Brain.Modules.Behavior.Contracts;

public interface IBehaviorGrain : IGrainWithStringKey
{
    Task<BehaviorSnapshot> PublishAsync(PublishBehaviorRequest request);

    Task<BehaviorSnapshot> ActivateAsync(int revision, string idempotencyKey);

    Task<BehaviorSnapshot> RunAsync(RunBehaviorRequest request);

    Task<BehaviorSnapshot> ReadAsync();
}
