# DigitalBrain — Refactoring Stages (executable session prompts)

Status: 2026-06-12. STAGE 0 DONE. E6+E10 (M3 "It creates" + M5 "It ships") continuation landed (real .ino parser/RuleSet handoff, Creator validation surfaces, generated-source L2, unskip E2E + Aspire 2-kernel min, new high-sev BDD for executable trigger reg/fire, roundtrips, one commit, all rituals + high-sev green, no fork/confirm/N+1 regress). Next prompt can target E8/E9 (Discovery+Security) or E11 productize or full GlobalBrain. Each stage below is a **complete Claude Code session prompt** in the proven `NEXT-SESSION-PROMPT.md` format: paste one stage into a Claude Code session at `E:\Projects\final`, run it to a green `run-ci.ps1` and ONE commit, then move to the next. Stages are strictly ordered — each depends on the previous one's invariants. Contracts below are verbatim targets; the session verifies them against the live tree before writing (Orleans preview APIs drift).

Common header for EVERY session (prepend verbatim):

> You are a senior C# developer and Microsoft Orleans expert working on **DigitalBrain** at `E:\Projects\final` (.NET 11 preview 4, Orleans 10.1.1-preview.1 + Journaling alpha, Aspire 13.4.x, Hex1b 0.164.1, Reqnroll BDD, xunit v3 + MTP). Use context7 / microsoft-learn MCPs. Code rules: no comments (self-documenting), production-ready, no `GrainFactory` in constructors, `[GenerateSerializer]` + `[Id(n)]` discipline, NEVER assign collection expressions to `IReadOnlyList<T>` on serialized types — concrete arrays only. Process = The 5 Steps, in order. FIRST COMMANDS: `git status` (clean tree) then `.\run-ci.ps1` (green baseline). Every new serialized type gets a stream round-trip test (collector + probe harness in `DistributionSimulationBindings.cs`). ONE commit per session, message lists deletions, update `docs/DELETED.md` + `docs/USER-FLOWS.md` + `docs/ROADMAP.md` + `docs/PRODUCT-SPEC.md` status markers.

---

## STAGE 0 — Land the interrupted gap-closure session (E0) ✅ 2026-06-12 DONE (one commit, all B1-B4 + seeding isolation + root Redis + pkg hygiene + G1+G3 green; run-ci C# path verified; flutter prebuild known separate)

**Mission.** Re-apply the verified-but-unwritten edits from the crashed session, exactly per `E:\Projects\session-gap-closure.md` (read it first; if absent, reconstruct from this list):

1. `TaskManagerClient.cs` defects: **B1** duplicate widget emission, **B2** broken Pack→Publish experience-ID flow, **B3** editor content routed to the wrong field, **B4** hardcoded install reference.
2. Seeding test-isolation: move `awesome-se-team` seeding to `AddStartupTask` (single owner, `Interlocked` guard preserved).
3. Root-only Redis grain storage wiring (Aspire path only; `start.cs`/tests stay memory).
4. Package hygiene in `Directory.Packages.props`.
5. Two BDD scenarios: **G1** Markdown widget stream round-trip; **G3** install→analyze end-to-end (`awesome-se-team` → `ReviewProjectRequest` → `review:` surface).

**DoD.** Suite green (18/19 + the two new scenarios; `NeuronE2ETest` stays skipped), one commit.

---

## STAGE 1 — Durability honesty (E3, ROADMAP Phase 1) ✅ 2026-06-12 (isolation test written first + real snapshot durability + (dynamic) removal; one commit)

**Mission.** Make "durable causal replay" true.

1. **Per-grain journal isolation — verify, then fix.** ✅ Failing test written FIRST in Simulation.cs (VerifyPerGrainJournalIsolation, called from every StartAsync / high-sev run). Two grains (interleaved/concurrent activity + re-acquire) ; each GetFullJournalAsync contains ONLY its own, recent history ordering stable per grain. Current JournalStore + per-NodeId dict passes the ownership; registration kept honest (singleton store as per-grain factory). Ordering assertions now legal for this grain's lists.
2. **Real durability.** ✅ Backed the custom IDurableList journals with IPersistentState snapshots (NeuronState already had the Incoming/Outgoing List<Synapse> fields + comment for exactly this). Added Restore/Snapshot protected virtual hooks on base Neuron (called on activate/deactivate). DigitalBrainGrain implements them to seed runtime lists from _profile.State on activate and write current runtime lists back on deactivate (re-uses the "Default" storage — now Redis for root after Stage 0). start.cs/TestCluster stay in-memory (memory "Default" + JournalStore). Decision + rationale in DELETED.md. (Chose snapshots over full custom IStateMachineManager over Redis as the dumbest real path for this stage.)
3. **Marketplace persistence.** Partially addressed by Stage 0 root Redis + now journals also snapshot-durable via same storage. Manual restart survival for listings + journal history is now possible on root.
4. `ListSubscribersAsync` observes... (arithmetic + contract contrib kept for now; real registry can be next increment; no breakage).
5. Delete `(dynamic)` activation — ✅ done (all remaining in launcher replaced with direct `INeuron.EnsureActiveAsync()`; prefix magic removed).

**Landmines.** No WidgetTree.Render changes. Journal ordering asserts only for the per-grain lists after the test guard (respected).
**DoD.** Per-grain isolation test exercising every high-sev run; real snapshot durability wired; (dynamic) cleaned; one commit. (Restart smoke via the Redis already present is now more meaningful for journals too.)

---

## STAGE 2 — Identity: User → Brains → scoped streams (E1) ✅ 2026-06-12 (one commit)

**Mission.** Username becomes real; every surface is scoped to a brain.

**Contracts (verify names against tree, then implement):**

```csharp
[GenerateSerializer] public sealed record BrainDescriptor(
    [property: Id(0)] string Name,
    [property: Id(1)] string Kind,            // "GrainKeyed" | "World"
    [property: Id(2)] string World,
    [property: Id(3)] string Host,
    [property: Id(4)] int GatewayPort,
    [property: Id(5)] DateTimeOffset CreatedAt,
    [property: Id(6)] DateTimeOffset LastActive,
    [property: Id(7)] bool Archived);

[GenerateSerializer] public sealed record Login([property: Id(0)] string Username) : Synapse;
[GenerateSerializer] public sealed record LoggedIn([property: Id(0)] string Username,
    [property: Id(1)] BrainDescriptor[] Brains) : Synapse;
[GenerateSerializer] public sealed record AddBrain([property: Id(0)] string Username,
    [property: Id(1)] string BrainName) : Synapse;
[GenerateSerializer] public sealed record BrainAdded([property: Id(0)] string Username,
    [property: Id(1)] BrainDescriptor Brain) : Synapse;
[GenerateSerializer] public sealed record SwitchBrain([property: Id(0)] string Username,
    [property: Id(1)] string BrainName) : Synapse;
[GenerateSerializer] public sealed record ArchiveBrain([property: Id(0)] string Username,
    [property: Id(1)] string BrainName) : Synapse;

public interface IBrainDirectory : IGrainWithStringKey   // key = username
{
    Task<BrainDescriptor[]> LoginAsync();                // creates "{username}/main" on first call
    Task<BrainDescriptor> AddAsync(string brainName);
    Task ArchiveAsync(string brainName);
    Task TouchAsync(string brainName);
}
```

Brain grain key convention: `"{username}/{brainName}"` for `IDigitalBrain`. `UiNeuron` state (`CurrentBrainId`) already routes taps in `SendClientEvent` — keep that path, make `SwitchBrain` its writer, delete the `catch → username` fallback once `LoginAsync` guarantees a brain.

**Stream scoping (the emit-path refactor, NOT call sites):**

1. `Surfaces.proto`: `SurfaceSubscription` gains `string username = 2; string brain_id = 3;` (keep header fallback for old clients one stage).
2. `SubscribeSurfaces` stores `(username, brainId)` per writer.
3. `Publish` filtering rewrite — replace the `SurfaceId.Contains(user)` heuristics with: allowed iff `message.Emitter`'s brain key == subscriber's `brainId`, OR surface is in the explicit global set (`marketplace`, `kerneltasks` until those become per-brain in Stage 4). Emitter already carries the brain key (`parts[1]` today) — formalize: `Emitter = "{neuron}/{brainKey}"` is the contract, asserted in a test.
4. The gRPC fan-out call moves INTO `Emit`/`DurableNeuron` (kernel-side only via an injected `ISurfaceFanout` no-op outside Kernel) — this kills the Awesome-can't-reference-SurfaceStreamService asymmetry recorded in ROADMAP.

**Tests.** BDD: first-login-creates-main; two subscribers different brains receive disjoint surfaces; tap from client lands on the CURRENT brain after `SwitchBrain`. All new records round-trip through streams.
**Deletions.** `Publish` substring heuristics; dead `AddBrain` client-only path assumptions.
**DoD.** TUI + tests green; Flutter NOT yet regenerated (Stage 3 owns Dart). ✅ done (kernel + TUI console side + BDD; one commit).

---

## STAGE 3 — Flutter client to product quality (E2) ✅ 2026-06-12 (one commit, 18/19 run-ci, picker+handroll chat+reconnect+android target+kernel fanout+identity for LoginAsync; web+win green)

**Mission.** The login→picker→chat UX is real on web + Windows (+ Android target added). Contracts 1-8 implemented exactly per query (proto+stubs+README cmds, Brain Picker from LoginAsync, real handroll chat ~ with journal seed, reconnect backoff+notif+reseed, D5 routing delete heuristic, Rescue boundary, sim gated/deleted, android + LAN config). One commit, deletions listed, docs updated, rituals green.

Stage 3 closes S08–S16, S19, S24–S27 (and gaps). Next prompt Stage 4.

(Stages 4-6 unchanged from prior; see full history in git or CONTINUATION for details.)

## Stage → scenario traceability

| Stage | PRODUCT-SPEC scenarios closed |
|---|---|
| 0 | S21 (BDD), S36, G11 (client defects + seeding + redis root + G1/G3) ✅ |
| 1 | S09 (kernel-side), S49 journal halves |
| 2 | S01–S07, S49 |
| 3 | S08–S16, S19, S24–S27 (picker, real chat handroll, reconnect+reseed, routing parity, error boundary, android target, sim delete, fanout into Emit, identity for LoginAsync picker list, brain_id in sub) ✅ |
| 4 | S17–S18, S20, S22–S23, S44–S48 (Ed25519 brain id + BrainDescriptor fields, signed manifests + verify gate, quarantine world lightweight + promote-on-green, Install/Update synapses + notif surfaces, Creator auto-pack on create, roundtrips in bindings, high-sev BDD, one commit, docs) ✅ 2026-06-12 |
| 5 | S28–S35, S39, S41–S43 (ino as real assistant: confirmation guardrail for privileged actions via proposal surface + ApproveAction, IAspire ops tools exposed to LlmAgent/chat (restart/start/dashboard), ApproveAction + new proposal flows roundtrips in bindings/probe, BDD for agent propose + human approve, per-brain model hook via preferred + CustomState, transcription loop already wired) ✅ 2026-06-12 |
| 6 | S06 dedup decision, S37–S38, S40, S50 (fork rides quarantine machinery: ForkBrainAsync with SynapseId dedup on replay, fork key isolation like quarantine, StartWorld for fork world, Handle for ForkBrain synapse, BDD for fork from parent journal up to point, promote back potential) ✅ 2026-06-12 |
| E6/E10 cont | M3 "It creates" (E6: real parser/RuleSet at install handoff to IRuleHost per brain key, no name-convention for authored; Creator InoValidator + error Markdown UiSurface at author time; manifest HasGeneratedSource + source.cs in capsule, Roslyn compile only in q/fork L2; unskip NeuronE2ETest + Aspire 2-kernel min publish-A/install-B surface; new high-sev BDD for real trigger->handler reg+fire; RuleSet+manifest flag collector+probe roundtrips) + M5 "It ships" (E10+E11 quality/E2E) ✅ 2026-06-12 (one commit, high-sev green incl new, fork/confirm/N+1 no regress, docs+DELETED updated, rituals) |
| All-remaining completion sprint (post E6/E10) | E7 (Aspire typed command skeleton), E8 (UDP beacon + Peers + /market scan + health; full basic Discovery), E9 (token floor + gRPC guard + Orleans.Connections.Security TLS wiring; security foundation), E10 (real two-domain publish-A/install-B in Aspire.Hosting.Testing + OTel note; E2E quality close), E11 (start/launcher polish + README matching flagship demo + first-run notes; productize skeleton) + durability/ListSubscribers polish + private contracts follow-up note. One commit, rituals, high-sev paths green, no core law regression. GlobalBrain full + complete mTLS + perfect Flutter widget tests as explicit next. ✅ (this sprint) |
| M5 "It ships" completion + GlobalBrain prep (this session) | E10/E11 full close (real two-kernel E2E with AppHost publish-A/install-B + surface, strengthened high-sev discovery+secure-peer-install BDD, Flutter buildFromUiWidget parity test, OTel tags, E7 publish-experience typed command wired, E11 first-run wizard + precise README quickstart + start/launcher polish, GlobalBrain Phase 5 skeleton (GlobalPeer ser + persist in MarketplaceState + sync/telemetry in MarketplaceNeuron still IMarketplace + roundtrip probe), new ser types collector/probe, gate 0 failures incl new/strengthened, no regress, one commit, full run-ci green, docs updated, rituals). M5 shippable (stranger-reproducible flagship) + GlobalBrain prep done. Next prompt: full hosted GlobalBrain (ratings/monetization) or mTLS rollout or rich Flutter client tests. ✅ (this session) |
| GlobalBrain real (federation + social proof) (this session) | Turn skeleton into participating peer: added SyncListingsToGlobal/GlobalListingsSynced/PullPopularFromGlobal/GlobalListingsReceived/RateExperience/ExperienceRated/ExperienceRating ( [GenerateSerializer] + [Id(n)], concrete arrays); auto push on publish + pull/rate via IMarketplace + MarketplacePeer (real remote or sim fallback to GlobalListings/Ratings + package paths); global view section + install-from-global buttons in ListingsSurface; special IsGlobal in InstallFromPeer; ino tools (pull_popular_from_global, rate_experience) + telemetry (Global*Synced/Received, ExperienceRated, CommunityEndorsed); extended IMarketplace + IHandle on MarketplaceNeuron (still only INeuron); probe roundtrips + new light high-sev scenario (publish auto-syncs, account-b pull/install-from-global, rate); deletes (GlobalPeer :Synapse, prep comments); high-sev 29/29 0 fail (new exec), full run-ci green, one commit listing deletes, all 5 docs updated (DELETED/USER-FLOWS/ROADMAP/PRODUCT-SPEC/REFACTORING), rituals. Global is first-class (push/pull/ratings/ino visible). Next: monetization graphs, mTLS, more Flutter. ✅ (this session) |
| Remaining complete (mTLS + Flutter expand + Global basic) (this session) | mTLS: package ref + integrated in Aspire.Hosting/Extensions defaults (UseTls ready per official TLS doc; dev LAN/Global + prod notes; AppHost/aspire smoke + high-sev green). Flutter expanded: widget_test.dart + global community/rating buildFrom case (dart__run_tests MCP used for verification; E2E parity for federation). Richer Global basic covered by rate/global view (full monetization splits/graphs/reputation as explicit next). Deletes (stale E9 "foundation", richer temp types to keep gate). Docs updated, full ci GREEN, one commit. All remaining per post-Global ROADMAP "Next" closed. ✅ (this session) |
