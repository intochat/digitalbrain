# DigitalBrain v2 — Neuron OS

**Status:** draft for approval
**Date:** 2026-07-16
**Replaces:** `2026-07-16-brain-workspace-module-sdk-design.md` (spec v1) entirely; amends `EVERYTHING-IS-A-NEURON.md` where noted (storage, workspace-as-module, MCP/UI edge).
**Companion visual:** https://claude.ai/code/artifact/c431a9bb-04a8-4666-9507-067fcc2e1469

## 1. Thesis: DigitalBrain is an operating system

One universal kernel hosts every addressable object as a Neuron. Everything above the kernel is a module — the windowing system, AI, Google, Salesforce — installed the same way, described the same way, governed by the same human-approval rail. Behaviors are single-file C# scripts: user programs that read, think, propose effects, and create live reactive UI.

| OS concept | DigitalBrain v2 |
|---|---|
| Kernel | Identity, scheduling (Orleans activations), IPC (invocations), security (grants + rail), storage (journal) |
| Drivers & services | modules/google, salesforce, web (outside world); modules/ai (system service behind ModelFacet) |
| Windowing system | modules/workspace: destinations, feed (compositor), inspector; UI Neurons are windows |
| User programs | behaviors/ — single-file C# scripts as cluster clients |
| Display | Flutter: a thin shell binary rendering a closed vocabulary; not a module, replaceable in principle |
| Remote shell | MCP: agents address the same windows and drivers under the same permissions |

v2 is a **one-shot rebuild**, not a migration: the v2 tree is built clean in dependency order, the keep-list is ported, and v1 is deleted wholesale in the same change series. Gates (root test suite green with zero skips, live MCP-to-Flutter proof, deletion metrics) apply at the end of the shot.

### Target repository shape

```text
kernel/
├─ Brain.Contracts           INeuron · NeuronAddress · Synapse · facet seams · error taxonomy
├─ Brain.Kernel              NeuronGrain (one grain type) · pipeline · journal storage + encryption · rail
└─ Brain.Client              BrainCluster.Connect(args) + Get<T>(scope) typed proxies
modules/
├─ Brain.Modules.Sdk         kind registration · conformance suite · BrainTest harness
├─ Brain.Modules.Workspace   destinations · feed · inspector queries · two-tier UI vocabulary
├─ Brain.Modules.Ai          ILlm · agents · model catalog + tiers · workflow runner · Ollama/AzureOpenAI
├─ Brain.Modules.Google
├─ Brain.Modules.Salesforce
└─ Brain.Modules.Web
behaviors/                   single-file C# scripts + BDD tests
edge/
├─ Brain.Mcp                 neuron_describe · neuron_read · neuron_invoke · catalog resource
└─ Brain.UiGateway           POST /ui/invoke · GET /ui/describe · WS /ui/watch
hosts/
├─ Brain.AppHost
└─ Brain.ServiceDefaults
app/                         Flutter: shell · Tier 1 views · block renderers · inspector · theme
tests/
├─ Brain.KernelTests
├─ Brain.ConformanceTests
└─ Brain.E2ETests
```

## 2. Product surface (unchanged from spec v1)

The attention-first workspace ships exactly as approved: Today home ordered by what needs the owner (approvals, failing/running work, connection problems), five destinations (Today, Chat, Abilities, Connections, Activity), the shared inspector with four fixed sections (Status, Caused by / Led to, Depends on, Actions), and the summonable composer (Cmd/Ctrl+K). Routes are Neuron addresses; navigation is addressing. The ontology stays felt, not taught: no "Neuron" vocabulary in the UI.

The five user concepts (Work, Approvals, Abilities, Connections, The explanation), the primary journeys (morning review, command, gain an ability, diagnose, extend), the responsive layout rules (desktop four zones; tablet inspector sheet; compact bottom bar), the accessibility bar (WCAG 2.2 AA: contrast-checked tokens, full keyboard traversal, semantic labels, reduced motion, 44px targets), and the design-system principles (calm instrument, dark-first, Inter + JetBrains Mono, one indigo accent for brain liveness, amber/green/orange status hues, color never decorative) are adopted from approved spec v1 unchanged, with one amendment: the projection system beneath them is the two-tier vocabulary of §6 below, not v1's fixed kind list.

## 3. Universal kernel

### 3.1 Identity

`NeuronAddress(OwnerId, SpaceId, NeuronId)` is the durable logical identity (EIAN §3 unchanged). One Orleans grain class, `NeuronGrain`, keyed by `NeuronAddress.ToGrainKey()`. MCP, Flutter, and behavior scripts resolving the same address reach the same activation. Transport audience, session ids, tokens, replicas, and current model never appear in the key.

### 3.2 Contract

```csharp
[Alias("digitalbrain.neuron.v1")]
public interface INeuron : IGrainWithStringKey
{
    Task<NeuronDescription> DescribeAsync();
    Task<NeuronSnapshot> ReadAsync(NeuronRead request);
    Task<NeuronReceipt> InvokeAsync(NeuronInvocation invocation);
    Task<NeuronEventPage> ReadEventsAsync(NeuronEventCursor cursor);
}
```

Typed domain interfaces (`IGmail`, `IChat`, `ILlm`, `IWindow`) are **client-side proxies generated over the universal envelope**. They are compile-time sugar; at runtime every call is `InvokeAsync` through the one pipeline. This is why the rail cannot be bypassed by a typed shortcut. Interfaces carry their own metadata as static virtual members (the IAW pattern): display name, description, capabilities, routing examples. `DescribeAsync` and the MCP catalog are generated from the interface — one source of truth.

### 3.3 Invocation pipeline

Every command, from every caller, runs these steps inside the grain:

1. Resolve caller identity: Owner, Actor, session, or behavior-script identity.
2. Evaluate Grants/Requires Synapses for the (caller, target, contract) triple.
3. Idempotency replay check: a duplicate command id returns the original receipt without re-execution.
4. Expected-revision check when supplied; bounded conflict information on mismatch.
5. Resolve the facet handler from the kind registry (module-registered, keyed DI).
6. Execute: deterministic function, connector read, or bounded model workflow via the ModelFacet seam.
7. External mutation stops here: the handler may only emit an Effect Neuron (payload digest, provider idempotency key, requesting identity). A Decision Neuron with an Approves Synapse must exist before any connector accepts the `ApprovedEffectProof`.
8. Append events to the journal; exactly one revision advance; persist before acknowledging.
9. Re-project affected workspace Neurons; append feed records durable-then-visible.
10. Publish observation events (streams) for live progress.

Failure behavior carries over from EIAN §16 unchanged (duplicate command → original receipt; unknown contract → fail closed; missing grant → fail closed before handler; external timeout → outcome-unknown, never blind retry; invalid persisted state → activation fails closed).

### 3.4 Synapses

`SynapseRecord` and the relation set (Contains, Requires, Grants, BackedBy, Projects, CausedBy, Awaits, Approves, EmitsTo, UsesModule) carry over from EIAN §8. Grants and approvals are Synapses with journal authority. The inspector's provenance sections are Synapse queries.

### 3.5 Storage: the journal is the truth

**Amends EIAN §5: `NeuronDocument` is deleted from the design.** Each Neuron is an event-sourced grain on Orleans journaling. Every invocation, revision, effect proposal, decision, and grant is an appended event; current state is the fold of the log; snapshots are an Orleans-managed optimization, never the source of truth.

Consequences:

- `ReadEventsAsync` serves the journal directly; no hand-rolled event tail.
- The Activity destination and the inspector's Caused by / Led to are views over the journal.
- "How the system evolved, what happened, what was decided" is answerable for free — the audit rail is native.
- Behavior lifecycle, effect decisions, and grant changes are ordinary events on the owning Neuron.
- The existing key-ring crypto (`EncryptedPersistentState` internals) ports as an event-payload encryptor: no product data in plaintext at rest.
- Rollback (behaviors, releases) is an event that re-points to a prior state, preserving history.

The kernel rejects: unknown kinds, unknown contracts, unbounded payloads, schema mismatches, revision regressions, duplicate event ids, invalid Synapse relations.

### 3.6 Error taxonomy and feed ordering (retained from spec v1 §8)

The stable machine-readable codes (`feed.not-delivered`, `feed.cursor-stale`, `action.revision-stale`, `action.replayed`, `grant.missing`, `connection.unhealthy`, `auth.expired`) and the durable-then-visible feed ordering ship as specified in v1. The Flutter transport sorts every failure into retryable / re-project / needs-user / terminal. Continuity rules (drafts keyed by address, server-side work leases, connection loss is chrome) carry over.

## 4. Modules

### 4.1 Anatomy

A module is a package that registers Neuron **kinds** with the kernel: for each kind, the typed contract interfaces (with static-virtual metadata), the facet handlers (keyed DI), the event schemas, and the projection shapes. There are no descriptor records: **the module's manifest is its Neuron description** (`DescribeAsync` + Synapses). Spec v1's ModuleDescriptor / CapabilityDescriptor / EffectDescriptor / OAuthDescriptor are deleted as a second catalog ontology; the same facts (capabilities with grant reasons, effect kinds with preview shapes, OAuth host allowlists and scopes) are properties of the module's Neurons, validated by conformance tests.

Rules:

- Modules depend on `Brain.Contracts` + `modules/Sdk` + provider client libraries. Never on the kernel runtime, the edge, or Flutter.
- The kernel depends on no concrete module; hosts compose concretes.
- Scope ownership is enforced by the Sdk (application / owner / actor / session / operation, as in spec v1 §5.4).
- The conformance suite runs for every module: kind registration validity, scope placement, closed health-union coverage, OAuth state machine, effect-proof enforcement, idempotency replay.

### 4.2 modules/workspace — the windowing system

Owns the destination Neurons (Today, Chat, Abilities, Connections, Activity), the feed (compositor), inspector queries, and the two-tier UI vocabulary (§6). Plugs into the ProjectionFacet and ObservationFacet seams. The kernel knows no destination names.

### 4.3 modules/ai — intelligence as a peer module

Owns `ILlm : INeuron`, agent abstractions, the model catalog with Fast/Balanced/Reasoning tiers (IAW `LLMModel` + `LlmAttribute<TModel>` pattern), the bounded workflow runner (ported from `AgentFrameworkWorkflowRunner`), and exactly two provider adapters: **Ollama and AzureOpenAI**. The other four current adapters (OpenAI, Anthropic, xAI, GitHub Models) are deleted until a requirement traces to a person.

The kernel keeps only the **ModelFacet seam**. The known couplings are severed: `InoOperationWorkerGrain` no longer calls the workflow runner directly (it invokes through the seam), and prompt-limit constants become model-catalog metadata. The self-healing compile loop (generate → build → feed errors back → regenerate, from IAW `CodeOrchestratorAgent`) lives here and serves behavior authoring.

### 4.4 modules/google, salesforce, web — drivers

Connector internals (provider clients, OAuth flows, probes) port from the current integrations. Anatomy changes to kind registration. Retained semantics from spec v1:

- The four orthogonal connector state machines (configuration / authorization / health / suspension) become the ConnectionFacet contract; `Authorizing` is durable with expiry; suspension formalizes e68e09c1 (dependent capabilities pause, Today shows one aggregate item, reauthorization resumes).
- `ConnectionHealth` stays a closed union (`Healthy | MissingAppCredentials | NotConfigured | NotAuthorized | TokenExpired | ProviderError | NetworkError`); each unhealthy state maps to exactly one fix action. Generic bool+string health is a conformance failure.
- Effect handlers accept only `ApprovedEffectProof`; no overload takes raw provider arguments.
- OAuth: lazy reauthorization with ScopeVersion and the orphan audit (spec v1 §10.1) — no token data migration.
- Salesforce is implemented from Google's template to prove the template.

## 5. Behaviors — user programs

### 5.1 Scripting model

A behavior is one C# file, a cluster client, programmed against typed Neuron interfaces:

```csharp
using var brain = await BrainCluster.Connect(args);

var gmail  = brain.Get<IGmail>("owner/local/connection/google-primary");
var unread = await gmail.ReadMessagesAsync(new(Unread: true, Max: 20));

var llm   = brain.Get<ILlm>(ModelTier.Balanced);
var brief = await llm.CompleteAsync(new(Prompt: $"Summarize for a morning brief:\n{unread.AsText()}"));

var chat = brain.Get<IChat>("owner/local/actor/dev/chat/main");
await chat.PostAsync(new(brief.Text));

var board = brain.Get<IWindow>("owner/local/actor/dev/ui/inbox-brief");
await board.RenderAsync(Blocks.Doc(
    Blocks.Metric("Unread", unread.Count),
    Blocks.Timeline(unread.Items.Select(m => Blocks.Entry(m.From, m.Subject)))));

var send = await gmail.ProposeSendAsync(new(To: "vlad@example.com", Subject: "Inbox brief", Body: brief.Text));
```

`BrainCluster.Connect(args)` + `Get<T>(scope)` follow IAW's `IAWCluster` exactly. Scripts read freely within their grants, think through modules/ai, create live UI by writing block documents into UI Neurons they own, and can only **propose** external mutations. A BDD test ships with every behavior, running on the `BrainTest` in-memory cluster harness (IAW `AgentTest<T>` pattern) with a mocked ModelFacet, asserting behavior properties such as "proposes exactly one send effect and never mutates directly."

### 5.2 Trust model

| | Dev script | Installed behavior |
|---|---|---|
| Identity | The developer's session identity | Its own behavior Neuron identity, pinned to the content hash of the source |
| Grants | The owner's live grants | Grants Synapses bound to the behavior identity, approved at install |
| Effects | Rail approval required — no dev bypass exists | Rail approval required |
| Execution | Ad hoc, from the developer's machine (hot loop: edit, rerun) | Under a WorkFacet lease with retry, pause, and completion semantics |

### 5.3 Lifecycle: hash + journal

The behavior Neuron's journal is its lifecycle. `Proposed(sourceHash, source, bddResults, requestedGrants)` → rail decision → `Enabled`. Upgrades are `UpgradeProposed(newHash, sourceDiff, grantDiff)` → decision. Version history is the journal; rollback is one event re-enabling a previously approved hash. There are no release artifacts, publication fences, or rollback replays — all of that v1 machinery collapses into events. Compiled assemblies are cached by source hash; compilation is deterministic from source.

### 5.4 Authoring: agent-first plus a slim editor

Primary path: the owner asks in the composer; modules/ai runs the self-healing compile loop server-side, produces script + BDD test, runs the test, and files an installation proposal on the rail — source, test results, and grant requests rendered as a Tier 1 DecisionCard plus Tier 2 blocks on Today.

Secondary path: a slim review-and-tweak surface at `/abilities/:id/source` — the script and its test in a syntax-highlighted code view, editable, with one action (recompile + re-run tests through the same loop endpoint); the proposal updates in place. One script, one test, no file tree, no drafts list (~500–800 Dart lines). Developers otherwise use their own IDE.

Feature Studio (7,323 Dart lines), the releases pages (1,514), and the MCP authoring services (~2,544 C#) are deleted.

## 6. UI: the two-tier vocabulary

Windows (UI Neurons) are described in data, rendered only by first-party Flutter.

**Tier 1 — governed semantic kinds (fixed set):** `GrantPrompt`, `EffectPreview`, `DecisionCard`, `ConnectionHealth`, `Conversation`. Security and consistency surfaces render identically everywhere; modules and scripts cannot restyle them.

**Tier 2 — composable block documents (versioned vocabulary; initial set of 11):** `Section`, `Columns`, `Text`, `Metric`, `Field`, `List`, `Table`, `Timeline`, `Media`, `Progress`, `ActionRow`. Bounded nesting, semantic style tokens only, byte-size caps enforced by the kernel like any payload. `ActionRow` carries only revision-bound ActionSet references — every action is a typed contract invoke through the pipeline and the rail.

Why this is not RFW: RFW shipped behavior (widget trees, data bindings, an event loop — a second UI runtime). Blocks ship content. Pixels, layout, and input never leave first-party Flutter. New expressive power requires a vocabulary version bump and a new first-party block renderer — never a more powerful interpreter.

Live reactivity is substrate, not feature: any mutation (script, MCP, module) advances a revision → re-projection → feed → Flutter repaints. Script-generated dashboards update live while the script runs.

**Flutter impact.** Deleted: `rfw_host/` (4,756), `ui_kit/` (1,954), Forui, the RFW half of `runtime/`, `grpc/` generated (7,953), `features/studio` + `releases` (8,837), flutter_bloc/bloc_test, and the never-imported dependencies (graphic, flutter_earth_globe, lottie, markdraw, youtube_player_iframe). Kept and reworked: shell, theme (calm instrument identity + semantic chroma), `digital_brain_ui`, feed cursor handling, activity/connections controllers re-rendered through kind views and the inspector. New: block renderers (one widget per primitive), Tier 1 kind views, the inspector, Today, the slim source view. State management stays `ChangeNotifier` + `InheritedNotifier`; convention per destination: controller + gateway + views ≤ ~300 lines.

## 7. Edge collapse

### 7.1 Brain.Mcp

Public tools: `neuron_describe(address)`, `neuron_read(address, projection)`, `neuron_invoke(address, contract, input, commandId, expectedRevision?)`, plus a catalog resource generated from interface metadata. Per-noun tools are deleted without alias periods (one-shot rebuild; the E2E suite moves directly to the universal tools). MCP transport sessions authenticate callers and carry no product state.

### 7.2 Brain.UiGateway

JSON over HTTP + WebSocket; **ui.proto and the protobuf toolchain are deleted** (0 generated Dart). Three operations:

- `POST /ui/invoke` — the universal invocation envelope, receipt in response.
- `GET /ui/describe` — projection/describe reads.
- `GET /ui/watch` (WebSocket) — feed envelopes from a durable cursor; reconnect reopens with the cursor and the feed replays; ping keepalive per ASP.NET Core guidance.

Rationale (measured in session): v2 payloads are dynamic block documents — JSON inside any transport — so protobuf adds framing and a codegen chain without typed benefit; perceived latency is bounded by model and provider calls, not the wire; WebSocket is native on Flutter desktop and web. Auth: the existing session-token service; no credentials or provider tokens ever cross to Flutter.

## 8. Keep / delete map (measured 2026-07-16)

| Mass | Lines | Disposition |
|---|---:|---|
| Feature lifecycle (Kernel/Features 6,632 + FeatureHost/Builder 3,624 + MCP authoring 2,544 + Dart studio/releases 8,837) | 21,637 | Delete |
| OrleansTests bound to Feature machinery | ~10–13k of 16,767 | Delete; survivors migrate to KernelTests/ConformanceTests |
| Per-RPC UI edge + generated DTOs (endpoints 1,888 + UiGrpcService 583 + generated Dart 7,953; ui.proto = 26 RPCs) | 10,424 | Delete (transport replaced) |
| RFW + ui_kit + Forui (+ RFW half of runtime/) | 6,710+ | Delete |
| Per-noun MCP tools, per-connector effect rails, duplicated leases | ~2,900 | Delete |
| Capability resolvers + memory service (Capabilities 924 + invoker 296 + params 89 + Memory 633) | ~1,940 | Delete; memory facts become plain Neurons (State + Query) |
| AI runtime (workflow runner 426 + Kernel/Llm 364; 4 of 6 provider adapters dropped) | ~800 | Move to modules/ai |
| Connector adapters (Google 1,836 · Salesforce 2,542 · Web 263) | 4,641 | Move to modules/; internals ported |
| Spine (INO worker, effect plan authority, Conversation/Session/SurfaceFeed semantics, key-ring crypto, AppHost topology, feed cursor handling) | ~6,500 | Port into kernel/modules |

Targets: backend 36.3k → ~9–12k production C#; Flutter 30.0k → ~13–15k handwritten Dart; generated protobuf 7,953 → 0. Lines added, removed, and concepts deleted are recorded at the end of the shot.

## 9. One-shot rebuild order

Dependency order inside the single shot; no compatibility shims, no side-by-side adapters, no alias periods:

1. `kernel/` — Brain.Contracts, Brain.Kernel (NeuronGrain, pipeline, journal storage with encryption, rail), Brain.Client. KernelTests cover pipeline, rail, idempotency, revisions, journal recovery.
2. `modules/Sdk` + `modules/ai` — seams proven; ModelFacet serves the kernel; BrainTest harness ships.
3. `modules/workspace` + `modules/google/salesforce/web` — kinds, connector state machines, conformance suite green for every module.
4. `edge/` — Brain.Mcp (3 tools + catalog), Brain.UiGateway (JSON + WS).
5. `behaviors/` — first behavior + BDD test through the full rail.
6. `app/` — shell, Tier 1 views, block renderers, inspector, Today; destinations land one by one on the new gateway.
7. Demolition — v1 trees deleted; deletion metrics recorded.

Exit gates for the shot: exact root `dotnet test --logger "console;verbosity=minimal"` green with zero skips; live proof `MCP neuron_invoke → Chat Neuron revision → workspace projection → feed → Flutter repaint`; a behavior script proposing an effect that renders as a Today decision card and executes only after approval; deletion metrics meet §8.

## 10. Test strategy

- **KernelTests** — pipeline invariants: idempotency replay, revision conflicts, grant fail-closed, effect gating, journal fold/recovery, event cursor catch-up.
- **ConformanceTests** — one suite, every module: kind validity, scope placement, health-union coverage, OAuth machine, effect-proof enforcement.
- **Behavior BDD** — every behavior ships its test; runs on BrainTest (in-memory cluster, mocked ModelFacet).
- **E2ETests** — the live MCP→Flutter proof; error-taxonomy enumeration (unmapped exception fails the test); the deterministic feed race test (ack-immediately-after-receive must succeed).
- **Flutter** — widget tests per Tier 1 view and per block renderer fed by conformance fixtures; goldens for shell/Today/inspector at three breakpoints; watch-reconnect cursor tests against the taxonomy.
- Zero comments in tracked source; exact root command is the only integration gate.

## 11. Non-goals

Graph-canvas UI; light theme; third-party executable UI; marketplace/billing/distribution; multi-owner collaboration; OAuth token data migration; any new runtime, dispatcher, plugin framework, auth system, or parallel UI protocol beyond the one gateway described here; gRPC anywhere on the UI edge; more than two model providers.

## 12. Risks and open implementation decisions

1. **Orleans journaling maturity for this shape** — the fold/snapshot behavior under large per-Neuron histories needs a spike in slice 1; mitigation: bounded event payloads and Orleans-managed snapshots.
2. **Block vocabulary creep** — the pressure to add primitives is permanent; the discipline rule (version bump + first-party renderer, never interpreter power) must be enforced in review.
3. **One-shot risk concentration** — there is no green-at-every-phase safety net by design; the mitigation is the build order in §9, where the kernel and modules are fully tested before the edge and app land, and the old tree remains in git history.
4. Whether typed client proxies are hand-written for the first kinds or source-generated from day one (decide in slice 1; public shape identical either way).
5. Whether `IWindow`/block contracts live in modules/workspace or modules/Sdk (decide when the second block producer appears).
