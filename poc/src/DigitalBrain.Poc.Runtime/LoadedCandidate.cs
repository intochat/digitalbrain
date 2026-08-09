using System.Reflection;

namespace DigitalBrain.Poc.Runtime;

internal sealed record LoadedCandidate(
    VerifiedCandidateModule Module,
    CandidateModuleIdentity Identity,
    Assembly Assembly,
    ExactHandlerCatalog Catalog,
    IReadOnlyList<Type> GrantedCandidateOutputTypes,
    IReadOnlyList<Type> GrantedTrustedOutputTypes,
    IReadOnlyList<string> GrantedTargetScopes);
