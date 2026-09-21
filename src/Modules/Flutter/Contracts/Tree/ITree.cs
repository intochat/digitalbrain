using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Tree;

[Alias("tree"), Orleans.Metadata.DefaultGrainType(UIVocabulary.TreeType)]
public interface ITree : INeuron
{
    Task Set(IReadOnlyList<TreeNode> nodes);
    Task Select(string id);
    [ReadOnly, Alias("read")] Task<TreeState> Read();
}

[GenerateSerializer, Alias("ui.tree-node")]
public sealed record TreeNode(
    [property: Id(0)] string Id,
    [property: Id(1)] string? ParentId,
    [property: Id(2)] string Label);

[GenerateSerializer, Alias("ui.tree-state")]
public sealed class TreeState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public List<TreeNode> Nodes { get; set; } = [];
    [Id(3)] public string SelectedId { get; set; } = "";
}