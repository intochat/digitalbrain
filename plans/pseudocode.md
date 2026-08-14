# CoreV2 pseudocode

The public boundary is an Operation. MCP and Flutter are equal adapters; both discover, invoke, and observe. Neither adapter calls BrainGraph, names a Neuron, or emits a DomainEvent.

```csharp
// Persisted contract identifiers are explicit versioned strings, never CLR typeof values.
sealed record ProofRunV1Input(string RequestId);
sealed record ProofRunV1Result(BrainActivityId ActivityId);
sealed record ProofRunV1Progress(BrainActivityId ActivityId, BrainActivityState State);

sealed record OperationDescriptor(
    OperationId Id,
    ContractId InputContract,
    ContractId ResultContract,
    ContractId ProgressContract,
    AuthorizationRequirement Authorization,
    IdempotencyScope IdempotencyScope,
    ModuleId OwningModule,
    NeuronRole EntryRole);

static readonly OperationDescriptor ProofRun = new(
    Id: "Proof.Run@1",
    InputContract: "corev2.proof.run.input@1",
    ResultContract: "corev2.proof.run.result@1",
    ProgressContract: "corev2.proof.run.progress@1",
    Authorization: "proof.run",
    IdempotencyScope: "caller-and-workspace",
    OwningModule: "Proof",
    EntryRole: "proof-entry");

sealed record ProofProduced(ProofId Id) : DomainEvent;
sealed record Rewire(SynapseKey Key, NeuronRole Target, ReshapeId? Reshape) : DomainEvent;

// Returned by BrainGraph. Callers treat it as opaque and cannot supply it.
SynapseKey firstRevision = brainGraph.Install(new SynapseDraft(
    SourceRole: "proof-entry",
    Contract: "corev2.proof.produced@1",
    TargetRole: "summary",
    Reshape: null,
    Provenance: activity));
```

```csharp
// Adapter boundary: authenticated caller invokes one discovered Operation with a caller idempotency key.
ProofRunV1Result Invoke(
    AuthenticatedCaller caller,
    OperationId operationId,
    ProofRunV1Input input,
    CallerIdempotencyKey idempotencyKey)
{
    OperationDescriptor descriptor = operations.RequireEligible(caller, operationId);
    BrainActivity activity = activities.OpenOrReturnExisting(
        caller.Workspace, caller.Principal, descriptor, idempotencyKey);

    // Direct send to the descriptor's entry role; no client graph call.
    neurons.Send(descriptor.EntryRole, new OperationInvocation(
        descriptor, caller, activity.Context, input, idempotencyKey));
    return new(activity.Id);
}

void OnProofRun(OperationInvocation<ProofRunV1Input> invocation)
{
    ProofProduced produced = new(CreateProof(invocation.Input));
    DomainEventMetadata<ProofProduced> metadata = invocation.Activity.Stamp(produced);

    // Resolution is internal. The staged outbox records the resolved route snapshot.
    RouteSnapshot route = brainGraph.Resolve("proof-entry", metadata);
    journal.Append(metadata);
    outbox.Stage(new RoutedDomainEvent(metadata, route));
}
```

```csharp
// A correction is an Operation, not a client topology command.
void OnProofCorrect(OperationInvocation<ProofCorrectV1Input> invocation)
{
    Rewire evidence = new(firstRevision, "assessment", "proof.to-assessment@1");
    activity.Record(evidence);

    authorization.RequireGraphReplace(invocation.Caller, evidence);
    brainGraph.Replace(firstRevision, new SynapseRevision(
        SourceRole: "proof-entry",
        Contract: "corev2.proof.produced@1",
        TargetRole: "assessment",
        Reshape: "proof.to-assessment@1",
        Provenance: evidence));
}

// Retire prevents a later ProofProduced from resolving. A zero-receiver emission remains journalled.
brainGraph.Retire(firstRevision, authorization);

// A staged Wiring binds only roles and public contracts for another Principal.
wiring.Stage(new WiringProposal(
    Trigger: "Proof.Run@1",
    Roles: ["proof-entry", "assessment"],
    Contracts: ["corev2.proof.produced@1"],
    Reshape: "proof.to-assessment@1"));
wiring.ActivateFor(otherPrincipal);
```

The internal bus carries sealed typed DomainEvents. Provider schemas and JSON do not enter the CoreV2 bus.
