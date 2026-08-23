using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.Execution;

[Alias("db.execution-context")]
public interface IExecutionContext : IEntity<ExecutionContextState>
{
    [Alias(nameof(Query))]
    Task<ContextEntry?> Query(ContextQuery query);
}
