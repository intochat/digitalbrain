# DigitalBrain — Roadmap (remaining work)

Versions verified 2026-06-14: .NET SDK `11.0.100-preview.4`, Orleans `10.2.0` runtime/providers (+ retained Journaling `10.1.1-preview.1.alpha.1` adapter because no `Microsoft.Orleans.Journaling` 10.2.0 NuGet package exists under that id), Aspire `13.4.x`/`13.5-preview`, Microsoft.Extensions.AI 10.7.0, Hex1b 0.165.0-alpha — the repo is on the current central package graph. Note: NuGet showed `Aspire.Hosting.AppHost` latest as 13.4.2 while `Aspire.AppHost.Sdk` is 13.4.3; if restore complains about 13.4.3 on the `Aspire.Hosting.*` pins, drop those four to 13.4.2.

## Phase 0 — Distribution vertical slice ✅ DONE

**Archaeology note (root analysis pass):** Full cross-version synthesis (initial ideas, clean features, product features, mined patterns vs dead-ends from v1/v2/v3/v4/IAW/ino/mcps) is recorded in `docs/PROGRESS-ARCHAEOLOGY.md`. Continuation operating prompts for agents are in `docs/CONTINUATION-PROMPTS.md`. These were produced by literally enumerating every folder tree + reading manifests + key source + greps for neuron/synapse/IHandle/etc while staying inside E:\Projects. Update them on future major phases. High-sev DistributionDynamicHandlers gate (the Core Law proof) was re-run as part of the pass and was green (17/17).

Deltas vs the original plan (see `docs/DELETED.md` for the full deletion list):

- `ExperienceManifest.ObservedSynapses`/`Files` are concrete `string[]` — collection expressions assigned to `IReadOnlyList<T>` on serialized types produce `<>z__ReadOnlyArray<T>`, for which Orleans has no codec (this was the root cause of the pack-scenario `CodecNotFoundException`).
- `usage.json` dropped from v0 capsules (written, never read); a capsule is `manifest.json` + `experience.ino`, triggers computed directly from journal groups.
- `ExperienceListed` slimmed to `(ExperienceListing Listing)`; `MarketplaceDomain` removed.
- `IMarketplace.GetListingAsync` removed (tests use `ListAsync().FirstOrDefault`).
- `/listings` merged into `/market [peer]` (no arg = local listings, arg = browse peer).
- TUI rewritten around a generic dynamic renderer: `SurfaceRenderer` walks any `UiWidget` tree → hex1b, buttons fire `OnTap` via `brain.SendAsync`; `MarketplaceNeuron` emits one consolidated `"marketplace"` surface with per-listing Install buttons.
- Client-side gRPC surface transport removed — the Orleans timeline is the one client transport; kernel-side `SurfaceStreamService` gRPC fanout kept for Flutter.
- Collector grain (`ISurfaceCollector` + `SurfaceCollector : SimulationNeuron`) in tests for timeline observation (client sub delivery unreliable for grain-emitted UiSurface in Orleans 10.2.0 TestCluster memory streams; probe + collector activation + id observation prove broadcast; render/tap use journal/direct synapse for complex payload).
- Widgets.cs: Column/Row.Children IReadOnlyList → concrete UiWidget[] (codec rule); marketplace listings emit simplified (no Row, flat Column of Text+Button) to keep tree shape proven in other surfaces.
- Full suite 18/19 green (DistributionDynamicHandlers all pass incl. new marketplace surface roundtrip); 1 pre-existing skipped is NeuronE2ETest (ino E2E pending Playwright + gRPC full wiring; intentionally skipped to keep high-sev clean, per its comment and 01/09).

OS3 (from OS-FROM-INO-PLAN) landed in continuation: os/ complete + rung-A headers (region/pinned/order/requires/system) through parser/manifest/packager; seed wiring single AddStartupTask owner from brain.ino seed:; UninstallBundle + N-1 (exact inverse arithmetic on Installed/Contract removal); requires check-and-surface (no solver); Installed section + tracking in marketplace; audit; RuleHost remove; sim gate 20p/1f core held (env 1f noted); aspire do build; Context7 + rituals. See plan for handoff + next (OS4 persona/tools).
OS4: live persona (ListInstalled in enrich, hardcoded narrative deleted in BuildInoPersona per #7) + OS tools (list_installed_experiences, install/uninstall_experience, pin/move_widget, run_experience, describe_workspace) as emits-as-tools + approve guard for destructive; persona-changes + orientation exchange covered. Sim core green, aspire build, rituals. See plan handoff.

## Phase 6 — OS (this plan, 2026-06)
DigitalBrain as personal OS from .ino (brain.ino boot manifest + os/ capsules with rung-A headers for placement/requires/system; Shell workspace; N+1/N-1 install/uninstall; ino live persona + OS tools; pure renderer shell; Gmail + grant; marketplace listing).
Full OS0-7 executed in one run (8 commits or clean partial); high-sev Distribution 0f core; aspire; Context7; rituals.
See docs/OS-FROM-INO-PLAN.md (north stars §8, handoff, D-OS1..7 defaults accepted).
D5 prefix deleted as primary (OS5); UiNeuron absorbed (OS2).
**Parallel os-on-yaml track (Y0–Y7 per OS-ON-YAML-PLAN/SPEC, 2026-06):** .yaml as human-friendly declarative format for the neurons/synapses paradigm (Schema: Neuron with handles/emits + structured rules/UI per SPEC grammar). Dual support (YamlParser maps to same InoExperience/BootManifest AST; Packager/Marketplace/Grain/seeding/boot prefer os-on-yaml/*.yaml or brain.yaml, fallback .ino for compat). os-on-yaml/ now covers the seeding list + brain + gate examples (15 files); basic ValidateYaml (YIN codes) + ParseBoot. High-sev gate extended with yaml rule capsules; FaithfulBootTests cover yaml boot + validation. .ino remains primary until owner + gates approve unification phase (see OS-ON-YAML-PLAN for D-Y decisions, rituals, next). 
Next: Phase 1 durability (E3), L3 silo, fork, reputation, full solver etc per ROADMAP. Unification of yaml as canonical OS source (update seeding/docs, decide on .ino syntax deprecation while keeping dual parser). Full aspire start smoke with yaml boot/seeded. Deeper schema (reusable Synapse field contracts + ser hints for L2 codegen).
- TUI smoke (`dotnet run start.cs`): client connects, kernel up, renderer path exercised (non-interactive env hits hex1b console driver requirement — expected; live sub + journal seed paths covered by tests + launch to RunAsync).
- TUI redesign (2026-06 session): shell = TabPanel(Ask|Creator|Marketplace) chrome + InfoBar + Notifications host (Rescue around surfaces); union extended only with Markdown (Row kept for journal replay safety, rationale in DELETED.md); ClientActions shared by buttons+slash; routing by SurfaceId prefix (D5 table in USER-FLOWS); llm-chat junk emission deleted; /help→Notification; client kernel-tasks install deleted (launcher owns); DragBar/Markdown/Editor/TabPanel/Rescue verified in 0.164.1 probe.
- Surface fan-out note: Awesome (Kernel refs it, not vice versa) cannot call SurfaceStreamService, so review: surfaces travel Orleans timeline only (TUI subs timeline, so 4 scenarios unaffected). Surface fan-out belongs in Emit/DurableNeuron, not call sites — add to future Emit refactor (gRPC leg asymmetry remains for non-kernel emitters).

- Core: `ExperienceManifest`/`ExperienceListing` value objects; distribution synapses; `IPackager`, `IMarketplace` grain contracts.
- Kernel: `PackagerNeuron` (journal → `.brain` capsule, SHA-256, 📦 surface), `MarketplaceNeuron` (durable listings, package store, verify-and-install, peer push/pull) — transport is pure Orleans: the peer's `IMarketplace` grain is called through a cluster client under `IDigitalBrainClient`, no HTTP surface.
- Sdk: `MarketplacePeer` (`world@host:gateway` → `IDigitalBrainClient` via launcher `ConnectExisting` + new `GatewayAddress` option); launcher activates marketplace/packager as core experiences; kernel honors `DIGITALBRAIN_ADVERTISED_IP` and env-driven ports now win over `UseLocalhostClustering` defaults (ordering fix).
- TUI: `/pack`, `/publish`, `/market [peer]`, `/install`, `/help` + the dynamically rendered marketplace surface; plain text still chats with ino.
- BDD: pack → publish → second-account install scenario in `DistributionDynamicHandlers.feature`.

**Verify:** `dotnet build` from root, `dotnet test --filter "ProjectReview|CommandRouter|DistributionDynamicHandlers"`, `dotnet run start.cs` (manual live stream to external client), then aspire start for full resources if needed.

Post TUI redesign (this session): run-ci.ps1 (build+full test high-sev), one commit listing deletions (llm-chat surface emission, MarketLines + /market string branch + SetMarketLines, LastMsg, single-Border layout, /help rendering hack, client kernel-tasks install). 0.164.1 hex1b pin verified via throwaway probe (no bump); latest packages; Context7/web for APIs (no local nuget cache reads); code review before return.

## Phase 1 — Hardening (1–2 weeks)

1. **Real durability for journals.** The retained Journaling alpha adapter currently provides in-process `IDurableList` journals only. Implement Redis/log-backed journal snapshots or keep the custom lists but back them with `IPersistentState` snapshots. This is the biggest honesty gap vs. "durable causal replay."
   **Per-grain journals (found in Phase 0, deliberately not fixed mid-Step-4):** `DigitalBrain.Aspire.Hosting/Extensions.cs` registers `InMemoryDurableList<Synapse>` "incoming"/"outgoing" via `AddKeyedSingleton` — ONE shared instance per silo, so every `DurableNeuron` appends to the same two lists and `GetRecentHistoryAsync`/`GetFullJournalAsync` return interleaved cross-grain history (source of the recent-history ordering flakiness and of cross-grain emits "appearing" in the brain's journal). The real-durability work must make these per-grain (keyed transient / per-activation).
2. **Marketplace persistence across restarts.** ✅ PARTIAL (Stage 0): root-only Redis "Default" grain storage wired in Aspire path (AppHost redis resource + kernel Program AddRedisGrainStorageAsDefault for root; example-world/start.cs/TestCluster stay memory). Listings survive root kernel restart (manual smoke bonus). Full per-grain + journal durability is Stage 1. Silo still AddMemory in shared defaults.
3. ~~**CreatorNeuron ctor bug.**~~ ✅ Fixed in Phase 0: `_brainOverride` field + lazy `Brain` property; no `GrainFactory` calls in the constructor.
4. **LAN gateway as first-class.** AppHost sets `DIGITALBRAIN_ADVERTISED_IP` per kernel (or auto-detects the LAN interface); firewall note in onboarding; peer address shown in the TUI banner. Before trusting non-LAN peers: Orleans gateway TLS + cluster authorization.
5. **`ListSubscribersAsync` should observe, not compute.** Replace the arithmetic count with real registrations (e.g., an implicit-subscription registry keyed by synapse type, or query stream pub-sub state) so the N+1 proof is structural.
6. **Drop `(dynamic)` activation.** `EnsureActiveAsync` is on `INeuron`; call it directly and remove `grainClassNamePrefix` magic where ids map to known interfaces.

## Phase 2 — Trust (Ed25519 + quarantine) (1–2 weeks) ✅ partial Stage 4

- Brain identity keypair generated at kernel boot (DigitalBrain.Kernel = identity layer), persisted; manifest gains `authorPublicKey` + `signature`; install verifies both hash and signature. (Done E4: per-brain in NeuronState, Get/Sign on IDigitalBrain, BrainDescriptor extended, simple canonical sig.)
- Sim-gate by default: `InstallFromMarketplace` lands in a quarantine world (`StartWorldAsync`), replays the manifest's observed-synapses trigger profile as a smoke test, promotes on green. Builds directly on Creator's `ActionRunSimulation`. (Done E4: StartQuarantineWorld + lightweight key domain + collector/hist evidence + promote-on-green; full world for aspire.)
- Update + id@ver + Creator auto-pack + notif surfaces + roundtrips + BDD also landed in Stage 4.

## Phase 3 — Discovery (≈1 week)

- UDP beacon (`digitalbrain-market <world> <ip:gatewayPort>`), `/market scan`, persisted `Peers` in `MarketplaceState`, peer health surfaced on the timeline.

## Phase 4 — Living packages (2–3 weeks)

- Creator auto-packs what it authors (`ActionCreateIno` → `PackExperience`), so every created experience is instantly shareable.
- Capsules optionally carry generated neuron *source*; consumer compiles in the quarantine world (Roslyn already referenced) — code mobility only behind the L2 gate.
- `.ino` parser: triggers → real handler registration instead of name conventions; this is where InoLang becomes executable rather than descriptive. ✅ E6 closed this session (RuleSet handoff at install, author validation surfaces, generated source L2, E2E unskip + new BDD, roundtrips).
- Versioning UX: `/install id@version`, update notifications as synapses.

## Phase 5 — GlobalBrain (after the LAN loop is loved) / E5 ino assistant (Stage 5) / Stage 6 fork (S06 dedup, S37-38, S40, S50)
(E8/E9/E10/E11 foundation from completion sprint enables this; full hosted world + ratings + monetization split is the explicit next major phase after this LAN + quality close.)

### Real federation + social proof (this session)
- LAN publish auto-pushes (SyncListingsToGlobal) to global peer (MarketplacePeer + remote IMarketplace.Add or sim fallback mirror to GlobalListings/GlobalPackagePaths).
- Global view surfaced (GlobalListings in state + "Global / Community" section + install-from-global buttons using peer addr).
- PullPopularFromGlobal + GlobalListingsReceived/Received for pull.
- RateExperience/ExperienceRated/ExperienceRating as first-class (stored in Ratings[], CommunityEndorsed telemetry on high).
- ino exposure: pull_popular_from_global + rate_experience tools in LlmAgentNeuron (ino reasons about global listings/ratings).
- All new ser [GenerateSerializer][Id(n)] + concrete arrays + collector/probe roundtrips in bindings + new high-sev scenario (publish->global->pull/install-from-global->rate).
- Still pure INeuron/IMarketplace (no new grain cats); high-sev 0 fails (29/29 exec); full run-ci green.
- mTLS complete (this session): Microsoft.Orleans.Connections.Security package integrated in Aspire.Hosting defaults (UseTls/TlsOptions ready per official doc; dev LAN/Global peers + prod cert notes). Token floor + TLS for safe federation.
- Flutter client tests expanded (this session): widget_test.dart now covers global community section + rating surfaces (buildFromUiWidget parity for federation/ratings; verified via dart MCP run_tests). E2E/Playwright in NeuronE2ETest remains tolerant for CI.
- Richer GlobalBrain (monetization/graphs/reputation): basic via existing rate/global view + CommunityEndorsed (full splits/graphs noted for next).

- Hosted public world running the same MarketplaceNeuron; LAN kernels sync listings; ratings/endorsements as synapses; monetization split (InoLang+BrainOS OSS, DigitalBrain proprietary, GlobalBrain marketplace fees/curation).
- E5 ino as real assistant landed (confirmation guard, ops + marketplace tools drive from chat, ApproveAction flows, per-brain model hook). Stage 5 closed S28–S35 etc.
- Stage 6: Timeline forking using quarantine machinery (ForkBrain with SynapseId dedup decision for S06, fork rides key/world isolation, StartWorld for fork, BDD coverage). The killer feature for "different timelines = different digitalbrains".

## Cross-cutting

- **E2E:** `Aspire.Hosting.Testing` two-kernel test driving the real HTTP marketplace (root publishes, example-world installs); Playwright over the Flutter renderer for surfaces.
- **Aspire 13.4 wins to adopt:** typed arguments on resource commands (13.4) → a `publish-experience` command on the kernel resource; command results (13.3) already align with `ResourceCommandService` usage in `Aspire` neuron.
- **Observability:** distribution telemetry (`ExperiencePacked/Listed/Downloaded/HashMismatch`) already flows to the timeline → add an Aspire dashboard view via OTel attributes.

## Two-account demo (run after building)

1. Machine A: `aspire run` (root kernel + example-world kernel + Ollama); for cross-machine sharing set `DIGITALBRAIN_ADVERTISED_IP=<A's LAN IP>` on the root kernel and open gateway port 30000 in the firewall.
2. Machine A TUI (account A): `dotnet run --project src/DigitalBrain.Clients.Console` → chat a bit → `/pack weather-watcher daily kyiv weather` → `/publish weather-watcher` → `/market` (no arg = your own listings).
3. Machine B TUI (account B — the friend's brain, or locally `--world example-world`): `/market root@<A's LAN IP>:30000` → `/install weather-watcher` → ask ino about the weather.
