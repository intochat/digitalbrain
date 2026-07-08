# Strict Code Review: PR #11 (ino-implementation-plan)

Findings ordered by severity (blocker > high > medium > low). Concrete, file:line references from the changed code. Focus on design/implementation problems vs. the approved plans in `proposals/implementation-plan.md`, `proposals/review.md`, `proposals/planofactions.md`, and `proposals/ino-system-awareness-proposals.md`.

## Blocker

### 1. Static mutable global capability list creates distributed state, test pollution, and concurrency hazards
- **Severity**: blocker
- **File:line**: `integrations/DigitalBrain.Ino/InoIntentClassifier.cs:14` (`private static readonly List<Capability> _caps = [...]`), `23` (`RegisterCapability` does `_caps.Add`), `21` (`public static IReadOnlyList<Capability> Capabilities => _caps`)
- **Why real bug/design risk**: 
  - Mutable static List mutated from `InoNeuron.OnActivate` (every ino grain activation), `AutomationDefinitionApplyHandler.ApplyAsync:40`, `InoNeuron.Load.../Register...`, and directly in tests (`InoAwarenessTests.cs:30`, `InoNeuronChatSurfaceTests.cs:175+`).
  - Orleans distributed: each kernel replica has independent static; replicas diverge.
  - No synchronization, no scoping (user/workspace), survives across test cases.
  - Directly contradicts the plan: "Stop treating `InoIntentClassifier._caps` as the source of truth. Keep it only as a projection/cache during transition." (planofactions.md:69, implementation-plan.md:112, review.md:159). The impl made the projection the practical owner again.
- **Proper fix**: Eliminate the static List. Load capabilities on-demand from journaled `CapabilityRegistered` synapses (or future `SystemCatalogNeuron`). Provide a scoped non-static projection inside grains or a read-only snapshot service. `RegisterCapability` becomes journal emission only. Remove public mutation API or gate it behind catalog.
- **Suggested test coverage**: 
  - Multi-activation / multi-grain isolation (different "ino-*" grains do not share mutations).
  - Test assembly isolation (one test's Register does not affect another's assertions on Capabilities).
  - Cluster simulation: two silos see same registrations only via journals, not statics.
  - Concurrent Register calls.

### 2. Hardcoded English string heuristics control capability detection and bypass the deterministic path
- **Severity**: blocker
- **File:line**: `integrations/DigitalBrain.Ino/InoCapabilityAnswers.cs:3-18` (`IsCapabilityQuestion`: `p.Contains("what can you do") || ... || p.Contains("do you have ")`), `79-102` (`TryExtractRequestedCapability` string slicing + prefix stripping on "do you have").
- **Why real bug/design risk**:
  - The plan requires: "Capabilities come from typed catalog records, not LLM guesses.", "Ino can answer capability/status questions without calling an LLM.", "The LLM cannot invent a new capability in this path." (implementation-plan.md Phase 1).
  - Current: only common phrases route to `TryCreateAnswer` (which then answers from `InoAgentCapabilities.KnownAgentRecords` + projection and says "No" for unknown). Any other phrasing (e.g. "list available integrations", "what integrations are wired", "can I use email?") falls through to `HandleGenericIntentAsync` + LLM classify + `BuildContextAsync` + `ReasonWithLlmAsync`. LLM can then invent or hallucinate capabilities.
  - Also used in classifier for early "capability_status" (InoIntentClassifier.cs:49).
  - This is exactly the ad-hoc prompt matching the plans said to delete/replace with catalog query.
- **Proper fix**: Route capability questions via structured means. Add explicit `CapabilityQuery` intent or let the catalog answer "list" / "has X" deterministically from a `ListCapabilities` / `GetCapability` call before any LLM. Remove or deprecate phrase list; at most keep a tiny last-resort fallback after catalog miss. Make "is capability question" a catalog-driven decision or a narrow, versioned classifier that itself queries metadata.
- **Suggested test coverage**:
  - Policy tests: given registered cap, both exact phrases AND semantic variants ("tell me integrations", "what's available for mail") produce deterministic non-LLM answer from catalog.
  - Negative: novel phrasing for unknown cap must not produce invented "yes" via LLM path (use capturing client + assert no invented ids in reply).
  - "Jira" case must stay "No" even under paraphrases.

### 3. Hardcoded topic string sniffing for trust classification (external memory)
- **Severity**: blocker
- **File:line**: `integrations/DigitalBrain.Ino/InoContextPacket.cs:116` (in `Build`), `140-149` (`private static bool IsExternalMemory(MemorySummary memory) { var topic = ...; return topic.Contains("gmail") || "email" || "salesforce" || "crm" || "upload" || "document"; }`)
- **Why real bug/design risk**:
  - Plan (multiple): "Every side effect routes through typed grains...", "External or user-provided content is evidence, not instruction.", "Prompt-injected document/email content is marked untrusted...", "trust levels... UntrustedEvidence", "memory trust should come from source/provenance metadata on the memory/context item, not topic text".
  - `MemorySummary` (src/DigitalBrain.Core/Synapses/InoSynapses.cs:39) has only `Topic, Summary, At, WorkspaceId` — no provenance, no `SourceKind`, no `TrustLevel`, no `Origin`.
  - Trust is inferred by brittle substring match in the packet builder. Adding "notion", "dropbox", "web-upload-42" requires editing the heuristic.
  - Also duplicated sniffing in `GetLastGmailBodiesFromJournal` (InoNeuron.cs:1894-1898).
  - Violates "context packets really preserve provenance/trust in a way future sources can extend without more string matching".
- **Proper fix**: When producing `MemorySummary` (Gmail fetch, Salesforce, upload handler, etc.), attach structured provenance. Extend `MemorySummary` (or use a richer envelope) with `SourceKind`, `TrustLevel`, `OriginAgent`, `EvidenceId`. `IsExternalMemory` (or equivalent) becomes `memory.TrustLevel == UntrustedEvidence` or a metadata predicate. Packet builder trusts the metadata. Producers (not Ino builder) decide trust.
- **Suggested test coverage**:
  - Producer test: Gmail path produces MemorySummary with UntrustedEvidence (or equivalent) without relying on topic name.
  - Packet test: trust level comes from the item metadata, not reconstructed from topic keywords. Test new source kinds without touching builder.
  - Regression: changing topic text does not flip trust.

## High

### 4. `InoNeuron` continues to accumulate excessive orchestration responsibility
- **Severity**: high
- **File:line**: `integrations/DigitalBrain.Ino/InoNeuron.cs` (entire ~1900+ LOC grain): `HandleAsync(InoRequest)` has early string checks + capability/explain + gallery + llm_settings + automation_create + approve + generic with gmailFollowup, crossGmailToSf, many p.Contains, `HandleGmailIntentAsync`, `HandleSalesforceIntentAsync`, `HandleAutomationCreateIntentAsync` (LLM JSON extraction + proposal staging), `FetchRecentGmailAsync`/`FetchSalesforce...`, auth surfaces, schema, tabular, `BuildContextAsync`, `OrchestrateActionsIfNeededAsync`, journal scanning helpers, etc. OnActivate also seeds.
- **Why real bug/design risk**:
  - Plan (review.md, implementation-plan.md): "Ino should be the conductor, narrator, and policy gate. It must not become a monolithic god object.", "Do not make `InoNeuron` the owner of all memory, tools, catalog, planning, and evolution."
  - It owns integration-specific glue, auth state machines, LLM summarization policy for G/SF, automation proposal construction, plus generic + context + explain.
  - Makes testing, evolution, and future catalog/context planner hard. Cross-cutting concerns (redaction, provenance) are spread inside.
- **Proper fix**: Continue extraction to `InoIntentHandlers` (already partially present). Move G/SF orchestration details into the respective neurons or thin adapters. Make Ino call typed "capability executors" or planners. Keep Ino focused on: request intake, policy gates (cap check, explain, risk), context packet request, routing to catalog/handlers, journaling response + packet.
- **Suggested test coverage**: Unit tests for InoNeuron become thinner "orchestration only" tests; integration behavior tests move closer to the specific handlers/neurons. Measure LOC or handler count over time.

### 5. Provenance/trust and context packets are not future-extensible; catalog slice is incomplete
- **Severity**: high
- **File:line**: `InoContextPacket.cs:75` (builder), `InoNeuron.cs:1521` (`BuildContextAsync` passes only `InoAgentCapabilities.KnownAgentRecords`), `1529`, `InoSynapses.cs:39` (MemorySummary lacks fields), packet render + Evidence refs are good but source data is not.
- **Why real bug/design risk**: 
  - `BuildContextAsync` ignores the classifier projection and journaled `CapabilityRegistered` for the packet (only Known list).
  - Packet shape (`InoContextItem` with SourceKind/Trust/Correlation) is the right direction (plan Phase 4), but the data feeding it is lossy and string-driven.
  - Future packs/integrations/MCP tools cannot participate without more string hacks or edits to InoNeuron/InoAgentCapabilities.
  - `ContextPacketSelected` is journaled (good), but evidence refs point back to weak sources.
- **Proper fix**: 
  - Enrich memory records at creation with provenance.
  - In packet assembly, union: Known (or better: catalog snapshot) + journaled CapabilityRegistered + other registered components.
  - Make builder take a capability provider interface, not a static list.
- **Suggested test coverage**: Packet for capability question contains the IAgent-derived record with source/trust. Packet after automation apply contains the registered cap. Packet with new source type (injected) carries its declared trust without topic match.

### 6. Redaction is narrow and does not cover all ingress/storage/trace paths; secrets and untrusted content can still leak into LLM context, responses, journals, and OTEL
- **Severity**: high
- **File:line**: `SecretText.cs:9` (only assignment regex for password|...|token + bearer), `InoNeuron.cs:922` (redact only on some MemorySummary), `934` (some but not all summary paths), `1718` (redact only prompt in Reason*), `ContextNeuron.cs:43` (`RememberAsync(text)` -> `MemoryStored(text)` with no redact), `FetchRecentGmailAsync` (raw `summaries` bodies used in `GmailReplyText` + InoResponse + surfaces before/without full redaction), MCP read tools, OTEL note in PR body.
- **Why real bug/design risk**:
  - External Gmail/Salesforce content is "UntrustedEvidence" in packet but raw bodies can appear in InoResponse, MemorySummary (if not hit), recalled vector text, and journaled responses.
  - Regex misses many secret shapes, JSON secrets, headers, etc.
  - Plan requires: "Secrets never render.", "Never include secrets in context packets, summaries, vector payloads, or prompts.", "Tool outputs are sanitized before entering context."
  - LLM can be prompted with (or trained on via traces) secrets or injection from "evidence".
  - Redaction happens too late (after fetch in some flows) or only in packet path.
- **Proper fix**: Redact at the boundary of every external fetch (inside Gmail/Salesforce neurons before returning bodies, or in a response sanitizer). Make redaction part of `MemoryStored` / `MemorySummary` creation for untrusted sources. Strengthen SecretText or use a dedicated sanitizer + structured `Secret` / `Redacted` types. Mark InoResponse bodies from external as redacted. Influence OTEL sampling/redaction policy (separate change). Apply redaction to content going into ContextNeuron.Remember.
- **Suggested test coverage**: End-to-end with real-shaped secret in email body: assert absent from all InoResponse, Memory*, packet render, captured LLM prompts (CapturingInoChatClient), and any journaled synapse text. Test redaction of varied shapes. Test that redacted evidence still supports "I saw X from gmail" without leaking value.

### 7. LLM-invented capabilities are only patched for common phrases, not architecturally prevented
- **Severity**: high
- **File:line**: `InoNeuron.cs:108` (the `if (await TryHandleCapabilityQuestionAsync...)` gate), `InoCapabilityAnswers.TryCreateAnswer:47` (falls to projected string match), generic path + LLM classify always available for other prompts, automation extraction still uses LLM for "when/script".
- **Why real bug/design risk**: Matches the explicit concern. Deterministic gate is string heuristic. Unknown cap answer only happens inside the heuristic branch. LLM classify + generic can still emit capability claims or act on them. Phase 5 hallucination resistance ("Capability claims must resolve to catalog records", "Invalid LLM outputs fail closed") is not yet in place.
- **Proper fix**: All answers about capabilities go through a `ICapabilityCatalog` (or journal query) that never consults LLM for existence. Action planners must validate every referenced capability id against catalog before any execution or proposal. For automation, the extraction is ok if the resulting proposal is always gated (it is).
- **Suggested test coverage**: Given only gmail/salesforce registered, any prompt that could be interpreted as capability question + "do X with jira/unknown" must produce explicit "no registered" or fail-closed without the LLM being allowed to add new ids to its answer. Use structured output validation in future LLM paths.

### 8. Tests overfit the string heuristics and the static projection instead of proving policy + catalog behavior
- **Severity**: high
- **File:line**: `tests/DigitalBrain.Ino.Tests/InoAwarenessTests.cs:39` (packet test deliberately uses Topic="last-gmail" to assert UntrustedEvidence), `tests/DigitalBrain.Tests/Ino/InoNeuronChatSurfaceTests.cs:22` ("hello, what can you do?"), `209` ("Give a one sentence status..."), `220` ("do you have Gmail?", "do you have Jira?"), `239` ("why did you do that?"), plus direct `RegisterCapability` in many tests.
- **Why real bug/design risk**: Tests will pass as long as the Contains strings are present and the static list has the items. They do not prove "answers come from IAgent metadata + journal regardless of phrasing" or "trust from provenance". Adding a new phrase or changing topic naming silently breaks the "policy".
- **Proper fix**: Rewrite tests to set up catalog/journal state, then assert answers and trust levels independent of specific English. Use the fake IAgent path without side effects on shared static. Assert classification intent + answer source.
- **Suggested test coverage**: Phrase-variation matrix, "add IAgent, no edit to any Ino* list, still discoverable", "memory with explicit UntrustedEvidence metadata is treated as such regardless of Topic text".

### 9. Self-evolution rail is used for the new automation path, but side effects and static mutation still leak around it
- **Severity**: medium (but important for "sacred" rule)
- **File:line**: `InoNeuron.cs:738` (stages `SelfEvolutionProposal` for automation_create — correct), `AutomationDefinitionApplyHandler.cs:40` (after decision, does `RegisterCapability` + `Fire CapabilityRegistered`), `InoNeuron.RegisterKnown...` also mutates static outside any proposal.
- **Why real risk**: The core flow respects `Proposal -> Decision -> apply`. However, the apply handler and Ino activation mutate the global classifier as a side effect. This is not journaled truth; the journal (CapabilityRegistered) is secondary. Future self-evo of "awareness" could be affected.
- **Proper fix**: Keep the rail exactly as-is for mutations. Make the capability projection strictly a derived view over journals (or catalog grain) — no mutation in apply handlers or OnActivate beyond firing the journal event. OnActivate should only load, never "Register" into static.
- **Suggested test coverage**: Verify that after approved apply, the effect is observable only via journal replay / catalog query, not via static side effects. Proposal/decision/apply counts and rollback paths exercised.

## Medium / Low

- `InoAgentCapabilities.KnownAgentRecords` is still an explicit hand list (InoAgentCapabilities.cs:48). Adding a third IAgent requires editing it (plan wanted no Ino classifier edit; similar pressure moved here). Prefer registration at startup from discovered contracts or self-registration by the agents themselves.
- Packet builder and many paths still do raw journal `TakeLast(N)` without budgets, provenance filters, or user/workspace isolation in all cases.
- Redaction regex and MCP `SanitizeToolText` are duplicated heuristic lists.
- `InoNeuron` still contains direct `chat.GetResponseAsync` in automation path and multiple LLM job types without the future gateway.
- No enforcement yet that LLM structured outputs for actions are validated against catalog ids before any Orchestrate step.
- Warmup activates ino-main (good), but seeding remains list-driven.
- Proposals/*.md added in the PR (per CLAUDE.md these are usually deleted as noise after decisions; living docs only).

## Proposed Remediation Plan (apply 5 steps)

1. **Make requirements less dumb**: Re-confirm with author that the explicit goal was "no ad-hoc string matching for capabilities or trust" and "catalog/journal as source of truth, not Ino statics". Trace the string lists and IsExternalMemory back to the plan text.

2. **Delete first (target >10% net reduction in heuristics + ownership)**:
   - Delete the phrase lists in `IsCapabilityQuestion`, `IsExplanationQuestion`, `IsExternalMemory`, duplicated Contains in InoNeuron.Classify paths and GetLast* helpers.
   - Delete (or hide) the public `RegisterCapability` mutation and the static `_caps` List (replace with journal-derived or catalog query).
   - Remove central `KnownAgentRecords` hardcoded array; drive from journaled registrations or a small explicit seeder that does not live in Ino.
   - Stop enriching InoNeuron with more G/SF specific orchestration.

3. **Simplify (what remains)**:
   - Capability/status path: always query journaled `CapabilityRegistered` (or small catalog projection) + IAgent metadata records. Deterministic answerer takes a capability provider.
   - Trust: producers declare it when emitting MemorySummary / evidence. Builder trusts the declaration.
   - Context packet: take full set of registered caps from the journal projection, not a static list copy.
   - InoNeuron: thin conductor that delegates to handlers + catalog + context planner.

4. **Accelerate**:
   - Use existing journal replay + `GetCausalLineageAsync` (already leveraged for explain — good).
   - Targeted restarts + `aspire__*` + `dotnet test` (minimal) for verification of fixes.
   - Make the deterministic paths the fast path with no LLM and no statics.

5. **Automate last**:
   - Only after the above: add assembly scan or signal-driven registration for new IAgents, full catalog grain, richer provenance on all memory items, LLM gateway with catalog validators.

**Immediate concrete next actions (no code until approved)**:
- Replace `IsCapabilityQuestion` with a narrow structured classifier or "starts with inventory intent" + catalog contains check.
- Extend `MemorySummary` (or introduce `EvidenceMemory`) with trust/provenance fields; update 2-3 producers.
- Scope or remove the static list; load only from journals in OnActivate and answer paths.
- Update/add 6-8 tests that assert policy over exact phrases and that do not call RegisterCapability on shared static.
- Audit every place external content enters journals/responses/prompts and insert redaction + provenance at ingress.
- Confirm via `dotnet test --logger "console;verbosity=minimal"` + `aspire doctor` + MCP lineage/packet inspection after each slice.

All findings are directly observable in the PR diff files. The architecture intent in the accompanying proposals is clear; the implementation re-introduced multiple ad-hoc mechanisms the plans set out to remove.

(End of review comment body. Post via gh.)
