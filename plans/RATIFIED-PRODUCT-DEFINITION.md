# DigitalBrain — Ratified Product Definition

> Synthesized from `Planning.txt` (the full Vlad ↔ Codex architecture-refit session, 36,169 lines),
> cross-checked against `UNIFIED-ARCHITECTURE.md`, `CONTEXT.md`, `CLAUDE.md`, `INTERCONNECT-REVIEW.md`
> and the current solution layout (33 projects).
> Line references `[L…]` point into `Planning.txt`.
> Status: **RATIFIED 2026-08-10 by Vlad** — approved as-is with one owner amendment (§1.18: SDK
> common rails / webhook ingress) and all four open items of §4 ratified as recommended.
>
> **Owner amendment — 2026-08-11:** production source is the current behavioral truth. The central
> automated-test project was intentionally deleted; no automated tests are to be created or run
> during this refit. Final hardening will design a proper per-module testing framework. The
> Salesforce Contracts project is a permanent product boundary containing module neuron and
> synapse interfaces and must not be deleted. This amendment supersedes conflicting test-layout,
> test-gate, and Salesforce-contract deletion text below.

---

## 1. What DigitalBrain IS

### Product identity
1. **A self-programming operating system for a person or a small team.** It adds new behaviors at
   runtime — approved once, then discoverable and autonomously executable — without redeployment.
   Canonical behaviors: *enrich a Salesforce record with customer info from Gmail + web research*
   [L3]; *when @elonmusk makes a new post on X, do this* (external-event trigger via webhook
   ingress + an X integration module — owner amendment, 2026-08-10).
2. **A shared workspace ("a digitalbrain") for teams up to ~100 people**, deployed into a private
   Azure resource group; a personal brain is simply a workspace with one member. One deployed
   installation = one workspace (for the proof journey) [L8576–8990, L10158–10162]. Explicitly NOT
   built for millions of users [L8990].
3. **Open-source, with its own installation-local identity.** No Entra, no IntoChat dependency —
   IntoChat stays a separate commercial product that may integrate later, optionally [L9829, L10011–10017].
4. **Proof journey (the definition of "it works")**: deploy Kernel + Flutter to an Azure dev RG,
   serve `digitalbrain.tech` over HTTPS, username/password login, admin creates a second user, each
   user has private chat + private Google/Salesforce credentials, the assistant builds a
   "Salesforce Weekly" barchart surface, and "Publish to workspace" makes it visible and
   discoverable by the friend's assistant [L10111–10147].

### Core architecture (ratified decisions)
5. **Kernel**: Orleans + Aspire. Neuron = durable grain; per-neuron journals; **journal-is-outbox**
   (fact + outbox commit in one write); at-least-once delivery + dedupe = effectively-once;
   single-threaded turns; `EmitAsync` (broadcast/graph) vs `SendAsync` (directed); client facade
   `IDigitalBrain.FireAsync` (per `UNIFIED-ARCHITECTURE.md`, phases 0–5 landed).
6. **ConnectionGraph is THE core product feature** [L31432]: the durable, long-lived brain topology —
   "when this neuron emits this fact, deliver it there." General mechanics only; connections may
   *trigger* a Behavior but never encode its internal steps [L31413–31414].
   The name `DigitalBrainGraph` was **rejected** (ambiguous); candidates are `ConnectionGraphNeuron`
   / `TopologyGraphNeuron`, "Brain Map" stays the product-facing visualization name [L31424].
7. **Modules are trusted, installed code** (contracts + neurons), composed at startup. Runtime-generated
   logic is **never** a Module or a hot-loaded assembly — it is a versioned **Behavior** [L31421, L31485].
8. **Behavior** = named, reusable product capability with immutable approved revisions.
   **One approval at creation** (revision + permission manifest), after that it is discoverable and
   executes autonomously; a material edit = new revision = new approval [L8576–8580].
9. **Behavior authoring model = single-file C# apps** (.NET 10 file-based apps, `#:` directives,
   `DigitalBrainClient.ConnectAsync`) — Vlad's explicit selection [L34770, recorded L36001].
   Ratified thesis: **"Script expresses the behavior. DigitalBrain provides durable effects.
   MAF is an optional capability used inside the script."** [L34611]
   (This supersedes the earlier `db.behavior/v1` YAML + `AgentProgram | WorkflowProgram` proposal,
   which Vlad pushed back on at [L32343] and Codex conceded at [L34607–34609].)
10. **Execution module** (ratified rename from `DigitalBrain.Modules.Tasks` → `DigitalBrain.Modules.Execution`,
    `IExecution` + `ExecutionNeuron`, **no compatibility layer**) [L23623, L23660]:
    the deepened durable-execution kernel. Ratified vocabulary (recorded in `CONTEXT.md`):
    **Task** (future user-visible goal) / **Execution** (one durable run) / **Attempt** (retry
    generation) / **Operation** (one externally observable effect) / **Blocker** (why an Execution
    waits) [L23646–23652]. Hybrid interface: minimal deep `Apply/Read` core + typed
    `ConversationTurn` adapter [L23596–23609]. Hard rule: after a non-idempotent external operation
    starts, an unknown outcome **never auto-retries** → `OutcomeUncertain` until reconciled [L23613].
11. **Conversation is its own domain module** — six ratified decisions [L15039–17743]:
    - D1: durable DigitalBrain conversation messages are the canonical source of truth; MAF
      `AgentSession` is only replaceable per-agent execution state [L15045].
    - D2: `UI ─► Conversations ◄─ AI`; Conversation lives in neither AI nor Kernel [L15619].
    - D3: UI clients subscribe to resumable projections (by message sequence); UI widgets are not
      Connection targets; the old `Chat` neuron dissolves into Conversation + a pure Flutter projection [L16689].
    - D4: one isolated MAF session per `Conversation × Agent` [L17680].
    - D5: `Conversations.Contracts` owns provider-neutral `IConversationResponder`; AI implements it
      (inverts today's UI→IAgent dependency) [L17716].
    - D6: exactly **one** `role:responder` connection per Conversation (MVP); zero/multiple = loud
      topology error [L17743].
12. **Assistant turns are durable jobs**, independent of the HTTP/SSE connection — a dropped browser
    must never decide whether the brain finishes its work [L18843–18845]. Not a second job framework:
    the Execution module is the one durable-execution mechanism [L18849].
13. **Identity & authorization** [L9791–10091]:
    - ASP.NET Core Identity at the Host boundary owns credentials/sessions (username/password;
      passkeys rejected for now [L10095–10111]); Orleans grains own workspace membership, roles,
      invitations, audit [L10070–10091].
    - Installation-local accounts; HTTPS mandatory the moment access extends beyond localhost [L10025–10038].
    - Roles: Owner/Admin, Builder, Viewer; workspace = the security boundary (per-dashboard ACLs
      deferred) [L9986–9998].
    - Durable commands persist the actor stamp; `RequestContext` alone doesn't survive
      reminders/retries/restarts [L9770].
14. **Integrations** (ratified naming: **Connection** = in-brain graph relationship; **Integration** =
    external system account) [L10164–10175]:
    - Strictly **per-user** OAuth in ordinary chat (Gmail AND Salesforce); workspace-scoped
      integrations only when an approved workspace behavior explicitly names them; **no silent
      credential fallback** [L11985–11997].
    - **All MCP tools are allowed** — `tools/list` is the executable catalog; no read/write
      classification, no approval middleware, no policy interface; provider permissions + per-user
      OAuth are the only authorization boundaries; the existing destructive-tool rejection is to be
      **removed** [L12625–12638].
    - **Migrate Gmail to the official Google Gmail MCP server** and delete the typed Gmail path
      (planner/token-store/auth-rail) only after parity [L12654, L13012–13017].
    - Publishing a dashboard **never transfers personal credentials**; unresolved integration
      dependency → refuse activation [L9971–9978].
15. **Surfaces & sharing**: dashboards/surfaces are personal-first; explicit **"Publish to
    workspace"** creates a workspace-owned revision; later private edits never auto-change the team
    version [L9929–9998]. Surface = durable declarative UI document — never a MAF Executor, never
    Flutter widgets emitted per run [L31420].
16. **MAF (Agent Framework 1.17.0)** is an *inner* engine, never the product boundary:
    direct agent loop for simple chat; optional workflow inside one Behavior revision; MAF
    checkpoints via an Orleans `ICheckpointStore` adapter; **Orleans-owned execution** — no Durable
    Task Scheduler / MAF Durable Extension unless Orleans-backed execution is proven insufficient
    [L31297–31305, L31413–31422].
17. **Self-aware engineering** (vision, deferred from current scope): a read-only Engineering
    control plane over logs/traces/metrics — deliberately excluded from the stabilization slice [L8582].
18. **SDK = the shared capability rails** (owner amendment, 2026-08-10, ratifying the original
    opening intent [L3]): commonly reused mechanics live once in the SDK and every provider module
    builds on them —
    - **Authorization/OAuth rail** (already ratified: used by Google, Salesforce, and any future
      integration) [L3, L8457–8459];
    - **Webhook ingress rail**: external events enter the brain as ordinary facts on the
      ConnectionGraph — "when Elon Musk posts on X" = webhook receives event → X module emits a
      generic fact (e.g. `x.post-created`, module *vocabulary*, not a case-specific operation
      synapse) → ConnectionGraph routes → Behavior executes. The existing webhook slice is
      therefore **characterized and kept/stabilized as an SDK rail, not deleted**;
    - **Security/state-protection primitives** (AES-GCM envelopes, purpose-separated key
      derivation — today's `DigitalBrain.Security`) as a shared rail; exact project placement is an
      implementation decision, the *capability* is ratified.
    Principle: the SDK exists so new integrations (X/Twitter, future SaaS) are thin adapters over
    shared rails, naturally aligned with the neuron/synapse paradigm — the paradigm is never
    bypassed and abilities are never limited by it.

---

## 2. What DigitalBrain IS NOT

1. **Not a mass-market SaaS.** No millions of users, no Entra/External ID multitenancy, no
   passkey-first onboarding (for now) [L8990, L9829, L10111].
2. **Not a demo.** Hardcoded, case-specific behaviors are banned: the `WantsTimeButton`/`ShowTime`
   keyword god-switch must die [L3, CLAUDE.md trap #9]; no provider/action-specific synapses
   (`SalesforceReadSynapse`, `GmailReadSynapse`, …) ever [L12013, L32272].
3. **Not a single-owner local toy.** Codex's "single-owner, local-first, one trusted machine"
   framing was explicitly **rejected** by Vlad in favor of the shared team workspace [L8986–8990].
4. **Not a second workflow/job framework.** No parallel job system beside Execution [L18849];
   MAF Workflows run *inside* Executions, they don't replace them [L23821]; MAF workflow edges are
   **never** mirrored as Connections [L23874, L31414]; the full flexible workflow engine (wait sets,
   child executions, generic signaling) is out of iteration scope [L23583–23585].
5. **Not MAF-shaped at its core.** MAF-workflow-as-universal-behavior-model was rejected [L32343];
   MAF's YAML schema is not a product storage format [L34537]; MAF checkpoints don't own side-effect
   accounting — DigitalBrain does [L34521].
6. **Not a runtime assembly loader.** Loading generated Roslyn assemblies into the credentialed
   silo = RCE; generated code belongs in a killable out-of-process worker (final invariant still
   pending ratification, see §4) [L34539, L34759–34764].
7. **Not built on Orleans Streams for the interconnect.** Azure Queue streams are at-least-once,
   non-rewindable, non-FIFO — weaker than the durable synapse outbox; outbox traffic never moves
   onto them (Streams/PubSub deletion allowed only after proof of non-use) [L8464, INTERCONNECT-REVIEW].
8. **Not a federation platform (yet).** No brain-to-brain sync, no portable cryptographic identity,
   no per-dashboard ACLs, no cross-workspace live sharing — all deferred [L9877, L9894, L9998, L9916].
9. **Not a big-bang rewrite and not cosmetic consolidation.** Both delivery strategies were
   rejected in favor of Stabilize-and-strangle [L8426, L8445–8447].
10. **Not IntoChat.** DigitalBrain never depends on IntoChat; integration is optional and later [L10011].

> **Reading rule (owner amendment)**: every exclusion above constrains *mechanics and boundaries*
> (how things are built), never the system's *abilities*. Anything expressible as facts +
> connections + behaviors over SDK rails is in scope — including external-event-driven automation
> like reacting to a new X post via webhook. If an exclusion ever appears to block a capability,
> that is a design question for the owner, not a reason to refuse the capability.

---

## 3. Ratified delivery strategy — "Stabilize and strangle" [L8576]

Chosen over big-bang rewrite and cosmetic consolidation [L8445–8447]. Ratified sequence [L8582]:

1. **Define the supported product boundary** (done above, §1–§2).
2. **Characterize the current production source, routes, and observable failures.**
3. **Replace defective seams one at a time** with the smallest coherent production change.
4. **Adversarially inspect every diff, then build/static-check it**, keeping the product runnable.

Initial exclusions from the stabilization slice (ratified) [L8582]: Behavior build-out, Engineering
module, graph renaming, project consolidation. They return as later strangle iterations.

### P0 defects (from the session's evidence)
| # | Defect | Where |
|---|--------|-------|
| P0-1 | OAuth state flow: two different states created (manual URL in `McpAuthorizationRail` vs MCP library in `McpClientSessions`); manual URL lacks PKCE; unknown callback states fill an unbounded static dictionary; completed codes stay replayable | [L8434, L8457] |
| P0-2 | Whole AI run tied to the POST request's cancellation token (`MapOwnerCommands.cs:109`) — browser refresh kills the turn after the user message persisted; no durable pending/failed turn | [L18824] |
| P0-3 | Singleton `"dev"` owner: host builds one `IDigitalBrain`; no workspace selection, no caller identity; every HTTP/SSE endpoint unauthenticated | [L9729] |
| P0-4 | `/owner/commands` accepts client-supplied `chatName` → two logins share the "main" transcript and "assistant" | [L13019] |
| P0-5 | MCP/Gmail tokens keyed by neuron identity, not verified principal; OAuth callback state not bound to the local user | [L11968–11970] |
| P0-6 | `DirectAgentSession` persists the MAF session only after streaming completes — mid-stream crash loses progress; fingerprint drift requires explicit migration/reset | [L23851] |
| P0-7 | Generic MCP gateway rejects ALL destructive tools — ordinary work is impossible today (removal ratified) | [L11971, L12625] |
| P0-8 | Execution/Tasks machinery: receipts+operations unbounded; `OutcomeUncertain` has no resolution path; operation identity includes `AttemptId` so retries can't suppress duplicate side effects; AI worker methods unimplemented (`GroupChat.cs:50`) | [L23562–23566] |
| P0-9 | Solution cannot rebuild while AppHost runs (60 file-copy errors) — no quiesce/rebuild/restart path | [chunk1: health baseline] |

### Known trash (delete/quarantine after characterization)
- `WantsTimeButton`/`ShowTime` keyword god-switch in Chat (W2) — [multiple, CLAUDE.md].
- ~~Empty `DigitalBrain.Modules.Salesforce.Contracts` project~~ **KEPT by owner amendment**: this
  project is the durable module-contract boundary for Salesforce neuron and synapse interfaces.
  The two small Salesforce Aspire projects remain independent cleanup candidates.
- Stale `docker-compose.yml` module env vars (`DigitalBrain__Modules__0..9` naming classes that no longer exist).
- Typed Gmail path (planner/token-store/auth-rail) — after Gmail MCP parity [L13012].
- Unused Orleans Streams/PubSub provisioning — after proof of non-use [L8464].
- Behavior Studio fixture demo (`seed_demo_behaviors.dart`, `BehaviorClient` against a nonexistent
  host) — quarantine as explicit demo until the real Behavior host exists [L8436–8437].
- ~~Unreferenced Webhook + token-presence slices~~ **AMENDED**: the webhook slice is NOT trash —
  characterize it and stabilize it into the SDK webhook-ingress rail (§1.18). Only the duplicated
  token-presence slice remains a cleanup candidate after characterization [L8479].
- Duplicated module catalogs: AppHost list vs `DigitalBrainComposition.ComposedModules` — two
  sources of truth [L8439–8440].

---

## 4. Formerly open questions — **RATIFIED 2026-08-10** (items 1–4), remaining open (items 5–7)

1. **RATIFIED — Grill Q2**: automatic recovery always replays the exact pinned `.cs` artifact;
   new/fixed code = a *linked* `RecoveryExecution`, the original stays immutable [L36167].
2. **RATIFIED — Grill Q1**: Behavior scripts get arbitrary control flow but **no ambient
   authority** (no direct filesystem/network/process/env; all effects cross the execution client);
   generated code runs in a killable out-of-process worker [L34759–34766].
3. **RATIFIED — FIFO turn ordering**: one active Execution per Conversation, extra turns durably
   queued; each message = durable queued turn; cancel advances the queue; different Conversations
   run concurrently [L23676–23689].
4. **RATIFIED — Prove-or-reject spike as a Stage-1 exit scenario**: source-audit the Orleans-backed
   execution path for restart / OAuth wait+resume / cancel / reconnect / duplicate submission /
   uncertain Salesforce write, then live-smoke the observable path where the local stack permits.
   Historical automated spike evidence is context only, not the current verification gate
   [L23922–23932].
5. Graph neuron rename: `ConnectionGraphNeuron` vs `TopologyGraphNeuron` [L31424] (rename itself is
   deferred out of Stage 1 anyway). — *open*
6. Preview .NET 11/Aspire preview vs stable LTS channel [L8463] — *open; keep current channel
   through Stage 1, decide at Stage-1 exit.*
7. Conversation message-history storage shape (bounded grain state vs durable segments vs
   append-only store) — deferred to its own grilling [L15039, L16671]. — *open*

---

## 5. Current repo reality check (what the plan must operate on)

- 32 C# projects (10 Kernel, 22 Modules) + Flutter `core`/`kit`/`shell` + file-based C# scripts in
  `src/Kernel/DigitalBrain.Scripting`; Salesforce Contracts remains among the module projects.
- `UNIFIED-ARCHITECTURE.md` records synapse-graph phases 0–5 as landed; `CONTEXT.md` holds the
  ratified domain vocabulary; `CLAUDE.md` documents current build/static verification and the
  9 kernel traps.
- Historical automated-test evidence remains in reports for archaeology, but production source,
  route inspection, zero-warning builds, static analysis, and live smoke are current truth.
- CI is .NET source-build-only today; Flutter production-source analysis is in the local gate.
- Stack: net11.0 preview / C# 14, Orleans 10.2.2 (+Journaling rc), Aspire 13.5.0-preview,
  MAF 1.17.0, MCP 2.1.0, xunit.v3 + Reqnroll + Testcontainers.
