# Task 4 report: durable concurrent and group orchestration

Status: DONE
Commit: `ai: add durable concurrent and group orchestration`

## Outcome

The AI runtime now exposes the frozen `Concurrent`, `GroupChat`, `Participant`, and
`Participant<TNeuron>` vocabulary. `Concurrent` sends the same immutable MEAI input to exact typed
`ILLM`/`IAgent` participants through the real MAF concurrent workflow. `GroupChat` uses the real MAF
group builder with a round-robin manager, runs one turn per ordered participant, and makes the final
participant the reconciler. The behavior proof composes a typed `ITestConcurrent` agent with a typed
`IGpt56` reconciler; the latter receives both independent answers and produces the group result.

`GroupChat` owns exactly one journaled `IDurableValue<byte[]>`. It stores a five-field envelope:
format version, MAF version, composition fingerprint, ordered typed participant identities, and one
protected MAF session. There is no DigitalBrain transcript, turn counter, checkpoint, event log, or
second history field. The session is reconstructed by an equivalent stable MAF workflow agent on
every call, committed before success returns, and demonstrably resumes history after Orleans
deactivation/reactivation.

The three inherited `IWorker` operations are abstract because C# requires `GroupChat : IGroupChat`
to declare them. Task 4 supplies no worker state or behavior; test-only concrete stubs throw, and
Task 5 remains responsible for the real lease/worker implementation.

## RED evidence

The concurrent contract was introduced before its production vocabulary:

```text
dotnet test tests/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj --no-restore --nologo
CS0246: The type or namespace name 'Concurrent' could not be found.
CS0246: The type or namespace name 'Participant<>' could not be found.
```

The first real MAF concurrent run then exposed stale Orleans capability context inside MAF's
off-thread participant callbacks:

```text
Failed: 1, Passed: 89, Total: 90
The capability request delivery does not target neuron
'llama32:concurrent-models/panel'.
The capability request delivery does not target neuron
'gpt56:concurrent-models/panel'.
```

A direct probe-to-`ILLM` control succeeded against the same deterministic keyed clients. The root
cause was that MAF's callback had no Orleans `SourceContext` but retained the outer probe-to-panel
delivery. The adapter now schedules only its grain-reference invocation onto the orchestration
turn's captured `TaskScheduler`. Existing Kernel filters then attribute and journal the child call
to the panel; no request context is cleared and no authorization filter is weakened.

The durable group proof was likewise added before `GroupChat` existed:

```text
CS0246: The type or namespace name 'GroupChat' could not be found.
CS0535: TestGroupChat does not implement IAgent.RespondAsync(...).
```

The first group implementation compiled but failed the two durable contracts:

```text
Failed: 2, Passed: 91, Total: 93
GroupChat resume: expected first-turn history; actual collection was [second-question].
GroupChat drift: Assert.Throws failure; no exception was thrown.
```

A diagnostic read immediately after deactivation showed the exact storage defect:

```text
Assert.Equal failure: expected the persisted envelope bytes; actual []
```

The sole durable value had been resolved lazily inside `RespondAsync`, after activation replay had
already registered durable fields. Resolving it in the `GroupChat` constructor, matching existing
durable neurons, made Orleans replay that same value on the next activation.

## GREEN evidence

```text
dotnet test tests/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj --no-restore --nologo
Passed: 93, Failed: 0, Skipped: 0

dotnet test --logger "console;verbosity=minimal"
DigitalBrain.Tests:       Passed 143/143
DigitalBrain.Simulations: Passed 93/93
DigitalBrain.HostTests:   Passed 5/5
Total:                    Passed 241, Failed 0, Skipped 0

git diff --check
exit 0
```

The simulations use real one-silo Orleans hosts and deterministic keyed Llama/GPT clients. They
prove immutable concurrent inputs, no answer leakage between concurrent branches, truthful
caller/target/causation/correlation in both outgoing and incoming causal journals, typed `IAgent`
composition, final reconciliation, new activation identity after deactivation, restored first-
and second-turn MAF history, an exact single-session envelope, no known prompt/answer plaintext in
state, invalid-contract rejection, and compatible resume after an incompatible definition is
restored.

The mismatch proof changes a participant name in an in-silo definition source, deactivates the
group, and verifies all of the following before restoring the compatible definition:

- an explicit migration/reset-required failure;
- zero new model/participant calls;
- byte-for-byte preservation of the prior envelope;
- no unprotect, MAF deserialize, or MAF run through the changed definition.

## Envelope, protection, and compatibility

The MAF session JSON is protected with ASP.NET Core Data Protection before entering the envelope.
The purpose chain binds the ciphertext to the format purpose, stable group `NeuronId`, and current
composition fingerprint. This prevents both cross-neuron transplantation and rewriting plaintext
envelope metadata to feed an old same-neuron session to a changed workflow. Malformed envelopes,
unprotect failures, and invalid session JSON fail explicitly as migration/reset required; none
silently reset state.

The selected AI module registers Data Protection internally and exposes no public key or credential
shape. It deliberately preserves any application discriminator already selected by the host.
A persistent shared key ring and identical deployment-owned discriminator across production
silos/restarts remain a Task 10 hosting/deployment obligation; this slice does not claim that
default local key storage satisfies multi-silo durability.

The fingerprint is deterministic over:

- orchestration kind (`group-chat`);
- concrete group type assembly-qualified identity and deterministic module code identity;
- exact MAF Workflows informational version;
- `InProcessExecution.Lockstep` and the stable host-identity scheme;
- round-robin manager algorithm and `MaximumIterationCount`;
- every ordered participant's contract assembly-qualified name, exact `NeuronId`, and derived MAF
  agent ID/name.

Compatibility is checked against the plaintext version, MAF version, fingerprint, and complete
ordered participant list before unprotecting or deserializing. The stable workflow host ID/name are
derived from that fingerprint; both MAF response-detail flags remain `false`.

## MAF/runtime traps

Context7 remained quota-blocked, so the restored MAF 1.13 assemblies/XML, compiler, and real runtime
probes were the API authority.

- Exact group construction is
  `CreateGroupChatBuilderWith(factory).AddParticipants(...).Build()`.
- Exact persistence is `AIAgent.SerializeSessionAsync` and `DeserializeSessionAsync` on the same
  reconstructed hosted workflow; the session contains MAF's complete workflow history/checkpoints
  and grows across turns, so no duplicate transcript or arbitrary Task 4 size limit was added.
- MAF deserialization accepts changed participant identities; Concurrent may fail only later during
  `RunAsync`, while group order/configuration can silently drift. The DigitalBrain fingerprint fence
  must therefore run before MAF deserialization.
- MAF participant callbacks can leave the Orleans activation scheduler. Scheduling the capability
  call back onto the captured turn scheduler is required for truthful child request attribution.
- Concurrent output ordering is nondeterministic. Tests assert content membership, not text order.
- Enabling `includeExceptionDetails` leaks internal failures, and enabling
  `includeWorkflowOutputsInResponse` duplicates outputs. Both stay at their secure defaults.

## Frozen-architecture diff grill

- Public messages remain MEAI only; all MAF session/workflow/protection types are internal.
- Only exact typed `ILLM` and `IAgent` participant contracts are accepted.
- Concurrent uses real MAF fan-out; GroupChat uses the real MAF manager. There is no custom fan-out,
  turn selector, transcript manager, or reconciliation loop.
- Only one protected MAF session envelope is durable.
- No Sequential, Handoff, Magentic, MCP, tool, capability-catalog, lease, checkpoint, worker-runner,
  or Task 5 behavior was introduced.
