using System.Collections.Generic;

namespace DigitalBrain.Poc.Foundation.Tests;

internal sealed record CandidateBuildResult(
    bool Succeeded,
    string Diagnostics,
    string CandidateDirectory,
    string AssemblyPath,
    bool FixedHeaderVerified,
    IReadOnlyList<CandidateDeclaredType> DeclaredTypes,
    IReadOnlyList<CandidateConstructor> Constructors,
    IReadOnlyList<CandidateContractAlias> ContractAliases);
