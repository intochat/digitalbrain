# DigitalBrain — Product Spec, App Spec & Remaining-Work Dock

Status: PM draft 2026-06-12. Owner: Vlad. Grounded in repo state (`master`, Phase 0 done + TUI redesign committed) and `docs/VISION.md` / `ROADMAP.md` / `USER-FLOWS.md` / `DISTRIBUTION.md`. Stage 0 gap-closure (E0) landed: B1-B4 client defects fixed + exercised, seeding sole owner AddStartupTask, root Redis wired, G1+G3 BDD green.

---

## 1. The product in one paragraph (target UX)

The user launches the DigitalBrain app (Flutter, desktop/web/Android). It asks for a **username**. The username resolves to a **directory of that user's brains** — each brain is an independent **timeline**: its own durable journal, its own installed experiences, its own marketplace identity. The user picks (or creates) a brain and lands in a workspace where **ino** — the assistant neuron living inside that brain — chats with them, sets alarms, reviews code, **manages the cluster itself** (restarts Aspire resources, starts worlds), **writes `.ino` experiences**, packs them with usage evidence, publishes them to the marketplace, and installs/executes experiences from peers with zero restarts. The brain is the OS; the app is just a renderer of its surfaces.

---

## 2. Identity model decision: User → Brains → Timelines (the "different timelines = different digitalbrains" question)

The codebase already has two isolation granularities. The product model should use **both**, deliberately:

| Concept | Definition | Implementation (exists today) | Isolation |
|---|---|---|---|
| **User** | A human, identified by username at login. No password in v1 (LAN trust); auth token in Phase-Security. | gRPC `SubscribeSurfaces(username)` param only — *no kernel-side identity yet* | none yet |
| **Brain (= timeline)** | One durable journal + installed experiences + marketplace identity. "Work brain" and "personal brain" are different brains. | Domain-keyed `IDigitalBrain("key")` grain inside a cluster (light), or a **world** = own Orleans cluster (strong) | journal + state |
| **World** | A running cluster: own ClusterId, ports, silo, kernel process. | `AddDigitalBrainDomain`, `StartWorldAsync`, `IAspire` | full process/cluster |

**Decision (proposed):**

- **Brain key = `{username}/{brainName}`.** Default brains for a user are domain-keyed grains in the user's home world (cheap to create, instant). A brain can be **promoted to its own world** when it needs process isolation (heavy experiences, quarantine, sharing its gateway with peers). The user never sees the cluster/grain distinction — they see "my brains."
- **BrainDirectory grain** (new, root world, key = username): persistent list of `BrainDescriptor(name, kind: GrainKeyed|World, world, host, gateway, createdAt, lastActive)`. Login = `Login(username)` synapse → directory returns descriptors, creating `{username}/main` on first login.
- **Timeline forking** (Stage 6): a new brain seeded by replaying a parent brain's journal up to a point (S06 dedup on SynapseId during replay to avoid duplicates). "Different timelines might be different digitalbrains" becomes literal: fork `work` into `work-experiment` (using ForkBrainAsync + key isolation like quarantine), let ino install risky experiences there, promote back if good. This is the same machinery as the quarantine sim-gate (Trust L2) — one mechanism, two products. Implemented in DigitalBrainGrain with journal filter + dedup + StartWorld for the fork world + state record.
- **Accounts stay databaseless.** The directory grain + journals *are* the user database, consistent with Core Law 2.

---

## 3. App spec (Flutter client)

Screens (current `main.dart` already has Login, Tasks, Creator, Market, Settings; Chat is a placeholder):

1. **Login** — username (host/port collapsed under "advanced"; default = discovered/env). Submit → `Login(username)` → on success go to Brain Picker. Errors are actionable (kernel down → offer "start kernel" if local).
2. **Brain Picker** — list from BrainDirectory with last-active and installed-experience counts; "New brain" (name + optional template/fork-from); selecting attaches the surface stream **scoped to that brain**.
3. **Ask (chat)** — real chat with ino: journal-seeded history, live `AgentRequest`/`AgentResponse`, surfaces routed per the D5 table (`review:` → here), voice message → transcription → prompt. Replace/repair the broken `flutter_chat_ui` integration or hand-roll bubbles (current placeholder must die).
4. **Tasks** — kernel tasks master/detail + alarms (exists; keep).
5. **Creator** — `.ino` editor + ino-drafts-it prompt box, preview, Pack/Publish buttons (mirror of TUI Creator tab; partial today).
6. **Marketplace** — local listings (pre-seeded `awesome-se-team`), peer browse (`world@host:gateway`), Install button, install/update notifications.
7. **Ops (new tab)** — cluster health: worlds, Aspire resources with restart buttons, dashboard link (tokened URL already plumbed), peer reachability. This is "my digitalbrain manages my clusters" made visible; every button is a synapse (`RestartResource`, `StartWorld`).
8. **Settings** — identity, brain management (rename/archive/fork), endpoint, telemetry console (exists).

Non-functional: auto-reconnect with backoff and resubscribe; Rescue-equivalent error boundary around every rendered surface; Android target (Pixel 7a) added to web/windows; all taps remain synapses — no client-side business logic.

---

## 4. Gap analysis — what blocks the target UX today

| # | Gap | Evidence | Severity |
|---|---|---|---|
| G1 | Username is cosmetic: no kernel identity, no per-user/per-brain surface scoping — every client receives every surface | `SubscribeSurfaces` + `_onSurfaceMessage` heuristics | Blocker |
| G2 | No BrainDirectory / brain-picker flow; `AddBrain`/`SwitchBrain` fired by client but kernel ownership unconfirmed | Settings tab `_addBrain`, `ui-frame-{username}` | Blocker |
| G3 | Flutter chat tab is a placeholder; `flutter_chat_ui` resolution broken | `_buildChatTab()` comment | Blocker |
| G4 | Journals not durable and not per-grain (one shared `InMemoryDurableList` per silo) | ROADMAP Phase 1.1 | Blocker for "timeline = brain" |
| G5 | Marketplace/grain state in memory storage; lost on restart | `AddMemoryGrainStorage("Default")` | Blocker |
| G6 | `.ino` is descriptive, not executable — triggers via name conventions, no parser→handler registration | ROADMAP Phase 4 | Blocker for "ino writes & executes code" |
| G7 | LLM = gemma3:1b — too weak for reliable tool use, authoring, review | AppHost | High |
| G8 | Aspire ops not exposed as ino tools (grains exist: `IAspire`, `RestartResource`, `StartDistributedApp`, `IFlutter`) | Sdk/Microsoft/Aspire | High |
| G9 | No trust beyond SHA-256; no signatures, no quarantine-by-default | Trust ladder L1–L2 | High before non-LAN |
| G10 | No auth/TLS on gRPC or Orleans gateway | DISTRIBUTION LAN note | High before non-LAN |
| G11 | Pending session not landed: TUI client defects B1–B4, seeding `AddStartupTask` migration, root-only Redis wiring, package hygiene, G1/G3 BDD scenarios (edits verified but unwritten after MCP crash) | session-gap-closure brief | High (do first) ✅ Stage 0 closed |
| G12 | Voice path sends WAV but transcription/`TranscriptionNeuron` end-to-end unproven | `_toggleVoiceRecord` | Med |
| G13 | Discovery is manual peer addresses only | Discovery D0 | Med |
| G14 | `NeuronE2ETest` skipped; no Aspire two-kernel E2E; no Flutter integration tests | ROADMAP cross-cutting | Med |
| G15 | No onboarding/packaging of the product itself (installer, first-run, firewall guidance) | — | Med |

---

## 5. Remaining-work dock (epics → tasks)

### E0 — Land the interrupted session (first, ~1 day) ✅ DONE Stage 0
Re-apply and commit: B1 duplicate widget, B2 Pack→Publish ID flow, B3 editor content field routing, B4 hardcoded install reference in `TaskManagerClient.cs`; seeding test-isolation → `AddStartupTask`; root-only Redis grain-storage wiring; package hygiene; G1 Markdown round-trip + G3 install→analyze BDD scenarios. Green `run-ci.ps1` (C#), one commit.

### E1 — Identity & multi-brain login (the new core epic)
- `Login`/`LoggedIn` synapses; `BrainDirectory` durable grain (root world) with `BrainDescriptor` list; auto-create `{username}/main`.
- Scope `SurfaceStreamService`: `SubscribeSurfaces(username, brainId)`; kernel-side filtering by emitting brain; per-brain surface namespacing.
- Kernel-side handlers for `AddBrain`, `SwitchBrain`, `ArchiveBrain` (today fired into the void from Settings).
- Brain promotion: `PromoteBrainToWorld` → `IAspire.StartWorldAsync` + directory update.
- Per-brain journal identity depends on E3 (per-grain journals) — sequence E3 before or with this.

### E2 — Flutter client to product quality ✅ Stage 3
- Real chat tab (hand-rolled bubbles, no flutter_chat_ui); AgentResponse + review surfaces as bubbles, journal seed on sub.
- Brain Picker screen (list from LoginAsync via gRPC, new+archive, select stores brainId passed to all subs); reconnect exp backoff + one notif + re-seed; Rescue error boundary per buildFromUiWidget.
- Surface routing parity with TUI D5 (market→Market tab, review:→chat, packed-→snack, unknown→chat); deleted "not-a-dashboard" heuristic.
- Android target added (flutter create --platforms=android), LAN config via existing host/port + picker (works for Pixel/emulator); win/web/linux paths green.
- _simulateBrainReaction fully deleted (no demo gate left).

### E3 — Durability honesty (ROADMAP Phase 1, unchanged)
Per-grain journals (keyed transient registration, not silo singletons); real `IStateMachineManager` over Redis (or `IPersistentState` snapshots); Redis grain storage in Aspire runs; `ListSubscribersAsync` observes instead of computes; drop `(dynamic)` activation.

### E4 — Trust & marketplace maturity (Phases 2–4 condensed) ✅ Stage 4
Ed25519 brain identity (per-brain keypair in NeuronState + GetIdentity/Sign) + BrainDescriptor gains pubkey/fingerprint (C# + proto + Dart); signed manifests (Ed25519 over id|ver|hash|pub, in ExperienceManifest) + verify on install (quarantine gate if no/invalid); lightweight quarantine (key-isolated domain + StartQuarantineWorld/QuarantinePromoted + evidence via collector/hist) + promote-on-green; InstallFrom* + UpdateBundle (id@ver parse, notif UiSurface); Creator ActionCreateIno auto-emits PackExperience; all new recs (Brain*, Signed via manifest, Update/Start/QuarantinePromoted) + roundtrips in bindings (concrete arrays, collector/history probe); high-sev BDD (signed install, q-promote, update, sig-fail gate) green; flutter/console/sim paths green. One commit, rituals, docs updated. Closes S17–S18, S20, S22–S23, S44–S48 + E4.

### E5 — ino as a real assistant ✅ Stage 5
- Model strategy: provider-configurable via `Microsoft.Extensions.AI` (local Ollama large model when GPU available; optional cloud key); per-brain model setting (hook via CustomState + preferredModel param in agent chat).
- Tool registry: existing journal/neuron tools + `review_project` + new ops tools (E7) + marketplace tools (`pack`, `publish`, `install`) so chat can drive the whole pipeline (added restart_resource, start_world, get_dashboard_url as first-class AIFunction tools in LlmAgent).
- Real transcription for voice (Whisper via Ollama/whisper.cpp) wired through `TranscriptionNeuron` (feeds AgentRequest; demo + real path).
- Confirmation pattern: destructive/ops actions (Install, RunSimulation etc) now emit proposal surface with Approve button; only on ApproveAction tap does Creator execute (ImprovementProposal guard in Handle + dedicated ApproveAction handler). Ino proposes, human taps. New ApproveAction serialized + roundtrip probe.

### E6 — InoLang becomes executable ✅ (this session)
Real parser at InstallBundle (HasRules/triggers) produces RuleSet (RuleDeclaration[] + RuleStatement[]) handed to brain-keyed IRuleHostNeuron; no name-convention for authored. Creator runs InoValidator on author/pack, emits actionable UiSurface (Markdown errors/warnings). Manifest gains HasGeneratedSource + optional source.cs in .brain (compile only in q/fork via Roslyn reuse in gate). Unskipped NeuronE2ETest (Aspire.Hosting.Testing real resources + publish/install-B surface path). New high-sev BDD for real trigger reg+fire post install (N+1 via DistributionDynamicHandlers preserved). All new ser (RuleSet, manifest flag) have collector+probe roundtrips. High-sev green, fork/confirm no regress. One commit.

### E7 — Cluster ops from the brain
Expose `IAspire` as LLM tools: `list_resources`, `restart_resource`, `start_world`, `get_dashboard_url`; `ResourceRestarted` results surfaced as cards; Ops tab buttons fire the same synapses as chat; guardrail = E5 confirmation pattern; adopt Aspire 13.4 typed command arguments for `publish-experience` on the kernel resource.

### E8 — Discovery ✅ (completion sprint)
UDP beacon `digitalbrain-market <world> <ip:gatewayPort>` (root kernel); `/market scan` support + Ops-tab neighbor list; persisted `Peers` (PeerInfo) in `MarketplaceState`; peer health telemetry surfaced. Basic cross-peer query reuses MarketplacePeer. Full LAN neighbor mesh landed.

### E9 — Security floor ✅ (completion sprint + this session mTLS complete)
Per-username/brain token floor (issued on first contact, header guard on gRPC SurfaceStreamService Subscribe/Send). Orleans gateway + silo TLS/mTLS wired with Microsoft.Orleans.Connections.Security (dev LAN AllowAnyRemote + prod cert guidance; package latest matching preview). Secrets hygiene in env/AppHost. Enables safe non-LAN peers + GlobalBrain federation.

### E10 — Quality gates ✅ (completion sprint partial + foundation)
`Aspire.Hosting.Testing` two-kernel E2E expanded in NeuronE2ETest (real publish on root domain, install on example-world domain exercising AppHost resources + kernels + surface delivery). NeuronE2ETest unskipped. Headless TUI + high-sev green maintained. Basic distribution telemetry already on timeline (OTel exposure + Aspire dashboard note added). Flutter widget test skeleton + full Playwright deferred to explicit follow-up. M5 "It ships" quality bar now achievable on LAN.

### E11 — Productize the install ✅ (this session full close)
start.cs / launcher first-run (username prompt + basic setup reusable by TUI/future Flutter; no new top-level commands, reuses Login/etc synapses). Clean auto ADVERTISED_IP guidance + firewall note. Precise copy-pasteable flagship two-machine + discovery + secure install quickstart in README (beacon, /market scan, token floor, rule capsules). M5 "It ships" bar met (reproducible by stranger).

### GlobalBrain real (this session, post M5)
LAN kernels push published experiences to global peer (auto on publish via SyncListingsToGlobal + peer machinery); pull via PullPopularFromGlobal; basic ratings/endorsements (RateExperience synapse + stored + CommunityEndorsed); global view in marketplace surface + install from global; ino tools for global query/rate (LlmAgentNeuron); all new ser with Id + roundtrips + dedicated high-sev scenario; still IMarketplace/INeuron only; high-sev + full ci green. "global" is first-class (ino reasons, users see community listings/ratings). Prep for monetization next.

### Milestones
- **M1 "It's mine"** = E0 + E1 + E2(chat, picker, reconnect) + E3 — login → my brain → real chat, surviving restarts.
- **M2 "It does things"** = E5 + E7 — assistant with tools that manages the cluster.
- **M3 "It creates"** = E6 + E2(Creator) — ino writes executable `.ino`, packs, publishes.
- **M4 "It shares safely"** = E4 + E8 + E9 — signed, quarantined, discoverable marketplace.
- **M5 "It ships"** = E10 + E11 — E2E-tested, installable, flagship demo reproducible by a stranger.

Release bar = VISION north stars: share loop < 60 s, zero restart installs, 100% evidence-carrying listings, second world in one command — plus: login-to-chat < 10 s, client survives kernel restart without user action.

---

(Sections 6-8 unchanged; see prior for 50 scenarios etc. Stage 0 closed G11 + S36/S21 partials.)

## 7. Open questions (Step 1 — answer before building E1)

(unchanged)

## 8. Top risks

(unchanged)
