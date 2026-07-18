# DigitalBrain — User Flows

Status legend: ✅ works today · 🔨 this milestone (implemented in this change, needs build/verify) · 🔭 planned.

Each flow lists the actor, the path through the system, and the synapses that carry it.

---

## A. Living with the brain

### 1. First boot ✅ (E11 productize enhanced)
Vlad runs `dotnet run start.cs` (fast in-memory world) or `aspire run` (root + example-world kernels, Ollama, Flutter).
- Minimal first-run wizard (username prompt if unset; default root; sets per-brain key convention). Reusable by TUI + (future) Flutter login.
- Guidance printed for DIGITALBRAIN_ADVERTISED_IP (LAN peers via beacon) + firewall (open gateway port).
Silo starts → core experiences activate (`Activated` on timeline) → TUI opens with kernel tasks + chat.
Synapses: `Activated`, `UiSurface("kerneltasks")`, optional Login/BrainAdded for identity.

### 2. Chat with ino from the TUI (Ask tab) ✅
In the Ask tab chat box (or /command) → `AgentRequest` broadcast → `LlmAgentNeuron` enriches + tools (incl review_project) → `AgentResponse` → TUI appends live in Ask; review: surfaces routed here and rendered via Rescue+Markdown. History from journals.
Synapses: `AgentRequest`, `ToolInvokeSynapse/ToolResultSynapse`, `AgentResponse`, `AgentOutcome`, `ReviewProjectRequest` (for project analysis), `UiSurface("review:...")`.

### 3. Ask ino about the brain itself ✅
"what's active right now?" → ino calls `list_active_neurons` / `list_subscribers` / `get_brain_full_journal` tools → grounded answer referencing real state.

### 4. Set a reminder from chat ✅
"set alarm in 10 mins wake up" → `SetAlarm` → `KernelTaskSupervisor` registers a real Orleans reminder → `AlarmSet` + alarm `UiSurface` card; on fire: `AlarmFired` + fired card. Survives re-activation (`ReEmitActiveAlarms`).

### 5. Inspect and drive kernel tasks ✅
Arrow/Enter on a task in the TUI → `InspectKernelTask` → supervisor emits detail `UiSurface`. Widgets are the source of truth; TUI and Flutter render the same tree.

### 6. Live a daily habit with an experience ✅
Friend-shareable behavior accrues evidence simply by being used: every `WeatherQuery`/`WeatherResult` lands in durable journals. This usage tape is what packing later consumes.

---

## B. Pack → publish → share (the core loop)

### 7. Pack an experience after regular use (Creator tab) 🔨
In Creator tab: edit .ino (multi-line), preview (Markdown), Pack button (or `/pack id desc`) → `IPackager.PackAsync` (from journals or authored) → capsule + 📦 surface (routed packed- to notif). Buttons and slash share ClientActions.

### 8. Publish to the local marketplace (Creator/Market tabs) 🔨
Publish button or `/publish id [peer]` → `PublishToMarketplace` → `MarketplaceNeuron` lists → consolidated `"marketplace"` surface (routed to Market tab). Peer query from Market tab populates local peer listing state (labeled peer results) + Install-from-peer. /help → Notification.

### 9. Friend browses your marketplace over LAN 🔨 (E8 enhanced)
On machine B: `/market root@vlads-pc:30000` or `/market scan` → UDP beacon + persisted Peers + peer health. `MarketplacePeer.ConnectAsync` + ListAsync shows listings. Discovery + scan now real (E8 completion sprint).

### 10. Friend installs your experience 🔨
`/install weather-watcher` → B's `MarketplaceNeuron.InstallFromPeerAsync` pulls the `.brain` bytes through the peer cluster client, re-hashes `experience.ino` against the manifest, saves to `pa-files/downloads/`, emits `ExperienceDownloaded(HashVerified)`, then `InstallBundleAsync(id, SourcePathOrUri: ino)` on B's brain → `BundleInstalled` broadcast → **N+1 handler growth on B with zero restart**.

### 11. Friend uses what you lived with ✅ (after 10)
Friend asks ino "weather in Kyiv" → `WeatherQuery` → the just-installed watcher answers with live https data → `WeatherResult` + surface.

### 12. Analyze a kernel-local C# project (new, via review_project or SE) 🔨
Install awesome-se-team (seeded by launcher). Ask ino "review the project at /path/to/cs" or Llm calls review_project tool → `ReviewProjectRequest` (kernel resolves local path, caps 100 files/1MB) → `ProjectReview.Analyze` (TODO count, largest, truncated) → `ReviewResult` + `UiSurface("review:{path}", ..., Markdown(report))` → routes to Ask tab, rendered with "Markdown:" + TODOs via WidgetTree/ TUI Rescue+Markdown. Honest no-LLM heuristic fallback.
Synapses: `ReviewProjectRequest`, `ReviewResult`, `UiSurface("review:...")` with `Markdown`, `NeuronTelemetry("ProjectReviewed")`.

## D5 Surface routing (verbatim from TUI redesign handoff)
- `marketplace` → Marketplace tab
- `review:` → Ask tab
- `packed-` → Notification only
- unknown → Ask tab

### 12. Push-publish to a peer 🔨
`/publish weather-watcher root@friend-pc:30000` → after local listing, the manifest + capsule bytes land in the peer's `IMarketplace.AddListingAsync` over its cluster client (for "I'm sending this to you" instead of "come browse me").

### GlobalBrain peer (real federation + social proof)
LAN kernels publish -> auto SyncListingsToGlobal (push via MarketplacePeer to hosted global addr or fallback mirror); global accepts (same IMarketplace.Add), listings appear in GlobalListings + "— Global / Community —" section of marketplace surface with "Install from global" buttons (uses global addr -> InstallFromPeer special). PullPopularFromGlobal / GlobalListingsReceived for pull. RateExperience / ExperienceRated + CommunityEndorsed for basic endorsements (stored in Ratings on mkt state). ino tools pull_popular_from_global + rate_experience surface global to chat. All new ser (Sync/Global*Synced/Received, Rate/ExperienceRated/ExperienceRating, GlobalPeer data) + collector/probe roundtrips. High-sev green. Same neuron, no new grain cats.

### 13. Check what this brain offers 🔨
`/market` (no peer argument) → local `IMarketplace.ListAsync` → the dynamically rendered 🛒 marketplace surface shows everything published from this account; `/market world@host:gateway` browses a peer instead.

---

## C. Self-evolution (ino as author)

### 14. ino proposes, human approves, brain grows ✅ Stage 5
"improve yourself" → `SelfImproveRequest` → LlmAgent reads journals via tools (now including restart/start/dashboard) → `ImprovementProposal` with structured action. For privileged (install, sim, ops) → proposal surface with Approve button. Human tap delivers ApproveAction → Creator executes only then. Non-privileged (create ino) auto. Full chat drive of marketplace + cluster ops with guardrail.

### 15. Creator authors a new .ino experience ✅ (E6 enhanced)
`ActionCreateIno` → Creator writes the `.ino`, runs InoValidator (error/warning Markdown UiSurface at author time if bad), generates neuron skeleton via Roslyn, installs (real parser at InstallBundle HasRules produces RuleSet with full RuleDeclaration[]/RuleStatement[], hands to brain-keyed IRuleHostNeuron), auto Pack (manifest HasGeneratedSource + source.cs for L2), emits created/packed. On second account/fork install the trigger registers (no convention) and fires (interpreter + binder emits declared synapses/surfaces). Validation + executable ino closes M3. Auto-pack on create/publish.

### 16. Sim-gated install (trust gate) ✅ Stage 4
`ActionRunSimulation` or InstallFrom* → if no/invalid Ed25519 sig (or unsigned legacy), StartQuarantineWorld (lightweight key-isolated domain + collector evidence for observed + tests), on green (sig + reactions + promote) emit QuarantinePromoted + notif surface + install in target. Real StartWorld for aspire worlds. Signed always for new packs. UpdateBundle + id@ver + notif UiSurfaces. N+1 zero-restart preserved.

---

## D. Worlds and accounts

### 17. Start a second account/world ✅
`StartWorldAsync("friend")` or the `example-world` Aspire domain → separate Orleans cluster (own ClusterId, ports, journals, marketplace). "Another account" on the same machine or LAN is just another world.

### 18. Fork a brain timeline (Stage 6 killer feature)
From picker or ino: fork "work" as "work-experiment" up to a journal point → ForkBrainAsync (replays parent journal with SynapseId dedup (S06), seeds the fork brain, starts isolated "fork-" world like quarantine). In the fork, ino can install risky (quarantine gate), test, then promote back (install the good bundles in parent or merge journal). "Different timelines = different digitalbrains" literal. Same machinery as L2 sim-gate.

### 18. Start the polished Flutter client from the brain (IFlutter, Aspire-integrated) ✅ (proper bring-back)
From REPL, ino chat, or Creator proposal: `StartFlutterClientAsync("web-server")` (or via the grain `GetGrain<IFlutter>().Start...`).
- The command synapse `StartFlutterClient` is emitted (journaled, observable on timeline).
- IFlutter grain (DurableNeuron in Sdk, exactly like the Aspire grain):
  - If running under a real DistributedApplication (aspire run): resolves the "flutter-client" executable resource and drives it via ResourceCommandService (start/restart) — true "aspire flutter integration" that allows building + running Flutter from the nervous system.
  - Standalone (`dotnet run start.cs` etc.): best-effort `flutter run -d web-server` from `src/DigitalBrain.Clients.Flutter`.
- The Flutter web client connects gRPC to the kernel's SurfaceStreamService (SubscribeSurfaces), receives UiSurfaceMessage (widget_json mirroring the UiWidget union), and renders live surfaces (kerneltasks, alarms from KernelTaskSupervisor, marketplace, reviews, etc.).
- Taps flow back (OnTap synapses or Flutter client events) → brain.SendAsync or dedicated handler.
This is the direct analogue of IAspire.StartNew / RestartDomainKernel: the brain neuron (not the AppHost) is the one that decides to bring up the Flutter UI. The client project is a focused surface renderer (see ino/clients/ino.flutter for the richer full ino experience with Rive/persona/topology).

### 19. Dedicated ino session terminal ✅
`LaunchInoSessionTerminalAsync(sessionId)` → spawned hex1b terminal with full-journal chat seed; live surfaces arrive over the Orleans timeline (the one client transport — the client-side gRPC subscription was removed in Phase 0) → long-running authoring/chat sessions per world.

### 20. Flutter as peer renderer (gRPC SurfaceStream + IFlutter lifecycle)
The console (hex1b TaskManagerClient + SurfaceRenderer) and the Flutter client consume the *same* UiSurface / UiWidget trees. The gRPC leg (SurfaceStreamService + SubscribeSurfaces) is the dedicated transport for Flutter/web (kept alongside the primary Orleans timeline). IFlutter is the single neuron the brain uses to bring the Flutter renderer up (build + run), exactly as IAspire is used for worlds and kernels. No special-cased hosting logic outside the neuron/synapse model.
