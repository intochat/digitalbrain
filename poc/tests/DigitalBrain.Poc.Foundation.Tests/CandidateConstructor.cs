using System.Collections.Generic;

namespace DigitalBrain.Poc.Foundation.Tests;

internal sealed record CandidateConstructor(
    string DeclaringNamespace,
    string DeclaringType,
    bool IsPublic,
    IReadOnlyList<string> ParameterTypes);
