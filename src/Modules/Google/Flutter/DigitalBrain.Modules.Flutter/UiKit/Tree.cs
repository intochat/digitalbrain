using DigitalBrain.Flutter.Tree;

namespace DigitalBrain.Flutter;

internal sealed record TreeSet(IReadOnlyList<TreeNode> Nodes);

internal sealed record TreeSelect(string Id);
