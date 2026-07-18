using Ino.Core;

namespace Ino.Core.Hosting;

public interface INeuronDefinition
{
    NeuronId Id { get; }
    string DisplayName { get; }
    string Description { get; }
    Type CanonicalSynapseType { get; }
    // Concrete string[] — avoids the <>z__ReadOnlyArray<T> cross-silo codec
    // trap documented in CLAUDE.md when this list appears on an
    // Orleans-serialized record field.
    string[] PromptExamples { get; }

    /// <summary>
    /// Optional grain interface (must extend <see cref="INeuronPlan"/>) that
    /// implements a multi-hop BFS for this neuron. When set, Cortex resolves
    /// the plan grain and calls <see cref="INeuronPlan.ExecuteAsync"/>
    /// instead of single-firing <see cref="CanonicalSynapseType"/>. When null,
    /// Cortex's legacy single-hop fire path runs — preserved so existing Travel
    /// /Taxi neurons keep working without migration.
    /// </summary>
    Type? PlanType => null;
}
