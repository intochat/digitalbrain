using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.SmartPrompt;

[Alias("behavior-definition")]
public interface IBehaviorDefinition : IEntity<BehaviorDefinitionState>
{
    [Alias(nameof(Save))]
    Task<BehaviorCompilation> Save(string source);

    [Alias(nameof(Test))]
    Task<BehaviorTestReport> Test();

    [Alias(nameof(Activate))]
    Task Activate();

    [Alias(nameof(Disable))]
    Task Disable();
}

[Alias("behavior-catalog")]
public interface IBehaviorCatalog : IEntity<BehaviorCatalogState>
{
    [Alias(nameof(Add))]
    Task Add(string name);
}

[Alias("behavior-ingress")]
public interface IBehaviorIngress : IGrainWithStringKey
{
    [Alias(nameof(Publish))]
    Task Publish(BehaviorEvent behaviorEvent);
}

[Alias("behavior-trigger-directory")]
public interface IBehaviorTriggerDirectory : IGrainWithStringKey
{
    [Alias(nameof(Subscribe))]
    Task Subscribe(BehaviorSubscription subscription);

    [Alias(nameof(Unsubscribe))]
    Task Unsubscribe(BehaviorSubscription subscription);

    [Alias(nameof(Publish))]
    Task Publish(BehaviorEvent behaviorEvent);

    [Alias(nameof(ReadStats))]
    Task<BehaviorDirectoryStats> ReadStats();
}

[Alias("behavior-subscription-partition")]
public interface IBehaviorSubscriptionPartition : IGrainWithStringKey
{
    [Alias(nameof(Subscribe))]
    Task<bool> Subscribe(BehaviorSubscription subscription);

    [Alias(nameof(Unsubscribe))]
    Task<bool> Unsubscribe(BehaviorSubscription subscription);

    [Alias(nameof(Publish))]
    Task Publish(BehaviorEvent behaviorEvent);
}

[Alias("behavior-runner")]
public interface IBehaviorRunner : IGrainWithStringKey
{
    [Alias(nameof(Deliver))]
    Task Deliver(BehaviorSubscription subscription, BehaviorEvent behaviorEvent);
}
