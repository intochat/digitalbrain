# remove.md — Exhaustive O(n²) cleanup ledger

**Status:** EXECUTED (A+B+C+D) — 2026-07-16  
**Method:** Full pairwise symbol scan (every production type definition × every tracked text file) + CodeGraph architecture pass.  
**Date:** 2026-07-16  

### Execution record

- Waves **A + B + C** executed earlier (trash commit `d61472a9`).
- Wave **D VERIFY** executed this cycle after product commit `d3f7c4b3`.
- Wave **E** renames **not** done.
- §3 FALSE DEAD kept (JsonElementSurrogate, Orleans grains, RuntimeProfile, McpRequestGuard, live model descriptors, RFW Synapse* widgets).
- Build: `dotnet build Brain.slnx` — 0 errors / 0 warnings.
- Tests: root `dotnet test --logger "console;verbosity=minimal"` — all green
  (EmailSummarizer 4, EnrichSalesforce 4, AppHost 27, Unit 45, Orleans 779, IntegrationContract 116, E2E 23).
- `aspire doctor` — 5/5 pass.

## 2. TRUE DELETE — executed set

### Wave A — pure trash
(see prior record — trash tests, docs/superpowers, media_kit, ghosts)

### Wave B — dead UI projects
(see prior — `DigitalBrain.Ui.Contracts`, `DigitalBrain.Ui.Runtime`)

### Wave C — dead Kernel contracts
(see prior — AuthRequiredAIFunction, LlmAttribute; **KEPT** JsonElementSurrogate)

### Wave D VERIFY — confirmed zero production use (this cycle)
1. `src/DigitalBrain.Kernel.Contracts/Core/Telemetry.cs` (`ITelemetrySink`, `TelemetryBuffer`, `MetricPoint`, `TraceContext`) — DI registration only; `Emit*` never called
2. DI registrations of `ITelemetrySink`/`TelemetryBuffer` in `DigitalBrain.Mcp/Program.cs` and `DigitalBrainOrleansExtensions.AddDigitalBrainClients`
3. `GrainIds` static helpers in `RuntimeContracts.cs` — zero callers (`FeatureGrainIds` is the live path)
4. `OwnerCollections` in `NeuronScope.cs` — zero callers
5. `NeuronScopeExtensions.AsScope` — test-only; deleted with its sole test
6. `Sensitivity` + `Redaction` in `RuntimeContracts.cs` — sole consumer was Telemetry
7. Unused LLM/embedding model descriptors (never registered in AppHost; only self-refs + reflection inventory test):
   - Anthropic: Claude45Haiku, Opus46, Sonnet46 (folder removed)
   - GitHub: Gpt41Mini, Gpt41Nano, O4Mini, TextEmbedding3Small (folder removed)
   - OpenAI: Gpt54, Gpt54Mini, Gpt54Nano, TextEmbedding3Small (folder removed)
8. Unused AppHost usings for Anthropic/GitHub/OpenAI model namespaces
9. Unused `using System.Collections.Concurrent` on RuntimeContracts after GrainIds removal

### Kept (production-wired)
- Ollama `Llama31_8B`, `MxbaiEmbedLarge` (AppHost)
- Azure OpenAI `Gpt4oMini` (registry tests + provider path)
- Provider IDs + chat client builders for Anthropic/GitHub/OpenAI (string config path, not typed descriptors)

## 3. FALSE DEAD — not deleted

- Orleans grain **implementations** (resolved by interface / grain type, not class-name callers)
- `JsonElementSurrogate` (Orleans serializer)
- `RuntimeProfile` / `CapabilityProfiles.cs` (Mcp + AppHost auth/cors)
- `McpRequestGuard` / `McpTransportPolicy` (MCP transport)
- FeatureHost/RuntimeHost extension entrypoints
- RFW `Synapse*` Flutter widgets (UI presentation names; not the deleted generic Neuron/Synapse runtime)
- `ToolGrounding` / conversation wire records (serialized contract surface)
- Living README/CLAUDE/AGENTS

## 4. Wave E (optional, not executed)

Legacy folder renames only if still valuable; no behavior change. Candidate later: `tests/.../Legacy` taxonomy, RFW Synapse* renames if product language moves fully off that noun.
