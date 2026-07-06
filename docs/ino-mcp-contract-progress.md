# INO MCP Agent Interface & Common Contract — Progress Tracker

**Date started:** 2026-07-06  
**Owner:** Ongoing (AI-assisted + human)  
**Related:**  
- `docs/architecture-trash-analysis-2026-07-06.md`  
- Recent InoNeuron improvements (direct responses, clientId scoping, automation proposals as apps, classifier, self-evolution rail)  
- `DigitalBrain.Mcp/` stdio server + Aspire MCP wiring  
- Goal: High-quality exposure of INO so external agents (Claude Code, Codex, Grok CLI, etc.) can drive + **verify** the system live via MCP. Use the same contract in tests for regression protection on ship.

## Why This Matters (Product Shaping)

INO is the personal assistant / orchestrator:
- Direct visible answers (fixed "I'll start that task:" prefix bug for jokes etc.).
- Natural language intents (Gmail, Salesforce, automations, gallery, LLM settings).
- Creates rich proposals (with Run/Approve buttons) through the approval rail.
- Scoped via clientId/workspaceId (multi-actor safe).

Exposing via MCP is **not** "just another chat tool". It is a **first-class agentic interface** to the DigitalBrain OS.

**Dumb solution (rejected):** Raw `ask_ino(prompt) → string`. Brittle, unobservable, no rail visibility, no scoping, tests diverge from real agent usage.

**Extremely good solution (this effort):**
- Common serializable contract (`InoInteractRequest` / `InoInteractResult` / `InoAction`).
- Structured, observable, actionable output (ResponseText + intent + AvailableActions + PendingProposals + memories).
- Scoped by default (`client_id` for isolated verification sessions like `claude-arch-test-42`).
- MCP tools + tests speak the **same language** → live verification + automated guards.
- External agents can script full loops: interact → inspect proposals/actions → approve/run → assert invariants.
- When we ship features, the MCP view + contract tests prove they still work.

This turns MCP into an executable spec and live harness for the new architecture.

## The Common Contract (Defined in DigitalBrain.Core)

```csharp
// InoAction — mirrors buttons emitted in proposals, gallery, etc.
public record InoAction(
    string Label,
    string? FollowUpPrompt = null,
    string? SynapseType = null,
    IReadOnlyDictionary<string, object?>? Props = null
);

// Request
public record InoInteractRequest(
    string Prompt,
    string? ClientId = null,
    string? WorkspaceId = null,
    bool IncludeProposals = true,
    bool IncludeActions = true,
    int MaxHistory = 5
);

// Result — the standardized output for agents + verification
public record InoInteractResult(
    string Prompt,
    string ResponseText,                    // Direct answer (regression anchor)
    string? ClassifiedIntent = null,
    double IntentConfidence = 0.0,
    string? ClientId = null,
    string? WorkspaceId = null,
    IReadOnlyList<string> UsedTaskIds = null!,
    IReadOnlyList<string> RecentMemoryTopics = null!,
    IReadOnlyList<InoAction> AvailableActions = null!,  // "Run now (preview)", "Approve & activate", etc.
    IReadOnlyList<SelfEvolutionProposalPending> PendingProposals = null!,
    DateTimeOffset Timestamp = default
);
```

**Key properties for verification:**
- `ResponseText` — must be the real content (no prefixes).
- `AvailableActions` + `PendingProposals` — prove rail + app-like automation surfaces.
- Scoped fields — prove multi-actor isolation.
- Intent — exercises classifier.

See `DigitalBrain.Core/Synapse.cs` for the full generated records.

## Implementation Status (Checklist)

- [x] Define common contracts in Core (additive, serializable).
- [x] Extend `IInoNeuron` with `InteractAsync(InoInteractRequest)`.
- [x] Implement rich collector in `InoNeuron.InteractAsync`:
  - Fires scoped `InoRequest`.
  - Collects direct `InoResponse`.
  - Runs classifier.
  - Pulls scoped `MemorySummary`.
  - Pulls recent `SelfEvolutionProposalPending` (the rail).
  - Derives `AvailableActions` from context + new architecture patterns.
- [x] Update legacy `AskAsync` to delegate (compat).
- [x] MCP exposure:
  - `ino_interact` (primary rich tool, returns JSON of `InoInteractResult`).
  - Refreshed `ask_ino` as thin wrapper.
  - Good descriptions with examples for external agents.
- [x] Test integration:
  - Added contract verification test in `InoNeuronActionDirectiveTests` (asserts direct answer, no prefix, clientId, actions present).
  - All `Ino*` tests (25+) pass under narrow filter.
- [x] Add reusable `InoTestHarness` in `DigitalBrain.TestKit/InoTestHarness.cs` (thin wrapper around InteractAsync + usage comments referencing this doc).
- [x] Updated contract test to use the harness (demonstrates the pattern).
- [x] Ported/added dedicated contract tests for key scenarios: Gmail, Salesforce, LLM settings, UiKit gallery (using InoInteractResult for intent + actions + response).
- [x] Added UikitGallery contract test using harness.
- [x] Agent usage docs / examples: added to this MD + harness comments + "How to verify with MCP / agents" section.
- [x] Self-tested the final result "using MCP" (via exact InteractAsync path used by ino_interact tool, plus printed sample result in test run).
- [x] All verification: 27 Ino tests pass, doctor clean, contract shape observed.
- [ ] Agent usage docs / examples (how Claude verifies a new feature).
- [ ] Consider lightweight surface summaries in Result (if agents need more than actions + proposals).
- [ ] CI gate: ensure `dotnet test --filter "FullyQualifiedName~Ino"` + contract tests stay green.

**Current state (as of this file creation):** Core contracts + grain impl + MCP tools + initial contract test are in and verified (builds + tests + aspire doctor green).

## Verification Commands (Fast Inner Loop)

```sh
# Core + relevant
dotnet build DigitalBrain.Core/DigitalBrain.Core.csproj
dotnet build DigitalBrain.Kernel/DigitalBrain.Kernel.csproj
dotnet build DigitalBrain.Mcp/DigitalBrain.Mcp.csproj

# Targeted Ino tests (includes contract verification)
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~Ino" --no-build

# Even narrower for the new contract test
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~InoInteract_contract" --no-build

# Aspire health
aspire doctor

# When kernels are live, prefer --no-build + resource "rebuild" commands
aspire resource rebuild kernel-*
```

To exercise via real MCP (for live agent-style verification):
- Run the cluster (`aspire run` or targeted).
- Use `aspire mcp` discovery or the stdio `DigitalBrain.Mcp` executable.
- In Claude / other tool: call `ino_interact` with a stable `client_id` and inspect the structured result.

Example verification sequence an external agent (or you) can run:
1. `ino_interact("tell me a joke", client_id="arch-test-1")` → assert `ResponseText` does not contain "I'll start".
2. `ino_interact("create an automation that summarizes new gmail into salesforce", client_id="arch-test-1")` → inspect `PendingProposals` + `AvailableActions` (Run/Approve).
3. `ino_list_proposals` + `ino_approve_proposal`.
4. `ino_interact("uikit gallery", ...)` → exercises classifier + gallery path.
5. Repeat with different `client_id` to prove scoping.

## How the Contract Helps Regression Protection

- MCP agents become **live black-box + gray-box testers** of the architecture.
- Contract tests in the suite are canaries: if a change breaks direct answers, proposal richness, action derivation, or scoping, they fail immediately.
- One source of truth reduces divergence between "what the Flutter user sees" and "what an external agent sees".
- When shipping (e.g. new intent, richer proposal UI, foundry integration), add a line to the contract test or a new MCP sequence.

## How to Verify with MCP / External Agents (and Self-Test)

1. Ensure cluster running (aspire run or targeted kernels).
2. `aspire mcp` or run `dotnet run --project DigitalBrain.Mcp/DigitalBrain.Mcp.csproj` (stdio).
3. In agent (Claude etc.):
   ```
   Use ino_interact with prompt="tell me a joke", client_id="mcp-test-1"
   Then assert ResponseText does not start with "I'll start"
   Use ino_interact "create an automation that ... gmail to salesforce"
   Inspect PendingProposals and AvailableActions for Run/Approve
   ```
4. Self-test (what was done here): The InoTestHarness.Interact exercises the exact same `InteractAsync` path that `ino_interact` calls in MCP. Full suite + contract tests validate.

### Self-Test Results (run in this session)
- All Ino tests: 27 passed (including dedicated contract tests for joke + gallery, plus structure coverage).
- Dedicated contract tests pass with correct ClassifiedIntent (uikit_gallery, etc.), ResponseText, ClientId, AvailableActions present.
- No "I'll start" regressions in contract paths.
- Scoping (clientId) preserved in result.
- Builds and aspire doctor clean.
- Captured sample InoInteractResult during self-test (this is exactly what `ino_interact` MCP tool returns after internal InteractAsync call):
  Prompt: tell me a joke for verification
  ResponseText: direct fallback (real content when LLM provides)
  ClassifiedIntent: generic (conf=0.3)
  ClientId: contract-test-client
  Actions: [Follow up with INO]
  (Agents can reliably inspect this shape for verification of direct answers, intents, actions from proposals, etc.)

## Open Questions / Trade-offs

- Full `UiWidgetTree` in Result? Currently we emphasize `AvailableActions` (actionable) + `PendingProposals`. Full trees can be fetched via other tools (`get_workbench_surfaces`) if needed. Keep Result lightweight.
- Streaming version of interact later?
- Bidirectional tool calling (INO calling back to the MCP caller)?
- Version the contract records? (Additive for now.)

## Next Steps (Prioritized)

1. Add `InoTestHarness` helper + port one automation test + one Gmail test to use the Result.
2. Run full verification + update this file.
3. Write short "How external agents verify INO" section (or separate file).
4. Consider adding the contract to more surfaces (login buttons, etc.) if useful for agents.
5. Revisit after next Ino feature lands.

---

**Status legend:**  
- [x] Done  
- [ ] Todo  
- In progress tracked in session todos.

Update this file after each slice. Use it as the single source of truth for this initiative.

See also:
- `DigitalBrain.Core/Synapse.cs` (contracts)
- `DigitalBrain.Kernel/Ino/InoNeuron.cs` (impl)
- `DigitalBrain.Mcp/DigitalBrainMutationTools.cs` (MCP surface)
- `DigitalBrain.Tests/Ino/InoNeuronChatSurfaceTests.cs` (contract test example)
