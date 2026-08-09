using System;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Creator;

/// <summary>
/// A normal-path candidate whose compilation result remains bound to the authenticated
/// principal that reserved its family. Only <see cref="CandidateAuthoringService"/> can
/// create this boundary object.
/// </summary>
public sealed class OwnerBoundCompiledCandidate
{
    internal OwnerBoundCompiledCandidate(
        FileCandidateCompiler.CompiledCandidate candidate,
        AuthenticatedPrincipal owner)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    internal FileCandidateCompiler.CompiledCandidate Candidate { get; }

    internal AuthenticatedPrincipal Owner { get; }

    public string Id => Candidate.Id;

    public CandidateFamilyId Family => Candidate.Intent.Family;
}
