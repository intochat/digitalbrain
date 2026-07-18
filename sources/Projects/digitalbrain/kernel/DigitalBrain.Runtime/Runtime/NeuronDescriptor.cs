namespace DigitalBrain.Runtime.Runtime;

// FQN = the bus key the Navigator routes by; PortName = the InoLang local
// trigger key the Interpreter dispatches on. Carried as a pair because the
// linker is the only place both are known together, and downstream consumers
// (Navigator at E-RUN#36, Catalog at E-RUN#34) each need a different face.
[GenerateSerializer]
public sealed record IncomingPort(
    [property: Id(0)] string Fqn,
    [property: Id(1)] string PortName);

// Outgoing carries *emitter* port FQNs (`using !x = signal(T)`); subscriber
// `on signal(T):` triggers are a separate concept and will land as an
// ABI-additive field at E-RUN#37 (Cortex fan-out).
[GenerateSerializer]
public sealed record NeuronDescriptor(
    [property: Id(0)] string Fqn,
    [property: Id(1)] IReadOnlyList<IncomingPort> Incoming,
    [property: Id(2)] IReadOnlyList<string> Outgoing,
    [property: Id(3)] string InoLangSource,
    // Optional for persisted definitions: each silo maps this stable key to
    // its local file and reads the source only when the plan is first needed.
    [property: Id(4)] string? InoLangSourceCacheKey = null,
    [property: Id(5)] string? InoLangSourceSha256 = null);
