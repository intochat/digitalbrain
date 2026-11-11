using DigitalBrain.Neurons;

namespace DigitalBrain.Graph;

/// <summary>What a node of the brain graph is.</summary>
public enum GraphNodeKind : byte
{
    /// <summary>The brain itself.</summary>
    Brain,
    /// <summary>A neuron instance.</summary>
    Neuron,
    /// <summary>An app neuron.</summary>
    App,
    /// <summary>An app operation (<c>[On]</c>).</summary>
    Operation,
    /// <summary>An app reaction (<c>[On("source")]</c>).</summary>
    Reaction,
    /// <summary>A neuron contract.</summary>
    Contract,
    /// <summary>A signal type.</summary>
    Signal,
    /// <summary>A connection neuron.</summary>
    Connection,
    /// <summary>A memory fact.</summary>
    Fact,
    /// <summary>A memory entity.</summary>
    Entity,
    /// <summary>A node of another brain: only its key prefix is known.</summary>
    External,
    /// <summary>A durable task in the work graph.</summary>
    Task,
    /// <summary>One step in a task's pipeline.</summary>
    Step,
    /// <summary>An active or completed agent or job run.</summary>
    Run,
}

/// <summary>What an edge of the brain graph means, from its source to its target.</summary>
public enum GraphEdgeKind : byte
{
    /// <summary>Brain owns neuron; app owns operation or reaction.</summary>
    Owns,
    /// <summary>Neuron implements contract.</summary>
    Implements,
    /// <summary>Neuron or app calls contract or neuron.</summary>
    Calls,
    /// <summary>Subscriber holds a synapse on publisher.</summary>
    Subscribes,
    /// <summary>Publisher publishes signal type.</summary>
    Publishes,
    /// <summary>Grantee may call target.</summary>
    Grants,
    /// <summary>Fact or entity relates to fact or entity.</summary>
    Relates,
    /// <summary>Fact mentions neuron, app or connection.</summary>
    Mentions,
    /// <summary>Parent task owns a child subtask.</summary>
    Subtask,
    /// <summary>Source task or step must complete before target can run.</summary>
    Blocks,
    /// <summary>Run is actively working on target task, step or app.</summary>
    WorksOn,
    /// <summary>Task step executes a capability contract.</summary>
    Executes,
    /// <summary>Task or run produced a target app, file or fact.</summary>
    Produced,
}

/// <summary>How much of the graph a view shows.</summary>
public enum GraphLevel : byte
{
    /// <summary>One node per group (module prefix, memory, external).</summary>
    Brain,
    /// <summary>The neurons of one group; each strongly connected set is one node.</summary>
    Cluster,
    /// <summary>One node, what it owns and its neighbours.</summary>
    Neurons,
}

/// <summary>
/// The graph in compressed sparse row form. Node <c>i</c> has the out-edges <c>Col[RowStart[i]..RowStart[i+1])</c>
/// with kinds <c>EdgeKinds</c> and weights <c>Weights</c> at the same positions. Immutable once built.
/// </summary>
/// <param name="Revision">The projection revision this snapshot was built at.</param>
/// <param name="Ids">Node ids, in the brain's key grammar.</param>
/// <param name="Kinds">Node kinds, by node index.</param>
/// <param name="Labels">Short labels, by node index.</param>
/// <param name="Group">The index into <paramref name="Groups"/> of each node, or -1.</param>
/// <param name="RowStart">Edge offsets, one per node plus one.</param>
/// <param name="Col">Edge targets, by edge index.</param>
/// <param name="EdgeKinds">Edge kinds, by edge index.</param>
/// <param name="Weights">Edge weights, by edge index.</param>
/// <param name="Groups">The groups nodes belong to.</param>
public sealed record GraphSnapshot(
    long Revision,
    string[] Ids,
    GraphNodeKind[] Kinds,
    string[] Labels,
    int[] Group,
    int[] RowStart,
    int[] Col,
    GraphEdgeKind[] EdgeKinds,
    float[] Weights,
    GraphGroup[] Groups)
{
    /// <summary>How many nodes the snapshot has.</summary>
    public int NodeCount => Ids.Length;

    /// <summary>How many edges the snapshot has.</summary>
    public int EdgeCount => Col.Length;

    /// <summary>The index of <paramref name="id"/>, or -1.</summary>
    public int IndexOf(string id) => Array.IndexOf(Ids, id);
}

/// <summary>A group of nodes shown as one node at the brain level.</summary>
/// <param name="Id">The group id.</param>
/// <param name="Label">The group label.</param>
/// <param name="Kind">The kind of the nodes it groups.</param>
/// <param name="Size">How many nodes it holds.</param>
/// <param name="Cycle">Whether the group is a condensed strongly connected set.</param>
public sealed record GraphGroup(string Id, string Label, GraphNodeKind Kind, int Size, bool Cycle);

/// <summary>What the algorithms say about a snapshot, by node index.</summary>
/// <param name="Revision">The snapshot revision the analysis belongs to.</param>
/// <param name="Component">Weakly connected component per node.</param>
/// <param name="Scc">Strongly connected component per node.</param>
/// <param name="Community">Label-propagation community per node.</param>
/// <param name="PageRank">PageRank per node.</param>
/// <param name="Hub">HITS hub score per node.</param>
/// <param name="Authority">HITS authority score per node.</param>
/// <param name="Findings">What the rules found.</param>
public sealed record GraphAnalysis(
    long Revision,
    int[] Component,
    int[] Scc,
    int[] Community,
    float[] PageRank,
    float[] Hub,
    float[] Authority,
    GraphFinding[] Findings);

/// <summary>One thing a rule found.</summary>
/// <param name="Rule">The rule: <c>call-cycle</c>, <c>dead</c>, <c>orphan-subscription</c> or <c>hub</c>.</param>
/// <param name="Nodes">The node ids involved.</param>
/// <param name="Message">What is wrong, for a person.</param>
public sealed record GraphFinding(string Rule, string[] Nodes, string Message);

/// <summary>A path through the graph.</summary>
/// <param name="Nodes">The node ids, from the start to the end.</param>
/// <param name="Edges">The edge kinds between consecutive nodes.</param>
public sealed record GraphPath(string[] Nodes, GraphEdgeKind[] Edges);

/// <summary>The brain graph was rebuilt and differs from the previous revision.</summary>
/// <param name="Revision">The new revision.</param>
/// <param name="Nodes">How many nodes it has.</param>
/// <param name="Edges">How many edges it has.</param>
[Signal("brain.graph.changed")]
public sealed record GraphChanged(long Revision, int Nodes, int Edges) : Signal;

/// <summary>The persisted state of the <c>brain.graph</c> neuron: a revision and a content hash. The snapshot is rebuilt from the sources.</summary>
/// <param name="Revision">The current revision, 0 before the first build.</param>
/// <param name="Hash">A hash of the node and edge lists of the current revision.</param>
public sealed record BrainGraphState(long Revision, long Hash)
{
    /// <summary>The empty state.</summary>
    public BrainGraphState() : this(0, 0) { }
}
