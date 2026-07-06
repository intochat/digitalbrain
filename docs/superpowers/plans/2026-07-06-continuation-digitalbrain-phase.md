# DigitalBrain Continuation Plan (2026-07-06)

Source: Analysis against dead-neuron-cleanup.md, architecture-trash-analysis-2026-07-06.md, architecture-trash-action-plan.md, PRODUCT_VISION.md (bundles/distribution), SYSTEM_DESIGN.md.

Date: 2026-07-06
Status: In progress. Immediate slice executed.

## Honest Current State Snapshot (post initial cleanup + exploration)

**Trash / Mechanical (per dead-neuron + trash plans):**
- Dead SDK neurons (Roslyn/Git/etc + Drive/Calendar) deleted, Roslyn pins unified to 5.6.0, ProcessRunner relocated.
- DigitalBrain.Developer + .Windows (and .Tests) .csproj + authored sources gone. **Remnant empty dirs + build artifacts removed in this session (immediate slice).**
- slnx + Kernel.csproj refs cleaned (no more ProjectReference hits).
- Thin wrapper shells (Telegram.Channel, UiKit + Developer/Windows) cleaned from disk in this session (interfaces + refs + tests were pre-migrated in prior work; CoreBoundaryTests and comments already correct).
- SystemRollingSurfaces is live (used by SystemNeurons for kernel self-update flows) — not trash.
- PrototypeJournals remains honest name (in-memory until durable journal work).
- Build: clean (0 errors, pre-existing nullable warnings only). Targeted tests (SelfEvolution/Ino/Marketplace/Automation/Foundry) green (38+).

**Self-Evolution Approval Rail (per trash-action-plan Phases 0-3):**
- Core/SelfEvolution.cs: full records (Proposal, Decision, Risk enum, ApplyVia constants for marketplace/automation/foundry, audit synapses like ProposalPending/DecisionRecorded/ApplyResult/RollbackRequired). Additive contract.
- SelfEvolutionNeuron (Kernel/SelfEvolution/): journals proposals, maintains pending/decided/applied/expired projection (replay on activate), validates, routes approved to ApplyRegistry.
- ISelfEvolutionApplyHandler + registry: risk-checked, allowlisted. Handlers present:
  - MarketplaceInstallApplyHandler
  - AutomationDefinitionApplyHandler
  - FoundryRun/DeployApplyHandlers (FoundryApplyHandlers.cs)
- MarketplaceNeuron.InstallFromMarketplace: stages MarketplaceInstallStaged + SelfEvolutionProposal (bypass only via explicit TrustedLocalInstallBypass config).
- MCP define_reaction: stages proposal + AutomationDefinitionStaged (description says "for approval").
- CodeFoundryClosedLoop: stages unless (AutoApply && TrustedAutoApply config — default false).
- Side doors largely closed for user paths; dev bypasses explicit and documented. Startup seeds / CompanySkillOrchestrator still use direct in a few places (review needed).
- Durable replay + full rollback integration: partial (proposals in journal, RestoreCheckpoint exists but integration thin).
- No bypass via "just exists" — explicit decision required.

**Google + Salesforce (focus area):**
- Live: IGmailNeuron + GmailNeuron (Google.GoogleGmailApiClient via credential from pack-config "google"/"default"), IGoogleAuthNeuron.
- ISalesforceCrmNeuron + SalesforceCrmNeuron (thin; delegates to ISalesforceApiClientFactory + scoped), SalesforceAuthNeuron + surfaces.
- Routed exclusively via InoNeuron (personal assistant grain, GrainType "ino.personal.v1").
- Auth flows: credential surfaces via UiKit (buttons emit signals), pending request resume on AuthCompleted / PackConfigured signals.
- Data: list + read, simple text reply + UiWidgetTree (Table/Heading etc.) surfaces delivered to FlutterUiNeuron.
- **Intent classification modernized (B1)**: Regex fully removed from InoConnectorIntents. New `InoIntentClassifier` provides fast keyword path + async LLM structured classification (IChatClient.GetResponseAsync with JSON intent prompt). Wired into Gmail + Salesforce handlers + Handle* methods (LLM confirmation before credential/fetch). Other intents still use contains-style in handlers (next pass). Dispatch chain preserved.
- UX after auth: basic "just chat" now goes through classifier (better paraphrase tolerance via LLM). Still simple summaries; richer follow-ups and actions planned in B2.
- No other "real" external integrations compete.

**LLM / Model Selection:**
- Composition only: AppHost + DigitalBrain.Aspire (WithLLM<Qwen25Coder1_5B>() or Gpt4oMini via LlmModels.cs + DigitalBrainBuilderExtensions). Env passed to kernel.
- Runtime: LlmResponderNeuron resolves global IChatClient (DI) or per-pack via IScopedChatClientFactory + IPackConfigStore ("llm_provider"/"llm_key").
- Ino generic path, AskLlm consumers, Foundry code-gen (if any) use the above.
- **No user-facing persisted setting** to change active model at runtime / per-scope / per-workspace. No Settings surface affecting responders.
- Embedding/voice also Aspire-wired.

**UiKit + Gallery:**
- ~30 components in DigitalBrain.Ui.Contracts/UiSurfaces.cs: UiKitVocabulary (Screen, Text/TextField/Button/Panel/Checkbox/Switch/TextArea/Select/Radio/Slider/Date/Row/Column/Divider/Header/Gap/Heading/Icon/Avatar/Badge/Tile/List/Tabs/Breadcrumb/Pagination/Alert/Progress/Spinner/Tooltip/Sidebar/BottomNav/Dialog/Sheet/Toast/Table/GraphCanvas).
- Server-driven: UiWidgetTree + UiSurface (widget-tree kind) emitted by neurons/packs. Factories in Pack.Contracts/UiKit/UiExperience.cs and Configuration.cs.
- "ui-gallery" exists as seeded experience/hop (demo surfaces, Flutter route /gallery, NativeGrpcGalleryDeliveryE2ETests, some render tests). Not exhaustive catalog of every vocab + state variants.
- No WinUI3-Gallery equivalent for pack authors or product showcase.

**Automations + Intent Creation:**
- AutomationNeuron: journal-driven reactions + scripts (RegisterScript/Reaction, CreateAutomationApp, hot on timeline). Surfaces emitted.
- MCP: define_reaction (stages via rail), create_automation_from_description (heuristic regex parser + DefineReactionAsync — still clunky NL->code), list/remove.
- Not "just chat": no LLM understanding of "when email from X, summarize to Salesforce" that produces safe proposal.
- All high-risk go (or should) through SelfEvolutionProposal.

**Intent Routing (general):**
- Mix: static dispatch + regex sniffing (Ino primary, some Foundry/DataVis/Gateway mutation paths).
- No capability graph or vector search yet.
- PersonalAssistant pack and others use AskLlm + Signal indirection.

**Verification baseline (this session):**
- `dotnet build ...` (skips): 0 errors.
- Targeted tests: 38 passed (SelfEvolution/Ino paths/Marketplace/Automation/Foundry).
- `aspire doctor`: all pass (Aspire 13.4.6, .NET 11p, Docker, certs).
- Per AGENTS: fast loop default; full cluster/Aspire for rail + LLM + distributed changes.

Gaps vs vision (PRODUCT_VISION distribution/creator focus + SYSTEM_DESIGN neuron/synapse law + self-evo promise):
- Rail mostly enforceable now — big win from analysis.
- "Just chat with Gmail/SF" and "chat to create automation" not magical (regex + ceremony).
- No user LLM control.
- UiKit is internal toolkit, not first-class discoverable product/author surface.
- Intent brittle; self-evolution not yet used for "Ino proposes new capability/automation".
- Build graph smaller but remnant dirs cleaned here; thin projects remain.

## Product Requirements (user phase priorities)

1. **Ruthless trash removal (complete)**
   - PR: Disk + solution free of Developer/Windows remnants, thin-wrapper merges complete, no dead code references in build graph or mental model. Verified by architecture guard tests + clean build.
   - Keep only live: Google (Gmail/Auth), Salesforce (Auth/Crm), Context, Telegram (split transport/logic), Ui (contracts + runtime).

2. **Focus hard on Google + Salesforce "magical chat"**
   - PR: After one-time OAuth/pack-config auth, user says "show my recent emails" / "list Salesforce accounts from Acme" / "find emails about the deal" and gets accurate, nicely rendered results (surfaces + text) with no extra steps.
   - Zero ceremony post-auth. Richer than list+snippet: summaries, actions (e.g. "summarize this email into Salesforce note").
   - Killer simple experience; everything else deprioritized.

3. **User-controlled LLM (first-class)**
   - PR: Settings (or per-workspace / per-chat scope) lets user pick active model/provider from registered ones (Qwen local, gpt-4o-mini, future). Persisted (pack config or dedicated journaled setting). Change immediately affects LlmResponderNeuron, Ino generic/reasoning paths, Foundry (if LLM-assisted), PersonalAssistant etc. No restart.

4. **UiKit Gallery**
   - PR: Browsable, live page/experience (like WinUI Gallery or Flutter widget catalog) that renders every UiKitVocabulary component with examples, states, and source hints. Server-driven (UiSurface/WidgetTree). Usable as product surface + authoring reference for pack creators. Tests via BundleHarness + LiveRenderVerifier.

5. **Radical simplicity for key actions**
   - "Chat with Gmail/SF" and "create automation by describing" feel like natural conversation.
   - Auth is one-time (already close); subsequent use is zero-config in context.
   - Automation: NL description -> LLM-proposed reaction/script -> SelfEvolutionProposal (user approves) -> active. No "register script + reaction" UI.

6. **Kill regex intent (modern classification)**
   - PR: Ino (and similar) use LLM + retrieval (vector/examples) to understand request + available capabilities, not brittle Contains/Regex. Hybrid, reviewable, fallback-safe.

All creation/mutation of behavior (new automation, pack embodiment, generated code) goes through (or explicitly integrates with) self-evolution rail. Journal + rollback visible.

## Technical Requirements

- Orleans grains (INeuron/IHandle), synapses as source of truth, journals for durability/audit.
- Server-driven UiKit (UiWidgetTree + vocabulary factories) — no client logic for components.
- SelfEvolutionProposal/Decision the only non-trusted mutation path.
- Small slices: independently buildable, targeted tests, fast loop (`dotnet build && dotnet test --filter "Category!=cluster"` or FullyQualifiedName~...).
- LLM changes must be config-driven at runtime (not recompilation); use existing IChatClient / ScopedChatClientFactory + pack config or new setting grain.
- Intent: capability descriptions registered (on install/automation define via apply handler hooks), embed + retrieve via Context/Qdrant or lightweight index, structured LLM output for classification.
- Tests: fast unit on TestCluster; E2E only when render/aspire required. No regression on timing fixes.
- Use Context7 for any Orleans/Aspire/Microsoft.Extensions.AI surface before edits.
- After edits: build + targeted test + aspire doctor. Full aspire run for rail/LLM/distributed.

## Pragmatic Sliced Plan (prioritized, continuation of trash-action-plan)

**Phase 0 (done in prior + this session):** Rail skeleton + dead neuron deletions + dir cleanup (this session executed removal of 4 remnant dirs; build verified).

**Phase A: Finish High-Value Trash (small, 1-2 slices) [largely complete as of 2026-07-06]**
- A1 (executed): Interfaces were pre-moved (`ITelegramChatNeuron` → `DigitalBrain.Telegram`, `IFlutterUiNeuron` → `DigitalBrain.Ui.Contracts`; boundary tests + Synapse.cs comment + references already updated). Deleted the four empty shell directories (Telegram.Channel*, UiKit*) + prior Developer/Windows shells. Brain.slnx and .csproj refs clean. Build + 30 relevant tests green.
- A2 (done): Audited. CompanySkillOrchestrator uses direct Install (trusted "system" bootstrap for auto skill synthesis — documented + commented). AutomationNeuron.DefineReactionAsync marked low-level/unsafe (public paths stage via rail). No other high-risk user paths found bypassing in current tree. Bootstrap exceptions documented per original plan.
- A3 (optional later): Stale demo literal cleanup after gallery lands.
- Verification (done for A1): build clean; `dotnet test ...CoreBoundary|Architecture|TelegramChat|FlutterUi` (30 passed).

**Phase B: Google + Salesforce Polish + Modern Intent (highest product value)**
- B1 (done): Introduced `InoIntentClassifier.cs` (fast keyword replacing Regex + async LLM structured via GetResponseAsync). Removed Regex from InoConnectorIntents. Wired LLM classify into Gmail/SF handlers + Handle* methods (confirmation before auth/fetch). 28+ Ino/Gmail/SF tests green. Classifier available for all intents.
- B2 (complete): Richer G/SF follow-ups landed + continued (journal MemorySummary + explicit GmailMessagesReady signal lookup, GetLast* hardened for cross-turn + workspace + signals, followup detection for "summarize that one"/"the one", cross G->SF journal follow-ups (use last gmail mem to reason SF suggestions without re-fetch or cred), surfaces use Tile/List + multiple actionable buttons (Summarize last + Find related in Salesforce) now emit proper InoRequest follow-up prompts (classifier fast-path order fixed to support cross prompts), 4+ focused characterization tests + surface button asserts + broader coverage (24+ Ino/Gmail tests green). Registry + caps still keyword Retrieve (vector next).
- B3/B4: "just chat" characterization + e2e; vector next.
- Next: advance Slice B (vector retrieval for capabilities using Context.Recall), or D3 gallery tests.
- Verification this slice: build 0 errors, relevant tests passed, aspire doctor clean. G/SF follow-ups + Ino journal paths.

**Phase C: Runtime User-Controlled LLM**
- C1 (started): Global override via "system"/"llm" pack config (llm_provider + llm_key) now honored first in LlmResponderNeuron.Resolve (before per-ask or composition default). This is the persisted user-controlled mechanism (settable via existing config forms or future dedicated settings surface).
- C2 (done this turn): Added "llm_settings" intent to classifier. Ino now detects "llm settings"/"change model" etc and delivers rich UiKit settings surface (headings, texts, buttons for choices). Also extended Ino's Reason*WithLlmAsync to ResolveGlobalLlmClientAsync (honors system/llm config like responder). Global selection now affects Ino too. Settings buttons functional via prompt/config set.
- Verification: build clean. Full selection tests + surface in follow-up.

**Phase D: UiKit Gallery**
- D1 (started): Added `DigitalBrain.Ui.Runtime/UiKitGallery.cs` — `Build()` returns a UiWidgetTree demoing the main vocabulary items (Text, Button, Table, GraphCanvas, etc.). Ready to be emitted by any neuron (Ino, dedicated experience, or ui-kit pack).
- D2 (progress): Wired via Ino classifier ("uikit gallery") -> DeliverUiKitGallerySurface using UiKitGallery.Build(). Gallery now reachable by chatting to INO. 
- D3 (progress): Expanded with Row/Column/Dialog/Toast demos. Tests pending.
- Verification: build of Ui.Runtime clean.

**Phase E: Simple Chat-to-Automation**
- E1 (progress): richer LLM prompt + improved parse in Ino handler + cap registration on apply. Ino proposes staged automations from chat.
- E2/E3: Proposals via rail. Next: MCP integration from Ino, feedback surfaces.
- Verification: Ino tests green.

Cross-cutting: All new behavior creation uses rail. Update docs.

**Recommended commit order:** Trash merges -> Intent classifier skeleton (Ino only) + G/SF polish -> LLM settings surface + wiring -> Gallery -> Chat automation -> Full intent + vector index.

## Architectural Thinking: LLM Identifies Intent + Vector/Graph (clean, not big-prompt)

**Problem with naive "call LLM with big prompt":**
- Unreliable structure (hallucinated capability ids).
- No grounding in actual registered surface (stale after install).
- Expensive on every message.
- Hard to test / fallback deterministically.
- Doesn't compose with journals/self-evo.

**Proposed clean architecture (hybrid, journaled, small):**

1. **Capability Registry (source of truth):**
   - Every installable/automation registers capabilities on embodiment/apply:
     - id (stable), tier (gmail | salesforce | automation | generic | ui), description, example prompts, handled signals/synapses, input schema hints, risk level, pack/automation origin.
   - Stored journaled (e.g. CapabilityRegistered synapse consumed by a CapabilityIndex grain or projected into Context).
   - Self-evolution apply handlers (marketplace, automation) emit registration on success.
   - Small N (dozens), cheap.

2. **Index (vector + graph):**
   - Primary: reuse DigitalBrain.Context / Qdrant (already wired via NomicEmbedText in AppHost). Embed "description + examples".
   - Secondary / cache: in-memory or lightweight grain projection for hot path (recent workspace + global).
   - Optional graph: simple "capability depends on" or "co-occurred with successful intent" edges (for future planner). Start with flat retrieval.
   - Update path: on SelfEvolutionApplyResult success for relevant ApplyVia, or on pack NeuronActivated, re-embed/register.
   - Fallback: keyword index if vector down.

3. **Classifier (orchestrated by Ino or dedicated IntentNeuron):**
   - On user request (InoRequest or Signal):
     - Retrieve top-k (vector similarity + filters by scope/workspace).
     - Prompt LLM (structured output, via existing AskLlm or direct IChatClient) with:
       - User prompt
       - Retrieved capabilities (id, desc, 2 examples each)
       - Current context summary (from Ino journals)
       - Instruction: return JSON { intent: "gmail.list" | "salesforce.query" | "automation.create" | "generic", params: {...}, confidence: 0-1, rationale }
     - Or use tool-calling if provider supports (future).
   - Hybrid rules (cheap pre-filter): if obvious keywords still present as guard, but LLM wins.
   - Confidence threshold: >=0.7 dispatch; else generic LLM + "did you mean X?" surface (offer quick actions).

4. **Composition with InoNeuron:**
   - Ino stays orchestrator + memory (journals, summaries).
   - Extract/replace the handler chain: first call classifier (or new IIntentClassifier), then route to specific (GmailHandler etc.) or Generic.
   - Gmail/SF handlers remain, now fed structured params from classifier (query, count, filters) instead of re-sniffing prompt.
   - Pending auth still works: classifier can emit "needs_auth: gmail" as special intent.
   - Memory: successful (prompt -> classified intent -> outcome) stored as MemorySummary or dedicated IntentSuccess for future retrieval (few-shot).

5. **Fallbacks & Safety:**
   - Classifier failure / low conf -> existing generic LLM path (no regression).
   - Unknown capability -> generic + log for review.
   - All automation creation still stages proposal (even if classifier proposed it).
   - Testable: mock classifier, seed fixed capability set, snapshot outputs.
   - Cost control: cache classifications per (user/workspace, recent prompt hash) for 5-10min; batch embeds.

6. **Tradeoffs:**
   - Latency: +1 LLM + 1 vector query (~200-800ms). Mitigate with cache + small model for classification if available.
   - Accuracy: far better than regex on paraphrases; grounded so won't invent "drive.list" if Drive dead.
   - Durability: index can be ephemeral (rebuild on restart from registry journals) or persisted.
   - Complexity: one new small grain + registration interface. Worth it for "magical" + future (Ino creates neurons).
   - Alternative start: pure LLM structured with capability list in prompt (no vector) for v0, add retrieval when N>20 or accuracy dips.
   - Graph later: for multi-step ("do X then Y if Z") planner.

7. **Where it lives:**
   - Registry + classifier logic: DigitalBrain.Kernel/Ino/ or new Intent/ subfolder.
   - Index update hooks: in apply handlers + SystemNeurons.
   - Reuses: ContextNeuron vector store, LlmResponder/AskLlm path, SelfEvolution audit.

This keeps mental model small, stays within neuron/synapse/self-evo law, accelerates the "chat is the UI" direction.

## Concrete Next Slices + Verification Steps (Updated 2026-07-06 post-progress)

Current state snapshot (after multiple sessions):
- A (trash): Complete (remnants deleted, thin wrappers merged in source, docs cleaned).
- B (G+SF + intent): B1 complete (full classifier, regex killed). B2 advanced-to-complete (query param extraction via classifier, Tile/List/Tile surfaces, summarize-last/that-one without re-fetch using MemorySummary journals, cross-turn context via journal lookup + recent req, actionable buttons wired via synapseType+InoRequest to trigger journal follow-ups, enhanced detection, 2 new characterization tests + broader coverage). Registry + caps still keyword Retrieve (vector next).
- C (LLM control): Complete (system/llm pack config + Ino/LlmResponder resolver + settings surface + functional buttons with dynamic current + set feedback).
- D (gallery): Complete (UiKitGallery + Ino invocation).
- E (automation): Progress (Ino detects + LLM proposes + stages via rail; MCP parser improved; cap registration).
- Intent arch: Registry + CapabilityRegistered + projection in Ino + caps remembered to Context (embeddings via Remember/Recall) + Retrieve now uses RecallAsync top-k + keyword merge (Slice B basic vector index done). Full use in dispatch/handlers + "did you mean" in later slices.

**Prioritized Next Slices** (small, independently buildable + verifiable; focus on G+SF magic, automation simplicity, full modern intent per arch thinking):

**Slice A (high value, G+SF polish - B2/B3): Richer follow-ups using memory/journals** (DONE in this + continuation session)
- In Ino: support "summarize the last email" / "summarize that one" even without current fetch (lookup last GmailMessagesReady or MemorySummary from journals; pass to LLM). **Implemented + hardened GetLast* (now includes explicit signal scan + cross mem fallback) + cross-turn recent-req logic.**
- Enhance Gmail/SF handlers to preserve/reuse context across turns. **Done (shared logic, workspace filter, recent mem fallback + cross G->SF logic using gmail journal bodies for SF-related prompts without cred/fetch).**
- Richer surfaces: actionable rows/buttons that trigger follow-up intents (e.g. "save to Salesforce"). **Done: multiple buttons (Summarize last message + "Find related in Salesforce") using synapseType+InoRequest+prompt; added cross button to Gmail surface; classifier adjusted for cross prompts.**
- Add 3-5 characterization tests for follow-up flows. **4+ focused tests (summarize last no-refetch, generic "that one", cross G->SF journal use, button structure asserts in fetch surface); 24+ green.**
- Verification: targeted Ino/Gmail/Salesforce tests (repeat); build; aspire doctor. **All passed repeatedly (dotnet build clean; tests 24 passed for filter; aspire doctor green).**

**Slice B (intent arch - B2 + vector): Capability registry + basic index** (implemented)
- Core: `CapabilityRegistered` synapse already journaled (id/desc/examples/tier/origin); emissions in apply handlers (automation + marketplace).
- Classifier: registry projection from journals on Ino OnActivate (LoadCapabilitiesFromJournal + Register); caps Remembered to Context for embeddings.
- Update `ClassifyWithLlmAsync` + `RetrieveCapabilities` to use real top-k retrieval: introduced RetrieveCapabilitiesAsync that does keyword + Context.RecallAsync("prompt", top) , parses "capability:ID" recalled texts, merges+dedups for grounded LLM prompt. Sync fallback preserved.
- Added registry test + exercised via Ino intent paths.
- Verification: new registry test + Ino tests green; build clean; aspire doctor. (Vector path degrades gracefully to keyword when no embedder in test silo, as per IContextNeuron design.)

**Slice C (LLM control polish - C2): Make settings surface functional** (implemented)
- Wired "set-llm:..." : buttons use InoRequest prompts; early check in HandleAsync before llm_settings surface return so sets execute (HandleLlmSetCommandAsync with improved parsing for "set-llm:xxx" and aliases).
- Basic UI feedback: DeliverLlmSettingsSurfaceAsync now reads current "system"/"llm" via IPackConfigStore and shows "Current active provider: xxx" dynamically.
- After set: confirmation reply + re-deliver settings surface (so UI reflects new current immediately).
- Test: new LlmSettings_Surface_And_SetCommand test exercising surface + set flow (52+ Ino/Llm tests green).
- Also affects LlmResponder (which already prioritizes system/llm config) + Ino ResolveGlobalLlmClientAsync.
- Verification: targeted tests + build + aspire doctor.

**Slice D (E + automation simplicity): Deeper Ino-driven automation**
- Enhance `HandleAutomationCreateIntentAsync`: better LLM prompt + parsing for high-quality when/target/script (support G/SF examples).
- After staging, deliver rich feedback surface (proposal id, rationale, "approve to activate").
- Optional: have Ino call MCP `create_automation_from_description` tool for sugar.
- Verification: automation + Ino + self-evo tests.

**Slice E (D3 + quality): Gallery expansion + tests**
- Expand `UiKitGallery.Build()` with more components/states/variants.
- Add BundleHarness + characterization tests asserting trees (D3).
- Register as discoverable surface/experience.
- Verification: gallery-specific tests; manual surface delivery.

**Slice F (full vector + arch): Complete intent modernization**
- Per "Architectural Thinking" section: full registry on applies/NeuronActivated.
- Index: use Context/Qdrant for embeddings (reuse Nomic); projection grain or in classifier.
- Update Ino dispatch + handlers to use structured params from classifier.
- Add "did you mean" fallback surface for low confidence.
- Verification: end-to-end intent tests; cost/latency notes.

**Overall verification after every slice (mandatory):**
- `dotnet build Brain.slnx -p:SkipFlutterBuild=true -p:SkipDeployBuild=true`
- `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "<touched area or Ino|Gmail|Automation>" --no-build`
- `aspire doctor`
- When touching rail/LLM/distributed/Context: targeted broader run.
- Update this plan doc + todos after each slice.
- Commit clean changes with plan reference.
- Use Context7 before any new Orleans/Aspire/Microsoft.Extensions.AI/Context usage.

**Decisions tracked:**
- Intent index reuses Context/Qdrant (2026-07-06).
- LLM selection via pack-config first.
- Registry is journaled (CapabilityRegistered) for durability/audit.
- All user mutations go through self-evo rail.

Next agent session: Slice C (LLM settings polish) **complete** (dynamic current in surface, fixed set button execution, re-deliver feedback, tests). Next: Slice D (deeper automation) or Slice F (full classifier modernization + structured output). Re-read plan. All mutations via rail.

End of continuation note. (Plan list of next actions maintained here and in todos.)
