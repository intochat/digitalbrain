namespace DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("tasks.blocker")]
public abstract record TaskBlocker
{
    protected TaskBlocker(BlockerId id)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("A task blocker id cannot be empty.", nameof(id));
        }

        Id = id;
    }

    [Id(0)]
    public BlockerId Id { get; }
}

[GenerateSerializer]
[Alias("tasks.input-required")]
public sealed record InputRequired(BlockerId Id) : TaskBlocker(Id);

[GenerateSerializer]
[Alias("tasks.approval-required")]
public sealed record ApprovalRequired(BlockerId Id) : TaskBlocker(Id);

[GenerateSerializer]
[Alias("tasks.dependency-pending")]
public sealed record DependencyPending(BlockerId Id) : TaskBlocker(Id);

[GenerateSerializer]
[Alias("tasks.retry-scheduled")]
public sealed record RetryScheduled(BlockerId Id) : TaskBlocker(Id);

[GenerateSerializer]
[Alias("tasks.outcome-uncertain")]
public sealed record OutcomeUncertain(BlockerId Id) : TaskBlocker(Id);
