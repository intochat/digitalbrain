namespace DigitalBrain;

public abstract record Synapse;

// A question carries its reply type: compile-time ask/answer pairing, edge AskAsync
// inference, and the boot answerer-cardinality check all hang on this one declaration.
public abstract record Synapse<TReply> : Synapse
    where TReply : Synapse;
