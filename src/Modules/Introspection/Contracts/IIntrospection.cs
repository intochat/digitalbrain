using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[Alias("introspection")]
public partial interface IIntrospection :
    INeuron,
    IHandle<TallyJournalRequest>,
    IHandle<ReadJournalRequest>,
    IHandle<ReadTopologyRequest>;
