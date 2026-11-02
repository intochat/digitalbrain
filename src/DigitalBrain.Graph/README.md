# DigitalBrain.Graph

One graph per brain, derived from what already exists: neuron instances and their contracts (`IBrainObserver`), synapses and the signals that flow along them, and whatever a host contributes through `IGraphSource` (app operations and reactions, memory facts, grants). Nothing is stored twice: the `brain.graph` neuron keeps a revision and rebuilds the snapshot from the sources.

- `IBrainGraph` — `Snapshot(level, focus)`, `Analysis()`, `Neighbourhood(id, hops)`, `Paths(from, to)`, `Rebuild()`.
- `GraphSnapshot` — compressed sparse row arrays; node and edge kinds; groups for the brain level.
- Algorithms — weak components, Tarjan SCC, PageRank (personalized with a teleport vector), HITS, BFS paths and k-hop neighbourhoods, label-propagation communities.
- Levels — `Brain` (one node per module group), `Cluster` (a group's neurons with each strongly connected set condensed into one node), `Neurons` (a node, what it owns and its neighbours).

Compose it as a module (`GraphModule`) and register an `IBrainObserver` in the host services; sources are optional.
