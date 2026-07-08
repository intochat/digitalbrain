# PR #11 Fix Plan — All Review Findings

**Status**: Planning complete (ritual followed). Ready for approval before any edits.
**Date**: 2026-07-08
**Base**: ino-implementation-plan branch, PR #11
**Ritual completed**:
- Context7 used for: C# static virtual interface members (IAgent pattern validated), Orleans journaling / JournaledGrain patterns / deriving state from events / no static mutable / GetCausalLineage (analogous to custom Neuron journals), Microsoft.Extensions.AI via /dotnet/extensions.
- `aspire doctor` (MCP + CLI): 5 passed, 0 warnings, 0 failed.
- `aspire__list_resources` attempted (no AppHost running — expected).
- `todo_write` used throughout.
- 5 steps applied in order below.
- Relative paths only. No C:\Users\ references.
- Tests always full root `dotnet test --logger "console;verbosity=minimal"`.

This plan addresses **every finding** from the posted review comment (blockers first). It follows repo CLAUDE.md + the proposals this PR claimed to implement.

## 5 Steps Applied to the Fixes (in order)

1. **Make requirements less dumb**:
   - Questioned: "Why any string heuristics or global statics for system facts when journals + IAgent metadata + typed records exist?"
   - Traced to specific: proposals/implementation-plan.md (Phase 1: "Capabilities come from typed catalog records, not LLM guesses", "fake test IAgent without editing classifier", "no Ino classifier edit"), review.md, planofactions.md (explicit "Stop treating _caps as source of truth", "delete static list duplication").
   - Challenge assumptions: The PR impl re-introduced exactly what the plans (and CLAUDE.md "delete trash", "structured over string") said to remove. "Do we need phrase lists for 'what can you do'?" No — data + narrow policy.
   - "Is full new catalog grain required now?" No — journals + existing CapabilityRegistered + IAgent metadata suffice for the slice (per original plan Phase 1).
   - "Does Ino need to own G/SF glue forever?" No — thin conductor + handlers.

2. **Delete first (target >10% net reduction)**:
   - Delete all growing English phrase lists (IsCapabilityQuestion 10+ || , IsExternalMemory 6 contains, classifier keywords, InoNeuron p.Contains for routing/approve/llm/gmail, GetLast topic filters, IsExplanation, TryExtract slicing).
   - Delete `private static readonly List<Capability> _caps`, `RegisterCapability` mutation API (and all callsites: InoNeuron, AutomationDefinitionApplyHandler, tests).
   - Delete explicit `KnownAgentRecords` hardcoded array (or reduce to bootstrap only; drive from journals).
   - Delete `IsExternalMemory` + all topic sniffing.
   - Delete duplicate/redudant code (sniffers in multiple files).
   - Delete overfit test data/setup ( "last-gmail", direct static Register in tests, exact phrase asserts that don't prove policy).
   - Expected net: 150-300 LOC removed (heuristics + statics + tests), much lower future maintenance, no more global pollution.
   - If not adding back ~10%, we didn't delete enough.

3. **Simplify or optimize (what remains)**:
   - Journal (CapabilityRegistered, MemorySummary) + structured IAgent metadata (NeuronAgentMetadata.ReadFrom<T>) is the single source.
   - Capability answers: pure function over current journaled records + metadata.
   - Trust: declared by producer at creation (MemorySummary enriched with optional fields), consumed in packet builder (no inference).
   - Classifier/answerer: receive or query the slice (no ownership).
   - InoNeuron: conductor that asks handlers + catalog projection + context packet. Move glue out via existing IInoIntentHandler pattern.
   - Redaction: applied at ingress + render boundaries.
   - No new grains for this (journals suffice; defers full SystemCatalogNeuron).

4. **Accelerate cycle time**:
   - Small slices (see below), each followed by: build, `dotnet test --logger "console;verbosity=minimal"` (bg + poll via logs/traces), `aspire doctor`, targeted `aspire__execute_resource_command` restart + `aspire__list_*` if AppHost up.
   - Leverage existing `GetCausalLineageAsync`, journal replay, ContextPacketSelected for verification (fast feedback, no full runs).
   - Delete waste: no broad reflection, no Qdrant yet, no new AppHost resources.

5. **Automate last**:
   - Only after clean: optional startup registration helper for IAgents (still journaled), or signal-driven. Never before heuristics deleted.
   - Self-evolution for any future WoW changes to this plan.

## Findings Grouped into Fix Slices (prioritized by severity + delete impact)

All 9+ findings mapped. Prefer structured data (records, declared metadata, journal events) over any string matching.

**Blocker Slices (do first)**

### Slice 1: Static mutable global state + ownership (Blocker #1, high impact delete)
- Findings: static _caps + Register in InoIntentClassifier.cs, mutations from InoNeuron (OnActivate, Load, Remember), AutomationDefinitionApplyHandler, tests. Cluster inconsistency, test pollution, violates plans.
- Approach: Journals are truth. Remove the List entirely. Load from CapabilityRegistered + IAgent metadata. Answerer/classifier become pure or receive list. No public mutation.
- Delete: the List, RegisterCapability body/public API, all Add calls.
- Files (relative):
  - integrations/DigitalBrain.Ino/InoIntentClassifier.cs
  - integrations/DigitalBrain.Ino/InoNeuron.cs (OnActivate, LoadCapabilitiesFromJournal, RegisterKnown..., Remember..., TryHandle...)
  - integrations/DigitalBrain.Ino/InoCapabilityAnswers.cs (minor)
  - src/DigitalBrain.Kernel/AutomationDefinitionApplyHandler.cs
  - tests/DigitalBrain.Ino.Tests/InoAwarenessTests.cs
  - tests/DigitalBrain.Tests/Ino/InoNeuronChatSurfaceTests.cs (and any other)
- Structured replacement: `IReadOnlyList<InoCapabilityRecord>` or `Capability` passed or loaded via journal query in grain. Use existing `HasCapabilityRegistration` pattern generalized.
- Acceptance: No static List remains. Capabilities visible only via journals. Tests use fresh state or explicit journal setup. "Fake IAgent" works without global side effect.
- Self-evo: N/A (no mutation path changed; registration stays via journals).

### Slice 2: Hardcoded string heuristics for capability questions (Blocker #2)
- Findings: InoCapabilityAnswers.IsCapabilityQuestion + TryExtract (long English list + slicing). Bypasses deterministic for non-exact phrases. LLM can invent.
- Approach: Delete the list. Make deterministic path (TryCreateAnswer over real records) the default or always-attempted first gate before generic LLM. Use registered ids/aliases for "do you have X" detection (structured match on data). Minimal canonical inventory triggers only if needed (target delete most).
- Delete: entire IsCapabilityQuestion method body list, TryExtract ad-hoc parser, related Contains in classifier and InoNeuron early paths.
- Structured: Capability answerer always consulted with current journaled/agent records. Detection can be "mentions any registered cap id/alias + question words" or always try for cap answers first. Tie to catalog data.
- Files:
  - integrations/DigitalBrain.Ino/InoCapabilityAnswers.cs
  - integrations/DigitalBrain.Ino/InoIntentClassifier.cs (uses it)
  - integrations/DigitalBrain.Ino/InoNeuron.cs (TryHandle + early strings)
- Tests: Add theory with phrase variants + "mentions registered id". Prove no LLM for cap questions (capturing client). "do you have Jira" stays closed even on paraphrase.
- Verify: MCP ino_interact + "what can you do?" and variants still deterministic, source: IAgent.

### Slice 3: Hardcoded topic sniffing for trust + weak provenance (Blocker #3 + high)
- Findings: InoContextPacketBuilder.IsExternalMemory (gmail/email/...), duplicated in GetLast*, MemorySummary has no provenance, packets not extensible.
- Approach: Declare at source. Enrich MemorySummary (add optional fields with defaults for compat). Producers set it. Builder consumes metadata. Delete all sniffing.
- Delete: IsExternalMemory entirely + all topic.Contains in builder/GetLast*/tests.
- Structured:
  - MemorySummary(..., string? SourceKind = null, string? TrustLevel = null, string? Origin = null)
  - In packet: map to InoContext* enums using declared values (fallback removed after sites updated).
  - Update InoContextItem etc if needed for richer.
- Files:
  - src/DigitalBrain.Core/Synapses/InoSynapses.cs (MemorySummary)
  - integrations/DigitalBrain.Ino/InoContextPacket.cs (builder + delete func)
  - integrations/DigitalBrain.Ino/InoNeuron.cs (all MemorySummary ctors + GetLast* + summary creation)
  - tests/DigitalBrain.Ino.Tests/InoAwarenessTests.cs (packet test)
- Producers declare: gmail/sf/upload = UntrustedEvidence + origin; schema/db = JournalFact/System.
- Acceptance: Trust in packet comes from metadata. Changing topic text does not affect. New source type works without editing builder.
- Orleans note (from Context7): state derived from journaled events/synapses — consistent.

**High slices**

### Slice 4: Redaction insufficient + leaks (High)
- Findings: SecretText narrow regex, late application, raw bodies in InoResponse/surfaces/MemoryStored, ContextNeuron, traces.
- Approach: Ingress + multiple layers. Strengthen regex. Redact before journal/response for external. Apply in Remember for untrusted.
- Delete: any "TODO redact" or duplicated marker lists if any.
- Files:
  - integrations/DigitalBrain.Ino/SecretText.cs
  - integrations/DigitalBrain.Ino/InoNeuron.cs (fetch paths, MemorySummary for gmail/sf, LLM prompts already good — harden)
  - src/DigitalBrain.Kernel/Grains/ContextNeuron.cs (Remember path)
  - Possibly GmailNeuron / Salesforce if bodies returned raw (read to confirm).
- Tests: Extend Capturing + new secret-in-gmail-body test asserting absent everywhere (InoResponse, packet, recalled, prompts).
- Note per PR: OTEL policy separate (don't change global here).

### Slice 5: InoNeuron orchestration bloat + LLM invention not fully closed (High)
- Findings: 1900+ LOC god object, many strings, action orchestration stub, cap invention only patched.
- Approach: Use/extend handler pattern. Cap gate always structured. Orchestrate remains minimal (no LLM invented actions auto-exec).
- Delete: inline strings for early returns where possible via handlers or cap data.
- Files:
  - integrations/DigitalBrain.Ino/InoNeuron.cs (refactor Handle, move logic)
  - integrations/DigitalBrain.Ino/InoIntentHandlers.cs (add cap/explain handlers if not)
  - integrations/DigitalBrain.Ino/InoCapabilityAnswers.cs + InoExplanationFormatter.cs
- Future: full validation in planner (Phase 5 of original).

### Slice 6: Tests overfit + self-evo rail side effects (High/Med)
- Findings: exact phrase tests, "last-gmail" for trust, static pollution in tests, apply handler still mutates (after rail).
- Approach: Journal/catalog state driven tests. Remove mutation side effects.
- Delete: overfit asserts + setup.
- Update apply handler (after Slice 1).
- Verify rail: after changes, use MCP get_causal_lineage + timeline for proposals/decisions; no new apply paths.

## Detailed Implementation Strategy per Slice (tradeoffs considered)

**Tradeoffs**:
- Journals vs new grain: Journals win for slice (existing, replayable, no new AppHost, matches "use existing CapabilityRegistered"). Full catalog later via self-evo proposal.
- Keep some strings? Only minimal canonical after delete attempt. Data-driven match on registered ids preferred.
- Breaking MemorySummary? Optional fields + defaults = non-breaking for deserial (Orleans + [GenerateSerializer]).
- InoNeuron size: handlers pattern already exists — extend it (low risk).
- Redaction: defense in depth (ingress + packet + prompt) vs one place.
- Tests: full root no-filter as mandated. Use TestKit harness.

**Per-slice steps (when executing after approval)**:
1. Ritual (Context7 if new API touched, doctor, todo).
2. Edit only the slice files (small diff).
3. Build.
4. `dotnet test ...` (bg).
5. doctor + inspect (packets, registered in timeline via MCP or get_timeline).
6. Commit slice or PR stack.
7. Retro: which 5 step skipped?

**Rollback per slice**: Revert commits; journals are durable.

## Verification & Success Criteria (all findings)
- All static mutable gone for caps.
- No topic string trust decisions.
- Cap answers deterministic from IAgent + journals for inventory + "do you have <id>" (even paraphrased).
- External memories marked UntrustedEvidence via metadata.
- Secrets redacted at boundaries; no leak in tests/packets/prompts/responses.
- Tests prove policy (data -> behavior), not strings. Phrase variants covered.
- InoNeuron smaller or same responsibility via delegation.
- Self-evo rail untouched (proposals/decisions still only path for mutations).
- Full test green + doctor green.
- MCP checks (ino_interact cap questions, get_causal_lineage, get_timeline for ContextPacketSelected/CapabilityRegistered) pass as before or better.
- No new string heuristics added.

## Order & Sizing
1. Slice 1 (statics) — biggest win.
2. Slice 3 (trust/provenance) — pairs with packets.
3. Slice 2 (heuristics) — now that data is clean.
4. Slice 4 (redaction).
5. Slice 5+6 (bloat + tests + rail verify).
Small verticals, high signal, delete heavy.

## Post-fix
- Update any living docs only via rail if needed (CLAUDE.md changes rare).
- Delete pr11-review-findings.md + this plan after merge (trash).
- Run full verification cycle.

This plan is concrete, skeptical, structured-first, and directly remediates every item in the review.

(End of plan. Exit plan mode to present.)