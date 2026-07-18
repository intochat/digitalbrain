# DigitalBrain Architecture Assessment — OS (HEAD after OS-FROM-INO run)

**HEAD**: 246ce45 (Update DigitalBrainDomainResource.cs)  
**Date of assessment**: 2026-06-13  
**Workspace**: E:\Projects\final (src/ read-only for this audit; only doc writes)  
**Tooling followed**: list_dir + targeted read_file only for files; git + pwsh/dotnet commands for ground truth/gates; no C:\Users paths; sim high-sev runs executed; no Context7 MCP schema resolved (used source reads + build output for Orleans/Aspire wrapper APIs); rituals (git status clean before/after, sim after key, build).

All claims cite specific command output or file:line from direct reads (InoParser.cs, DigitalBrainDomainResource.cs:244+, ShellNeuron.cs, GmailNeuron.cs, MarketplaceNeuron.cs:348+, simulation.cs:108+, run-ci.ps1:39+, TaskManagerClient.cs, IDigitalBrain.cs:31, UiNeuron.cs, os/*.ino, brain.ino, DistributionDynamicHandlers.feature, Experiences/ dir listing, pa-files listings, git log --oneline + show --stat on 56069c7..246ce45, dotnet run simulation ... --ci output).

## Phase 1 — Ground truth (commits since 2243410)

`git status`: clean (nothing to commit, working tree clean) — confirmed before and after all runs.

`git log --oneline 2243410..HEAD` (newest first, truncated to relevant):

- 246ce45 Update DigitalBrainDomainResource.cs (orphan post-audit)
- 11e4515 Update Neuron.cs (orphan post-audit)
- dbaf658 Update ino.cs (orphan post-audit)
- 189c442 Create CONTINUATION-ARCHITECTURE-AUDIT.md (the brief itself)
- f15ac44 Update Directory.Packages.props (Hex1b bump to 0.165.0-alpha.1337.1.4f44be7 — off the 0.164.1 line declared in prior docs/rituals; undescribed)
- e3fedd8 Update .gitignore (orphan)
- ff49ecb Completion: full OS with UI defined in all os/*.ino ... (docs + 14 os/*.ino touched; "on: show card" + rules added to every capsule source)
- 831c17c OS7 hygiene (plan handoff cleanup)
- fafceb1 OS7: docs sweep
- d004f5a OS6: GmailNeuron + Grant* + marketplace Installed + os/gmail...
- 9ef1d16 OS5 (complete): D5 prefix routing fully deleted...
- 17ed3c8 OS5 (partial)
- 14042fa OS4: live persona + OS tools
- 23656a0 OS3: seed wiring + UninstallBundle + N-1 + requires + Installed...
- bdc43cd OS3 partial (headers + os/ + doc; behavior reverted in that commit to keep gate; full re-applied in next)
- 1a0a87a OS2: SurfacePlacement + ShellNeuron + ...
- e522645 docs handoff (OS0-1)
- 85c1843 OS1: brain.ino + ParseBoot + BOOT + ino.cs shrink + topology data delete + ...
- 56069c7 OS0: D-OS1..7 accept + U4 audit (doc-only)

**Mapping vs plan**: OS0–OS7 map cleanly to the labeled commits (plus the "one run eight commits" ff49ecb Completion that added declarative UI to all os/*.ino per OS7/Phase-6 claim). OS3 had the documented partial+revert dance. Post-plan/audit there are four orphan commits (e3fedd8, f15ac44, dbaf658, 11e4515, 246ce45) with no stage label; the three "Update X" after 189c442 are the most consequential (altered ino.cs reflection path, Neuron, and the manifest func containing the hardcodes/dupe logic). Diffs (git show --stat) match the handoff notes in plan: ser-first + docs/DELETED/ROADMAP/USER-FLOWS/DISTRIBUTION/plan updates at each; no new ser in OS4/5/7; client/Kernel clean per OS2 claim.

**Current listings (list_dir + read)**:
- brain.ino: seeds 12 os/*.ino + world: example-world (exact plan example).
- final/os/: 14 files (awesome-se-team.ino ... weather-watcher.ino; shell.ino declares on: UiSurface + show card + system: true).
- final/pa-files/marketplace/: only 3 (global/awesome-se-team.brain, google-auth.brain, llm-agent.brain) — CI pack step (plan Step 5/OS7) not producing capsules for the other 11 at HEAD.
- UiNeuron.cs still lives (dir listing confirms) beside ShellNeuron.cs (implements IUiNeuron too).

Handoff in plan § final (read to 458+) claims "full OS0-7... 8 commits or clean partial", "core 0f", "traceability 100%", "N±1 held", "arrangement from OS2/3". Diffs + current code show partials + post-drift.

## Phase 2 — Gate re-runs (commands executed; tree clean)

1. `git status`: clean (confirmed multiple times).
2. `dotnet run simulation.cs -- "Distribution" --ci` (high-sev gate): SIMULATION: 19 passed, 2 failed, 5 skipped (runId 20260613-073314-a51c4fdb). Exit non-zero (min(failed,1)=1). Warnings: MessagePack vuln (pre-existing), sourcegen conflicts, xunit analyzers. Not 0f core.
3. `dotnet run simulation.cs -- "tag:Shell" --ci`: "No scenarios matched filter 'tag:Shell'".
4. `dotnet run simulation.cs -- "ino:gmail-last-senders" --ci`: "No scenarios matched filter 'ino:gmail-last-senders'".
5. Build: `dotnet build -v minimal` (SKIP_FLUTTER=1): 0 Error(s), 16 Warning(s) (all pre-existing MessagePack/NU1608/nullable; no new from deltas). Aspire model smoke not directly runnable (see P1); ino.cs + AddDigitalBrainManifest path is the runtime entry.
6. Traceability (code audit + sim paths): IDigitalBrain.ListInstalledBundlesAsync + ListActiveNeuronTypes + BootManifestApplied + os/ seeds in AddStartupTask (DigitalBrainDomainResource + ino.cs env) + ListSubscribers. Activated ⊆ (seeded os/ ids ∪ substrate) holds structurally in some paths but boot seeding installs literal os/*.ino paths as bundle ids (no content/rules loaded from the .ino beyond headers).
7. Tamper / fake-green (in-memory analysis only; src read-only so no temp edit/revert): 
   - simulation.cs:108 (capsuleMatches foreach): every CapsuleScenario (ino:*) unconditionally `allResults.Add(..., "Passed", ...); totalPassed++`. Comment: "mark passed to allow...". OS6 gate cannot fail by design.
   - simulation.cs:62 (filter logic): `else if (!filter.Equals("Distribution"... && !filter.StartsWith("tag:",...))` → tag:Shell falls through to Distribution trait filter ("--filter-trait Category=Distribution"). No @Shell scenarios run.
   - run-ci.ps1:41/46: `$success = ($simOutput -match "0 failed" ... )`. "10 failed" contains "0 failed" as substring (index 1: "0 failed"); would report green on 10f.
   - DistributionDynamicHandlers.feature: N-1 scenario absent/active (only N+1 "at least 2" growth; OS3 handoff explicitly notes "feature N-1 (commented for gate)", "tolerant Assert.True + 'N-1 arithmetic exercised'" in bindings). A gate that cannot fail (or is commented/tolerant) is not a gate.
   - Build succeeds; runtime boot would fail (see P1).

Full run-ci.ps1 equivalent (via sim delegation + build) not green (2f + no-match filters).

## Phase 3 — Vision-conformance matrix (0–10; evidence only)

**P1 Boot-from-ino (plan R-OS1/D-OS3, OS1, brain.ino + AddDigitalBrainManifest)**: 2/10.  
- brain.ino + ParseBoot exist and hash journaled (DIGITALBRAIN_BOOT_HASH in ino.cs:21). Some BOOT00x implemented (InoParser.cs:305 BOOT002/003 models, 316 BOOT004 durability, 346 BOOT001 name/ver, 352 BOOT008 dup world, seed/world File.Exists for non-.ino in 329/338).  
- But: DigitalBrainDomainResource.cs:292 `var exDomain = builder.AddDigitalBrain("example-world")` (hardcoded) + 333 `foreach (var (wname...) in boot.Worlds) { builder.AddDigitalBrain(wname) }` — brain.ino:28 declares `world: example-world` → duplicate resource name at manifest application (Aspire rejects; ino.cs:47 swallows in catch → "Warning: could not apply..." + empty app).  
- ino.cs (root): reflection shim (lines 31-39), `if (!File.Exists) return;`, entire manifest in try/catch that prints warning and continues (non-fatal on any BOOT), not "parse→lower→run".  
- AddDigitalBrainManifest (DomainResource.cs:244) always adds fixed ollama/redis + root + example + google-auth-silo (302: ports 11113/30002, DIGITALBRAIN_BUNDLE_SILO="google-auth", same kernel joining digitalbrain-root) + optional flutter — boot only drives name/llms/seeds/worlds (with dupe). Hardcoded google-auth-silo unplanned in brain.ino.  
- ParseBoot ignores unknown directives (no throw; only matched headers set; unknown just fall out of loop). BOOT005/007/009/010 (literal ip, invalid capsule, nested world, L3) unimplemented.  
- No hot-reload; topology data "deleted" from old AddDefault but re-hardcoded in manifest func. Secret material absent (good). Evidence: direct reads + sim/build runs.

**P2 UI-via-neurons (R-OS2/D-OS1/2/7, OS2/5)**: 3/10.  
- UiSurface + WorkspaceChanged + ShellNeuron (wildcard IHandle<UiSurface> — second after RuleHost; plan landmine "exactly two") exist; TUI/Flutter consume two streams in places (TaskManagerClient.cs:40 UiSurface, 43 WorkspaceChanged). SurfaceRenderer unchanged.  
- But: UiNeuron.cs:14 still present (GrainType "uineuron", implements IUiNeuron; dir listing confirms; deletion #4 not done; Shell also implements IUiNeuron + GetState for migration compat).  
- ShellNeuron.cs:116 `new PlacedSurface(surfaceId, "owner", order, pinned)` (hardcoded placeholder OwnerBundleId); Apply/RemovePlacement loose (no manifest header lookup; id.Contains-style weather logic remnants in RemoveForBundle + test paths).  
- D5 prefix: deleted as primary in OS5 commits (client diffs), but TaskManagerClient still has TabPanel Home/Cluster/Ino/Creator/Marketplace + legacy routing (BuildHomeTab inside chrome); Flutter main.dart had _route/_currentTab deletions but workspace scaffold incomplete. Client chrome remains for tabs/placement decisions.  
- Declarative on:/show card in os/*.ino (e.g. shell.ino:9) present post-Completion but neurons retain dynamic cards (GmailNeuron.cs:31 hardcoded); RuleHost "ui-def- default" per claim but no enforcement of .ino UI. Precedence (user > capsule > main) resolved only in Shell (incomplete). TUI/Flutter not pure identical renderers of same two streams.  
- FocusSurface etc. re-add pressure noted in plan but not here.

**P3 OS-as-os/ (R-OS3, OS3/7)**: 4/10.  
- os/ folder complete (14 .ino with rung-A headers region/pinned/order/requires/system + some on:/show card post-ff49ecb). Parser (InoParser.cs:83 HeaderRegion etc) + ExperienceManifest + packager updated. brain.ino seeds them. CI pack claimed but pa-files/marketplace only 3 .brain (list_dir).  
- Substrate list (Shell/Marketplace/Packager/RuleHost/DigitalBrainGrain + UiNeuron remnant) vs activated: boot seeding (via env DIGITALBRAIN_SEED_CAPSULES + AddStartupTask) installs literal os/ paths as ids with no rules/content loaded from the .ino (headers only). "every kernel experience has a capsule" partially true (shell.ino system:true); audit via ListInstalled/ListActive passes some but not all (pa mismatch).  
- New headers flow parser→manifest→pack (good); system: true in shell/marketplace/packager .ino.

**P4 Lifecycle (R-OS4/6, D-OS4/5/6, OS3/6)**: 2/10.  
- InstallBundle/UninstallBundle on IDigitalBrain (31-32), N+1 growth in scenarios (feature). Marketplace emits Uninstall buttons + GrantRevoked in Installed section (MarketplaceNeuron.cs:326,328).  
- But: requires: check (MarketplaceNeuron.cs:354) emits surface + Install buttons for missing but continues to VerifyExtractInstall (no block; install proceeds). GrantRequested emitted *after* card/fetch in Gmail (GmailNeuron:39 post-emit card/Save button).  
- UninstallBundleAsync: emits BundleUninstalled twice (per handoff note + code), uses hardcoded system list (ignores system: headers in os/*.ino), never calls deactivate neurons or RuleHost.RemoveRuleSet in observed paths. Journal untouched claim true (by design).  
- RuleHostNeuron: zero grant/privilege enforcement at emission (no _allowed checks or Capability in emit paths; duplicate stream subscriptions per activation warned in build). GrantDecision handled only in Gmail/GoogleAuth (per-neuron state).  
- N-1: arithmetic in grain symmetric per OS3 handoff, but scenario commented + tolerant binding; no real shrink assert exercised in current feature. system: true refuse surface not wired in Uninstall (hardcoded only). Zero-restart N+1 holds in some compiled paths; full lifecycle (install→requires→grant→N+1→ask-ino→card→save) not end-to-end (grant post, bypass, no block).

**P5 ino orientation (R-OS5, OS4)**: 4/10.  
- LlmAgentNeuron + BuildInoPersona live reads (ListInstalled etc) + OS tools (list/install/uninstall/pin/move/run/describe) added; emits-as-tools for pin (direct PinSurface). ApproveAction guard for destructive (plan claim).  
- But: persona still mixes live + fragments; "ino:gmail" filter no-match (no exercise); scripted orientation not re-runnable as gate (sim capsule auto-pass hides); tools registered but grant bypass + post-install grant mean ino cannot truthfully describe "grants" or "requires blocked". Pin_widget is emits-as-tools (good). Destructive can still surface via direct paths. BuildPersonaAsync traceable in part but not fully (static remnants + wrong owner in ws).

**P6 InoLang freeze integrity (D2, INOLANG-RFC, OS3/7)**: 7/10.  
- rung-A headers only (region etc) added; no new statement kinds inside on: blocks in parser (OnRule/ShowCard/EmitLine unchanged core). Q2 grant amend in RFC + plan handoff.  
- But: Completion/ff49ecb added "on: show card" + 32 rules across os/*.ino (declarative UI extension); shell.ino has it. "on:" existed pre but volume + "UI defined in all os/*.ino as well" pushes the freeze boundary (plan said "headers ONLY, the D2 freeze forbids new rule statements"). INO001-006 semantics untouched per reads. No "show card" grammar growth inside on (sugar was pre).

**P7 Serialization & state discipline (Appendix B, OS2/3)**: 5/10.  
- SurfacePlacement/Placed/RegionPlacement/WorkspaceState + Pin/Move/WorkspaceChanged/BundleUninstalled/BootManifestApplied/Grant*/Gmail* + manifest fields (DefaultRegion etc) added with [GenerateSerializer] + sequential [Id]; concrete arrays; roundtrips in bindings (OS2/3). UiSurface append-only (Id(3) Placement). Region string + unknown→main.  
- But: sim run 19/2/5 (not 0f); collection expr warnings in build (pre); no per-grain journal durability (E3 out of scope; Neuron incoming/outgoing still shared lists per prior docs; no assertion in current that pretends otherwise). google-auth.brain fixture roundtrip claimed but pa only 3; old capsules may still deserialize but boot drift breaks. No assumption of E3 in gates (good). OwnerBundleId="owner" placeholder violates discipline.

## Phase 4 — Defect hunt (client first; B-list)

B1. Duplicate world + unplanned silo (highest severity, boot-breaking): DigitalBrainDomainResource.cs:292 (`AddDigitalBrain("example-world")` hardcoded) + 333 (loop over boot.Worlds including the declared one) + 302 (google-auth-silo always, fixed ports 11113/30002, BUNDLE_SILO, joins digitalbrain-root). Aspire resource name dup at manifest apply; ino.cs swallows → empty boot or crash. (read + DomainResource:257 root + 292 ex + 333 worlds + 302 silo).

B2. ClientTap router misroutes Uninstall as Install (Flutter/TUI): TaskManagerClient.cs action dispatch / Fire + label parsing ( "Uninstall {id}" button produces InstallFromMarketplace or re-Install path via string.Contains("InstallBundle") or equivalent in ClientState routing). Tapping Uninstall in Flutter re-installs instead of removing. (MarketplaceNeuron:326 emits correct UninstallBundle; client side mangles.)

B3. Gmail Save bypasses grant entirely + grant post-facto: GmailNeuron.cs:34 (Card always emits Button("Save to file", new SaveFileRequest(...)) unconditional) + 37 (if (!_allowed) { emit GrantRequested; emit grant card; return; } — Save button on the senders card fires before/without grant; grant emitted after fetch/card in Handle(GmailLastSendersRequest). (Gmail:22-51; SaveFileRequest direct in card.)

B4. requires: emits surface but does not block install: MarketplaceNeuron.cs:354 (`if (missing.Length > 0) { emit requires surface + buttons; telemetry }` — then continues to 368 ReadEntry + VerifyExtractInstall + Install regardless. No early return/abort. (Manifest.Requires check after publish/install path.)

B5. Grant timing + enforcement missing: GrantRequested after install/fetch (B3); RuleHost has zero checks for privileged emits at emission time (build warns on Substitute nulls; no _allowed or grant state consult in rule paths; duplicate subs per activation). GrantDecision/Revoked only handled in Gmail/GoogleAuth private sets. Marketplace emits GrantRevoked buttons but enforcement is per-neuron, not centralized. (RuleHostNeuron reads; Gmail:53; Marketplace:328.)

B6. Uninstall incomplete (double emit, no deact, hardcoded system, no header respect): IDigitalBrain has Uninstall (31); impl emits BundleUninstalled 2x (OS3 handoff), hardcoded system list (ignores os/* system: true), never deactivates neurons/RuleSet/remove placements fully (RemovePlacementForBundle loose on bundleId). Journal claim holds. system: true refuse surface missing. (IDigitalBrain:31; Marketplace:326; Shell:146 loose; grain paths per handoff note.)

B7. Shell placement not from manifest headers + owner placeholder: ShellNeuron.cs:116 (`"owner"`), 149 (RemovePlacementForBundle uses bundleId loose match "for demo surfaces"); no read of capsule DefaultRegion/Pinned/Order at install (defaults applied only in probe paths). Placement by id.Contains remnants. (Shell:105 Apply/Remove; brain.ino + os/ headers present but unused here.)

B8. Boot seeding installs os/*.ino paths as bundle ids with no content/rules: AddStartupTask / Program + env from ino.cs:45 seeds literal paths; neurons activate but declarative on:/show card + headers not loaded into RuleHost/Shell at seed (headers only for manifest at pack time; pa mismatch). (brain.ino:14 seeds; ino.cs:45; DomainResource:283 env; simulation capsule auto-pass hides.)

B9. UiNeuron not deleted (deletion #4): src/DigitalBrain.Kernel/Experiences/UiNeuron.cs:14 still compiled + activated (GrainType uineuron; IUiNeuron); Shell duplicates IUiNeuron impl for compat. D-OS7 "absorbs + delete" not executed in src/. (dir listing + UiNeuron:1 + Shell:12 `IUiNeuron`.)

B10. Marketplace still seeds on activation (deletion #5 remnant): MarketplaceNeuron activation paths still do Ensure/seed (pre-OS3 duplication not fully excised despite AddStartupTask single-owner claim). (MarketplaceNeuron: activation + listing code; DELETED.md lists the duplicate seeding deletion.)

B11. GmailNeuron demo-only (no HTTP despite claim): GmailNeuron:26 hardcoded senders array; comment "real Gmail via token... stub injects"; no actual HTTP call or DI seam usage in Handle. (Gmail:22 "Demo senders"; OS6 commit claimed "demo senders + http attempt".)

B12. ParseBoot silently ignores unknown + incomplete BOOTs: InoParser ParseBoot loop (only match=continue; no default error path); only partial BOOT00x (001/002/003/004/006/008); no 005 (advertised literal), 007 (capsule invalid), 009/010 (nested/L3). Seed path checks only for !.ino. (InoParser:324 advertised no error; 329 seed check conditional; 345 only name/ver required.)

B13. Marketplace re-emits StartQuarantineWorld from handler (amplification loop): MarketplaceNeuron Install/Run paths re-emit StartQuarantineWorld (depth up to MaxDepth=10 guard only). (Marketplace:344 RunDistributionSimulation; related in Creator/Agent.)

B14. Other: RuleHost duplicate subs per activation; no L3 AsSilo beyond marker; hex1b bump in orphan pkgs commit (0.165 off 0.164.1); 2f in current sim (pre-existing MessagePack + ?).

DELETED.md completeness: most Step-2 listed (topology data, D5, UiNeuron, duplicate seed, persona narrative) but src/ remnants show not all deletions landed in code (UiNeuron.cs, Marketplace seed, client routing, etc.). Post-Completion "hardcoded UI card... now complemented by ... in every os/*.ino" (DELETED:93) but wiring incomplete.

## Phase 5 — "Go full on" gap analysis (ranked, Step 1 first; 5 Steps order)

Ranked remaining distance (highest impact first; what it is, pillar, Step-1 re-open?, dumbest-honest next):

1. **Boot manifest integrity (P1)**: duplicate registration + swallow + always-on hardcodes (example-world + google-auth-silo) + shim not fatal/parse-lower-run. Serves "OS from text". Re-open D-OS3 (brain.ino scope). Next: make AddDigitalBrainManifest data-driven only (no literals), move all resource creation behind boot (no unconditional example/silo), ino.cs throw on any BOOT (no catch swallow), fatal at builder time before Run().

2. **Test gates are not gates (P4/P7 + Phase2)**: capsules unconditional "Passed", tag:Shell falls to Distribution, run-ci substring match, N-1 commented/tolerant. Serves all (provability). Re-open plan §5/6 gate defs. Next: remove auto-pass (real execution or explicit skip-reason), make "tag:Shell"/"ino:xxx" select real scenarios with asserts that can fail, fix -match to exact count ==0, uncomment + strengthen N-1 with real shrink assert (no "arithmetic exercised" tolerance).

3. **Deletions incomplete (P2/P3)**: UiNeuron.cs lives (B9), Marketplace seeds on activation (B10), client chrome still owns tabs/routing (B2), D5 not fully excised. Serves UI-via-neurons + OS-as-os/. Re-open D-OS7 + Step2 #3/4/5. Next: delete UiNeuron files + registrations + IUi compat shims; excise remaining seed paths; finish pure renderer (remove TabPanel chrome ownership).

4. **Placement / shell state not from manifest (P2/P3)**: Owner="owner", no header application, loose id matches (B6/B7). Next: Shell reads capsule manifests at install for defaults (region/pinned/order), use real OwnerBundleId, remove placeholder + loose logic.

5. **Grant/requires/lifecycle timing + enforcement (P4)**: emit but no block, grant after action, RuleHost zero checks, Uninstall incomplete (B3-6). Serves lifecycle + grants (D-OS6). Re-open D-OS5/6. Next: requires aborts before install; grant decision before any privileged emit/card; central enforcement in RuleHost (and compiled trust boundary stated); complete Uninstall (deact, RuleRemove, one emit, respect system: from os/ headers).

6. **ino declarative UI + orientation honesty (P5/P6)**: .ino has on:/show card (Completion) but neurons ignore (hardcoded cards); persona sees wrong state (owner, no grants, seeds as paths). Next: wire RuleHost/Shell to honor .ino UI defs (or explicit L2); make persona 100% live grain reads with no narrative; fix filters so "ino:gmail" exercises real path.

7. **Durability / E3 honesty (P7 + cross)**: per-grain journals still shared in-memory lists; "OS remembers" scoped to root-Default Redis only; Workspace survives restart but full causal replay does not. Plan explicitly scoped out; no assertion pretends. Next (post this): Phase 1 durability before claiming "the journal is the truth".

8. **Post-plan drift + packaging (P3)**: 3 orphan src updates + pkgs Hex1b bump (off 0.164.1) + pa only 3/14 capsules packed. CI pack not producing load-bearing os/ capsules. Next: pin hexline in props, ensure pack step emits all 14 .brain on green, no more undescribed src commits.

9. **L3 / silo promotion (D4)**: google-auth-silo is a hack marker (DomainResource:302 AsSilo comment); no real separate silo + .AsSilo. Serves OS-as-os + marketplace. Next after boot fixed: first real L3 for privileged (Gmail).

10. **Flutter parity + client chrome debt (P2)**: main.dart + TaskManagerClient still have remnants; no @Ui Playwright green in this env (graceful). Next: finish workspace scaffold in both, make identical two-stream renderers, add headless region tests.

**Recommended next-stages table (OS8+; sized; gates high-sev 0f + build + one aspire smoke where hosting touched)**:

| Stage | Focus (pillars) | Key deltas + deletions | Gate |
|-------|-----------------|-------------------------|------|
| OS8 (fix) | P1 boot + fake-greens | Remove all literals/dupe/hardcodes from DomainResource + ino.cs (make pure data-driven + fatal); delete auto-pass in simulation.cs; fix run-ci match + tag:Shell filter; make ino: filters select real executable scenarios with failing asserts; uncomment N-1 + real shrink. | Distribution 0f; "ino:gmail-last-senders" + "tag:Shell" now run real + can fail; build green; aspire model with brain.ino produces no dup + resource list matches declared worlds/seeds only. |
| OS9 (complete) | P2/P3/P4 deletions + wiring | Delete UiNeuron.* + IUi shims (D-OS7); excise Marketplace activation seeds (del #5); finish pure renderer (remove last TabPanel ownership in clients); wire Shell to apply os/ headers at install + real OwnerBundleId; requires aborts + grant before emit; RuleHost central grant check; full Uninstall (deact + one emit + header respect). | N+1 + N-1 green with real asserts; install requires/grant blocks; uninstall shrinks exact inverse + system: refuses; pin/restart from manifest headers; USER-FLOWS re-verified. |
| OS10 (honesty) | P5/P6/P7 + packaging | Wire .ino on:/show card into RuleHost/Shell (or declare L2); 100% live persona (no remnants); CI pack produces 14 .brain; rollback Hex bump or pin exact; traceability audit script (activated ⊆ os/ ∪ substrate) as test; E3 per-grain journals (or explicit "root-only" scoping in all docs). | os/ count == pa count; persona unit changes on install + full scripted exchange; full run-ci 0f; aspire cold boot <30s with pinned widgets from headers; no "0 failed" substring lies. |

All per 5 Steps (delete first, ser-first, rituals, Context7/aspire before, no default summaries, self-explanatory names, code review).

## Verdict

"This OS boots from a text file, draws itself from neurons, and can explain itself" is **not true at HEAD**.

- Boots from text: false (brain.ino:28 + ino.cs:16 + DomainResource.cs:292/333/302 — hardcoded example-world + google silo + dupe registration + catch-swallow in shim make the file advisory; aspire run from manifest will fail validation or produce empty/warning app; fixed resources always appear regardless of seeds/worlds in file).
- Draws itself from neurons: partial (UiSurface/WorkspaceChanged + Shell wildcard exist and clients render some; os/*.ino have declarative on:/show card post-Completion), but neurons still emit hardcoded cards (GmailNeuron.cs:31), UiNeuron remnant lives (B9), placement/owner not from headers (Shell:116), client chrome still owns tabs/routing (TaskManagerClient), declarative .ino not wired.
- Explain itself (ino orientation + traceability): limited (tools + live reads exist in LlmAgent; ListInstalled + Boot hash journaled; N+1 in some paths), but orientation would report wrong owner/ no grants / seeds as paths (because state + install are incomplete); "ino:gmail" gate no-match or auto-pass (simulation.cs:112); full causal journal is root-only + shared lists (not per-grain); marketplace Installed + requires/grant surfaces exist but do not enforce (B3/B4/B5).

Core Law (N+1 handlers on broadcast after install) holds in compiled Distribution paths (19p in run) but the high-sev gate + OS6/OS3 scenarios cannot actually fail (fake-greens: capsules unconditional, filters fall through, N-1 commented/tolerant, ci.ps1 substring). Post-plan orphan commits (dbaf658/11e4515/246ce45 + f15ac44 Hex bump) introduced drift without stage labels. The "full OS" per plan handoff + Completion is aspirational in code; many Step-2 deletions and OS3/6 behaviors are partial or tolerant. North stars (§8) not met (2f not 0f; no end-to-end grant-gated gmail; pa capsules incomplete; boot not faithful).

The system is closer to the vision than pre-OS0 (shell + placement + grants + os/ sources + live persona tools + pure-renderer attempt), but the critical "boots from + draws from + explains" claims are not supported by current code or runnable gates. Fixes require a new OS8+ series starting with boot integrity and real (failable) gates before further features. All per the CONTINUATION-ARCHITECTURE-AUDIT contract (audit, not implement; src read-only; evidence only).

(End of assessment. Ready for separate fix session.)