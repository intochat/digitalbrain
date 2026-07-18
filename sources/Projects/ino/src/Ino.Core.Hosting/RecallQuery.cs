using Ino.Core;

namespace Ino.Core.Hosting;

/// <summary>
/// Predicate-shape passed to <see cref="ITraversalEngine.VisitAsync{TEvent}"/>.
/// Cortex's BFS picks the next hop by reading a neuron's journal and applying
/// these filters; the seven traversal primitives in the design doc (frequency,
/// negative-search, temporal-window, recurrence, cloning, co-occurrence,
/// content-scan) are all expressible as <see cref="RecallQuery{TEvent}"/>
/// instances.
///
/// Predicates are NOT serializable — they run in-process inside
/// <see cref="TraversalEngine"/> after the cross-silo grain call has returned
/// the raw journal. <see cref="LastN"/> bounds the cross-silo payload size; the
/// other filters narrow the result set locally. For very long journals this is
/// wasteful; pushing predicates server-side is a post-v0.1 optimization.
/// </summary>
public sealed record RecallQuery<TEvent> where TEvent : class, ISynapse
{
    public int? LastN { get; init; }
    public DateTimeOffset? Since { get; init; }
    public DateTimeOffset? Until { get; init; }
    public Func<TEvent, bool>? Where { get; init; }
    public Func<EventEnvelope<TEvent>, bool>? WhereEnvelope { get; init; }

    public static RecallQuery<TEvent> All { get; } = new();

    public static RecallQuery<TEvent> Last(int n) => new() { LastN = n };

    public static RecallQuery<TEvent> Recent(TimeSpan window) =>
        new() { Since = DateTimeOffset.UtcNow - window };

    public static RecallQuery<TEvent> Between(DateTimeOffset since, DateTimeOffset until) =>
        new() { Since = since, Until = until };
}
