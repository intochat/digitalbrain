using System.Collections.Generic;

namespace DigitalBrain.Poc.Creator;

public sealed record CandidateShape(
    IReadOnlyList<string> SourceFiles,
    string Source,
    string SourceHash);
