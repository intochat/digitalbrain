# CoreV2 scenarios

The proof is framework-neutral. MCP and Flutter are equal adapters: each discovers eligible Operations, invokes one explicit Operation, and observes the policy-filtered BrainActivity. The proof contains no provider integration or presentation scenario.

## Proof.Run@1 — first revision, correction, retirement, and reuse

1. An authenticated Principal invokes `Proof.Run@1` through either adapter. CoreV2 authorizes the Operation, applies the caller idempotency scope, creates a BrainActivity, and direct-sends the sealed Operation input to the proof entry role.
2. The proof entry Neuron uses a typed Capability if needed, preserves its ActivityContext and delegated authority, and emits the published `ProofProduced` DomainEvent.
3. The first BrainGraph Synapse revision resolves `ProofProduced` to the summary behavior. The Neuron journals the event and stages an outbox route snapshot for that resolved target.
4. The Principal invokes the correction Operation. Its entry role produces a `Rewire` evidence event identifying the opaque SynapseKey and the desired assessment behavior. Rewire is evidence; it does not change topology by itself.
5. Authorized BrainGraph replacement keeps the same opaque SynapseKey, changes only the target and Reshape, preserves the old revision in history, and records provenance from the Rewire. It changes neither source nor DomainEvent contract.
6. Later `ProofProduced` emissions resolve to the assessment behavior. The old summary behavior no longer receives later emissions.
7. An authorized Retire removes the live Synapse from resolution. A later `ProofProduced` is journalled and visible on the BrainActivity with zero receivers; it stages no outbox delivery.
8. A Wiring proposal is staged from the successful proof pattern and then activated for another Principal. It binds only Neuron roles and public contracts: the `Proof.Run@1` trigger, `ProofProduced`, registered Reshape, and lineage. It copies no Entity, authority, journal, transcript, payload, endpoint identity, or private Synapse.

## Boundary check

The caller never fires `ProofProduced`, names a Neuron, supplies a SynapseKey, selects a target, or calls BrainGraph. The adapter’s role ends at discover, invoke, and observe. CoreV2 owns the direct send, internal emission, route snapshot, Rewire evidence, authorization, replacement, retirement, and Wiring activation.
