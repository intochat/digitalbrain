namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.member-query")]
public sealed record MemberQuery([property: Id(0)] string SymbolId);