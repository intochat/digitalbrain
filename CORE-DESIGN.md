# DigitalBrain Core — POC-0 architecture (2026-08-09, v5)

> **Status:** approved greenfield proof charter. This replaces the rejected
> behavior DSL, interpreter, and legacy-coexistence design. It makes no claim
> that POC code already exists.

## 1. The hypothesis

POC-0 must answer one falsifiable question:

> Can owner intent become one reviewable C# source file which, after verified
> owner approval and a cold restart, acts as ordinary durable DigitalBrain
> neurons and updates a trusted Flutter chart through typed contracts?

The POC succeeds only if the generated file:

1. contains ordinary C# classes inheriting from **Neuron** and ordinary
   **Synapse** records;
2. sends a point through the trusted chart module rather than owning UI;
3. survives a whole-process restart with state, journal, and undelivered work;
4. is refused before admission when it requests ungranted authority.

“ScriptedNeuron” is authoring provenance only. It is not a runtime base type,
an interpreter payload, a behavior definition, a special router path, or a
catch-all receiver.

POC-0 is deliberately greenfield. It must not reference the current
pet-project runtime, DigitalBrain.Scripting, its generated project-file flow,
or any legacy implementation.

## 2. Two concepts, one verb

DigitalBrain exposes only these domain concepts:

- **Neuron** — a durable, addressable actor that handles declared synapse
  types.
- **Synapse** — immutable data carrying a typed fact or command. The runtime
  journals it.

The one action is **FireSynapse**. IHandle<T>, IDigitalBrain,
IDurableState<T>, router, journal, outbox, compiler, and candidate catalog are
runtime machinery, not a third domain vocabulary.

~~~csharp
public abstract record Synapse;

public interface IHandle<TSynapse>
    where TSynapse : Synapse
{
    Task HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}

public interface IDigitalBrain
{
    Task FireSynapse(
        Synapse synapse,
        CancellationToken cancellationToken = default);
}

public interface IDurableState<TState>
{
    TState Value { get; }
    void Replace(TState next);
}
~~~

The runtime supplies IDurableState<T> only inside the owning neuron’s durable
turn. It exposes no raw store, journal, provider, save operation, service
provider, grain factory, or other neuron’s state. State replacement and
outgoing synapses commit as one durable turn.

Handler matching is exact. IHandle<SocialPostObserved> handles that one
contract; it is not a subscription to a base type or arbitrary subtype.
IHandle<Synapse> is invalid in generated code.

### The durable activation seam

Neuron is a normal compiled DigitalBrain base type, not a thin public wrapper
over raw Orleans APIs. A trusted internal NeuronActivationGrain is the only
Orleans-facing activator. It is keyed by an immutable route binding
(owner, contract, candidate family/revision when applicable, trusted target
scope when applicable, neuron type), owns the journal/outbox/state turn,
constructs the selected normal Neuron with restricted dependencies, and invokes
its exact IHandle<T> method.

The same activation seam hosts trusted and generated Neuron subclasses. It
does not interpret a behavior language or provide a ScriptedNeuron special
case; it supplies durable activation to compiled C# selected by the route
table. Candidate assemblies are application parts so their serializers are
available at host construction. This avoids exposing a raw GrainFactory,
service provider, or Orleans activation capability to generated code.

## 3. The real POC route

The generated module must not define either end of the real integration.
Trusted modules own stable ingress and effect contracts. The generated file
proves custom vocabulary only inside its own atomic module.

~~~mermaid
flowchart LR
    S["Trusted social module<br/>SocialPostObserved"]
    R["Generated elon-chart.cs<br/>ElonPostRuleNeuron"]
    M["Generated ElonPostMatched"]
    F["Generated ChartForwarderNeuron"]
    A["Trusted chart contract<br/>AddChartPoint"]
    C["Trusted chart module<br/>ChartNeuron"]
    U["Flutter chart"]

    S --> R --> M --> F --> A --> C --> U
~~~

| Item | Owner | What generated code may do |
| --- | --- | --- |
| SocialPostObserved | trusted social-ingress module | Handle it; never redefine or spoof it. |
| ElonPostMatched | generated candidate | Define it and route it between generated neurons. |
| AddChartPoint and ChartPointAdded | trusted chart-contract module | Construct an allowed AddChartPoint only. |
| ChartNeuron, chart state, Flutter bridge | trusted chart module | Never reference implementation types or UI capabilities. |

The candidate contains one local synapse, generated state, and two ordinary
neurons:

- ElonPostMatched : Synapse;
- ElonPostRuleState;
- ElonPostRuleNeuron : Neuron, IHandle<SocialPostObserved>;
- ChartForwarderNeuron : Neuron, IHandle<ElonPostMatched>.

For a valid social fact from author **elonmusk**, the rule neuron advances its
private durable state and fires ElonPostMatched. The forwarder fires
host-owned AddChartPoint. A fact from another author produces neither local
nor chart output. ChartNeuron owns its durable point collection and publishes
its Flutter projection.

The trusted social ingress derives a durable inbound identity from owner,
contract, and source-post ID. Re-observing the same source post therefore
reaches each matching generated family at most once; generated state only needs
to track its family-local accepted ordinal.

The candidate sees the AddChartPoint contract, never ChartNeuron. Flutter
widgets, channels, credentials, and rendering therefore remain behind a
trusted deep module.

### The routing identity that FireSynapse does not expose

FireSynapse remains the sole candidate-visible verb, but it is not a bare type
broadcast. The runtime owns an immutable SynapseEnvelope containing:

- owner identity from authenticated ingress;
- contract alias;
- candidate-family identity and a pinned target candidate revision whenever
  delivery enters generated code; a candidate-local envelope also records its
  producing candidate revision;
- target scope when a trusted contract names an instance;
- inbound identity, causation identity, and runtime-derived delivery/effect
  identity;
- capability origin and output ordinal.

Candidate code receives a scoped IDigitalBrain proxy. It cannot construct,
replace, or select envelope fields; FireSynapse derives them from the current
durable turn and the approved route binding.

At cold boot the verified catalog creates these immutable bindings:

| Inbound contract | Binding key | Selected activation |
| --- | --- | --- |
| SocialPostObserved | owner + each active candidate family/current revision whose grant permits that trigger | one binding pinned to that revision’s ElonPostRuleNeuron |
| ElonPostMatched | owner + candidate family + producing revision/local alias | that same pinned revision’s ChartForwarderNeuron |
| AddChartPoint | owner + trusted chart ID | that owner/chart’s ChartNeuron |
| ChartPointAdded | owning ChartNeuron turn | terminal chart journal fact; no routing outbox entry |

A trusted inbound fact is expanded by the immutable route table into one
envelope per matching active family, each carrying the active pointer’s exact
target revision at creation time. The inbound receipt is deduplicated once at
trusted ingress; its per-family outgoing delivery identities are
runtime-derived, so a duplicate source post creates no second turn in any
family. A family whose trigger grant does not contain the contract receives no
envelope.

A candidate family is a host-minted opaque stable identity for one owner’s
named rule. Its canonical grammar is cf_ followed by exactly 26 lowercase
base32 characters (a-z and 2-7). The control plane normalizes and collision
checks it before persistence. A friendly owner/rule display name is metadata
only: it never enters C# identifiers, assembly names, namespaces, aliases, or
route keys. Source namespace, assembly name, local aliases, and route bindings
derive only from the canonical family ID. One active revision exists per
family, while different owners may have different active families in one host
without type or alias collision. A behavior-only revision keeps the
family/local schema; an incompatible schema is rejected.

## 4. Owner and capability are runtime facts

Authenticated ingress assigns every inbound envelope an owner. Source text,
command-line arguments, a candidate-created OwnerId, and caller-chosen
envelopes cannot establish or alter it.

IDigitalBrain is invocation- and owner-scoped. Each candidate receives a
finite grant:

- permitted trigger contracts;
- permitted output contracts;
- permitted target scopes, including that owner’s chart identities;
- permitted state schema and a state-size limit.

The runtime enforces the grant on every FireSynapse. ChartNeuron validates
owner, target, schema, capability origin, and durable effect identity again.
Constructing a foreign ChartId cannot update another owner’s chart.

The POC control edge uses a trusted test owner authority that issues opaque
owner-session tokens to the host-process harness. Tests may use readable names
to seed that authority, but candidate code and public projection calls receive
only an authenticated principal derived from a token. A forged owner name is
not a principal.

This is the general future pattern. A script does not open a file; it fires a
contract toward a trusted FileSystemNeuron whose module owns paths,
credentials, authorization, and actual I/O. POC-0 intentionally contains no
filesystem module or arbitrary external effect.

The POC social fact contains only what this rule needs: source-post ID, stable
author identity, and occurrence time. It does not journal body text, prompts,
credentials, OAuth material, or Flutter session data. POC storage is local,
disposable test data. Each test has an isolated owner-data root and teardown
must prove removal of its journal, outbox, snapshots, chart projection, and
test sessions. Candidate source/evidence needed for that test’s restart proof
may exist only until its final teardown, which also removes its disposable
candidate and control-plane test roots. Retention, redaction, and a real
owner-facing deletion contract are product gates before real owner data.

## 5. One C# source artifact, not one generated project

One candidate is exactly one persisted, owner-visible .cs file. It may declare
a closed family of state records, local synapses, and normal neurons. All types
in the file share a candidate identity and promote or roll back together.

The Creator stamps, and admission later verifies, this fixed POC header:

~~~csharp
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net11.0
#:property OutputType=Library
#:property PublishAot=false
#:property ImplicitUsings=disable
#:property AssemblyName=DigitalBrain.Poc.Candidate.<family-id>
#:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj
#:project ../../../src/DigitalBrain.Poc.Social.Contracts/DigitalBrain.Poc.Social.Contracts.csproj
#:project ../../../src/DigitalBrain.Poc.Charting.Contracts/DigitalBrain.Poc.Charting.Contracts.csproj
~~~

The POC candidate is a normal managed IL library. It has no Main, top-level
statements, ConnectAsync, static or module initialization, or arbitrary
constructor injection. A one-file edge client using
DigitalBrainClient.ConnectAsync(args) and FireSynapse(...) remains a valid
future product shape, but it is a one-shot client rather than the durable
neuron proof.

The text #:DigitalBrain.Abstractions is not a legal file-app directive. POC-0
uses legal, fixed local #:project directives. A later portable package may use
pinned #:package directives. A candidate cannot choose its own SDK, project,
package, include, or property directives.

The candidate lives at poc/candidates/<run-id>/<sha256>/elon-chart.cs, so the
fixed ../../../src references resolve against its disposable per-run root. The
SDK may synthesize virtual-project metadata and build output. That is
expected implementation detail, not a generated or retained candidate
.csproj. The build command is dotnet build on the source file; publishing is
prohibited. PublishAot=false is fixed because Native AOT cannot dynamically
load the IL candidate into the restarted JIT host.

The file-based model is documented in
[Microsoft’s file-based apps documentation](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps).
The AOT limitation is documented in
[Microsoft’s Native AOT limitations](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#limitations-of-native-aot-deployment).

## 6. Creator: AST first, C# second

No owner or model supplies raw C# source for admission. Prose may become a
schema-validated, compiler-private NeuronIntent. That is neither persistent
behavior data nor a runtime interpreter.

~~~text
owner prose
  → typed NeuronIntent
  → Roslyn SyntaxFactory tree
  → canonical elon-chart.cs
  → syntax and semantic policy checks
  → real SDK IL build
  → isolated quarantine scenario
  → explicit owner approval
  → cold-restart admission
~~~

The Creator owns every declaration, expression, statement, directive, and
formatting. It must:

1. construct the complete tree with Roslyn syntax factories;
2. reparse persisted source and verify the fixed header byte-for-byte;
3. bind it against the exact trusted reference graph and a semantic allowlist;
4. prove every generated receiver derives from Neuron and closes one exact
   IHandle<T> interface;
5. collect syntax, declaration, semantic, analyzer, and SDK-build diagnostics;
6. persist canonical source and AST hashes; and
7. refuse unknown symbols, duplicate aliases, collisions, or policy mismatch
   before quarantine.

The useful precedent in the old RoslynAgent is its use of syntax trees,
compilations, semantic models, and diagnostics. It is not reused as product
code: its workspace/editor assumptions and text-fragment fallback do not meet
this boundary. Roslyn is fast preflight; the actual file-based SDK build is
the final directive and source-generator oracle. See
[the Roslyn compiler API model](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/compiler-api-model).

## 7. The closed C# profile

The Creator emits and admission accepts only bound symbols from this closed
profile:

- immutable values and approved record constructors;
- declarative serializer metadata only: exact Orleans GenerateSerializer,
  Alias, and member Id attributes on generated synapse/state declarations;
- locals, comparisons, boolean expressions, if, and switch;
- typed private durable state through IDurableState<T>;
- awaited brain.FireSynapse calls;
- runtime cancellation/time and declared approved pure helpers with an acyclic
  call graph.

Admission refuses before publication:

- filesystem, network, process, environment, console, native interop, and
  arbitrary external APIs;
- reflection, typeof, GetType, object/dynamic escape hatches, activators,
  assembly loading, ServiceProvider, GrainFactory, raw Orleans objects, and arbitrary
  constructor dependencies;
- loops, recursion, lambdas or delegates, threads, timers, locks, background
  work, parallel tasks, Task.Run, and static mutable state;
- top-level effects, static constructors, unsafe code, P/Invoke, preprocessor
  indirection, unapproved directives, and names/aliases colliding with trusted
  modules.

This is an **allowlist of resolved symbols**, not a source-string blacklist.
Admission checks both the AST and the built assembly reference set.
The serializer attributes are the sole Orleans metadata exception. They do not
expose a grain, serializer service, activation, stream, or runtime object to
candidate behavior; their type/member Id values are host-generated and checked
for the exact expected declaration shape.

Honesty clause: this proves policy-enforced, owner-reviewed source. It is not
an operating-system sandbox against malicious IL injected by an attacker.
POC-0 admits only Creator-produced, hash-verified artifacts and makes no
hostile-code-containment claim. A product-grade hostile-code guarantee needs a
later restricted worker or process with filesystem, network, process, IPC, and
credential permissions removed.

## 8. Durable routing physics

The fresh runtime owns routing, state, journal, and outbox. For every handler
turn, one atomic commit contains:

1. inbound envelope receipt and deduplication watermark;
2. the typed state replacement; and
3. every outgoing envelope in creation order.

Only committed envelopes enter the outbox. Every envelope whose receiver is
generated retains its target candidate revision; dispatch resolves that exact
loaded revision, never whichever revision later becomes active for the family.
On boot, the runtime verifies and loads the approved candidate before accepting
input, restores state, and resumes undelivered envelopes. Delivery is at least
once. Each receiver deduplicates using a runtime-derived delivery/effect
identity; generated source does not invent that identity.

ChartNeuron deduplicates AddChartPoint by that durable identity. It allocates
the chart ordinal itself; generated code submits only source-post identity and
time. A crash after the generated turn commits but before chart acknowledgement
retries to exactly one point rather than losing or duplicating it.

ChartPointAdded is a terminal chart fact. ChartNeuron journals it inside the
same durable turn that stores the point, and that turn is its acknowledgement;
the fact does not enter a routing outbox. The trusted chart projection endpoint
reads the owner-matched ChartNeuron snapshot.

There is no live type admission, subscription mutation, behavior registry,
interpreter, or implicit catch-all receiver. Orleans application parts are
configured as part of host construction, so POC-0 admits candidates only on
cold restart. See [Orleans application-part configuration](https://learn.microsoft.com/dotnet/orleans/host/configuration-guide/server-configuration).

## 9. Candidate lifecycle

Each content-addressed candidate record binds:

- authenticated owner identity;
- stable candidate-family identity;
- source bytes and canonical AST hashes;
- fixed-header, SDK, compiler, and reference hashes;
- generated type identities, handled contracts, output contracts, and target
  scopes;
- state-schema/local-synapse aliases and policy version;
- all diagnostics, IL hash, and quarantine evidence;
- approval record and prior approved candidate hash.

~~~text
Draft → Validated → Quarantined → Awaiting owner approval
      → Approved inactive → Active after restart
      → Rolled back after restart
~~~

Candidate metadata stored beside source is evidence, not authority. A trusted
control-plane store outside the candidate directory holds a signed immutable
attestation over the record and a separately signed owner-approval record. The
active/previous pointer lives there too. PointerSigner signs the canonical
pointer payload (owner, family, current/previous hashes, parent payload hash,
and version) with a P-256 control-plane key; CandidatePointerHead stores that
canonical payload hash, not the detached signature. The POC test authority
keeps all signing material outside candidate storage.

Quarantine starts a disposable brain without production data, credentials, or
live chart effects. Promotion needs an authenticated principal’s approval of
the exact signed record. Promotion and rollback only move the trusted
accepted-candidate pointer; they never modify a running host. Before either
transition, the supervisor puts the current host into a **Quiescing** ingress
state. Trusted ingress stops accepting new owner facts (it returns a retryable
quiescing result). Admission atomically acquires an in-flight lease before it
can enqueue a turn; closing the gate prevents new leases, then waits for every
already-held lease, in-flight turn, and outbox envelope targeted at the current
candidate revision (including a trusted-ingress fan-out envelope) to drain or
refuses the transition. POC-0 never runs old and new revisions together merely
to deliver stranded local work. At boot the host
verifies source, IL, header, references, capability grant, scenario evidence,
attestation, approval, and pointer before taking input.

The control-plane store keeps a trusted per-(owner, family) pointer head:
monotonic version, current canonical pointer-payload hash, and parent payload
hash. A new
promotion or rollback succeeds only through an atomic compare-and-swap from
that head to a higher-version signed pointer. A rollback is a new higher
version pointing at the prior artifact; it never restores an old pointer file.
Boot first refuses an invalid pointer signature, then a signed pointer whose
version/payload hash does not match the current trusted head. The POC assumes
that head store is the trust root; defense
against an attacker rolling back the entire trusted store is a production
storage/attestation concern, not something a candidate-file signature can
solve.

The supervisor uses a two-phase handoff: after the old host is quiesced and
drained, a new child loads and verifies the proposed module in a no-input ready
state, then the control plane advances the pointer head, then the old host
stops and the ready child opens ingress. A child failure before ready or before
that compare-and-swap reopens the old host’s ingress and leaves the visible
pointer unchanged.

Local generated synapses use host-generated immutable aliases under the stable
POC schema. POC-0 has no in-place state or synapse-schema evolution. Rollback
selects a previously built immutable artifact, never regenerated source. If
retained journal data requires an incompatible artifact, rollback refuses
rather than dropping or guessing at data.

Every serializable host or generated contract/state record receives an Orleans
serializer declaration plus a host-owned immutable alias. The trusted POC
aliases are db.poc.social.post-observed.v1, db.poc.chart.point.v1,
db.poc.chart.point-draft.v1, db.poc.chart.add-point.v1, and
db.poc.chart.point-added.v1. POC aliases are stable across behavior-only revisions of the
elon-chart module, so a restarted host can read its retained journal. An
attempt to reuse a trusted alias or change a local schema in place is refused.

## 10. Acceptance gates

POC-0 is accepted only when all of these have executable evidence:

1. The Creator emits exactly one elon-chart.cs recursively within its
   per-run candidate directory and no candidate .csproj; the actual SDK builds
   managed IL and the configured Orleans serializer round-trips its generated
   local synapse.
2. The candidate has two ordinary durable Neuron subclasses and one generated
   local Synapse; no ScriptedNeuron, interpreter, or special behavior runtime
   exists.
3. A trusted valid SocialPostObserved moves through both generated neurons,
   host-owned AddChartPoint, and the trusted ChartNeuron, which exposes point
   one through its Flutter projection.
4. A non-Elon fact causes no AddChartPoint and no chart point.
5. Killing the entire host, booting the same approved candidate against the
   same durable store, and firing a second valid fact exposes point two,
   generated state count two, and one globally ordered retained journal
   history. Re-firing the original owner/source-post identity after restart
   produces no new rule turn, state increment, or chart point.
6. A forced crash after ChartNeuron commits AddChartPoint but before the
   upstream outbox observes its acknowledgement recovers the first point
   exactly once; replaying the delivery identity does not duplicate it.
7. A separate crash after the rule commits ElonPostMatched but before its
   forwarder receives it causes the next host to deserialize and deliver that
   generated local synapse.
8. Owner B cannot invoke Owner A’s candidate, route to Owner A’s chart, or read
   Owner A’s projection through a forged owner name or token.
9. File, HttpClient, reflection, ServiceProvider, GrainFactory, Task.Run,
   loops, recursion, top-level statements, and unapproved contracts are all
   refused before publication.
10. Tampering with candidate source, IL, candidate metadata, fixed header or
    references, capability grant, quarantine evidence, signed attestation,
    signed approval, or active pointer prevents host startup and leaves the
    last known-good candidate untouched. Replaying an older valid signed
    pointer also fails against the trusted pointer head.
11. A candidate remains absent until owner approval and a restart. Promotion
    and rollback first quiesce ingress, wait in-flight turns and drain the
    current revision’s candidate-targeted outbox (including trusted fan-out)
    or refuse;
    rollback selects the prior artifact only after another restart, and an
    incompatible retained local schema makes it refuse. An approved-inactive
    module on disk cannot be selected by normal boot.
12. A Flutter integration run uses an authenticated owner projection request to
    observe the chart point produced by the generated module, rather than a
    fake projection.
13. The POC solution has no project reference to the current runtime or
    DigitalBrain.Scripting.
14. Test teardown deletes the isolated owner-data root and verifies that no
    journal, outbox, chart projection, session, candidate, or control-plane
    test record remains for that run.
15. A forced crash after trusted social fan-out commits but before the rule
    acknowledges the envelope leaves it pinned to the old revision. Promotion
    then refuses until a host booted from the old pointer drains that envelope;
    it never delivers it to the proposed revision.
16. Two active granted families for one owner each receive one delivery for
    one trusted post, while an active family without that trigger grant receives
    none; their family-qualified local aliases do not collide.

## 11. Deliberate exclusions

POC-0 does not build the legacy BehaviorDefinition system, a
Compose/Interpret/Compile ladder, runtime-created subscriptions, hot type
loading, collectible assembly contexts, schema migration, arbitrary code,
dynamic package acquisition, external social OAuth, discovery/recall/watch,
general UI generation, or a generated Flutter chart.

It proves the narrow foundation first: one owner-reviewed C# module can be a
normal durable brain module and safely orchestrate a trusted chart module.

## 12. Decision log

| Decision | Why |
| --- | --- |
| Generated code is a normal Neuron, not ScriptedNeuron | Durable custom logic should use the same compiled neuron model as trusted modules; ScriptedNeuron is provenance only. |
| Candidate is exactly one authored C# library file | File-based C# supplies an SDK virtual project without persisting a generated candidate project. |
| Admission is cold-restart only | Candidate serializers and application parts are configured during host construction; POC-0 makes no hot-load claim. |
| ChartNeuron is trusted POC code, never generated | It owns Flutter/UI state, chart ordinals, projection, and effect deduplication. |
| Creator emits Roslyn ASTs, not LLM text | It makes the admitted C# shape deterministic and semantically checkable. |
| Capability policy is not a hostile-code sandbox | POC-0 protects the Creator path and rejects ungranted APIs, while a later restricted process is needed for malicious-IL containment. |
| Candidate evidence is externally signed | A hash in a writable candidate directory cannot be the trust root. |
| Active pointer advances through a trusted monotonic head | A valid old signature is not enough; promotion and rollback need replay-resistant ordering. |
| POC owner data is disposable and teardown-proven | Deletion-first means no indefinitely retained test journals or artifacts while product deletion design is still out of scope. |
