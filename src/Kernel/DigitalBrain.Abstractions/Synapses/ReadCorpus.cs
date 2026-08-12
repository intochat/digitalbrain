namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.read-corpus")]
public sealed record ReadCorpus(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long AfterSequence = 0,
    [property: Id(2)] int Limit = 100) : RequestSynapse<CorpusPage>;

