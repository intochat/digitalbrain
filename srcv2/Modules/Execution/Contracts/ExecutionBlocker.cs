using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.blocker")]
public abstract record ExecutionBlocker
{
    protected ExecutionBlocker(BlockerId id)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An execution blocker id cannot be empty.", nameof(id));
        }

        Id = id;
    }

    [Id(0)]
    public BlockerId Id { get; }
}

