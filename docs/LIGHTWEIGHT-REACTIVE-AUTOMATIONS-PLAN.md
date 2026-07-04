# Lightweight Reactive Automations — Implementation Plan (Reactions + C# Scripts)

**Status:** IMPLEMENTED (core + tests + wiring green as of 2026-07-04). See summary at bottom.  
**Date:** 2026-07-04  
**Owner:** (to be assigned)  
**Related:** `docs/CONTINUATION-DISTRIBUTION.md` (the heavier rail), `docs/PRODUCT_VISION.md`, `docs/SYSTEM_DESIGN.md`, `docs/authoring-a-bundle.md`, AGENTS.md

## TL;DR

Add a **fast, lightweight, reactive path** for "apps", automations, and glue logic that the system (Ino/LLM) or humans can write, store, activate, reuse, and share **without** going through full NeuroPack + Roslyn ALC + publish/install/embody ceremony every time.

Core idea (hybrid of reactions + C# scripting):
- **Reaction** = data record declaring `when` (e.g. `NeuronActivated` on a target, `Signal:Foo`, synapse type).
- **Script** = small, named (or inline) C# snippet executed via the already-declared `Microsoft.CodeAnalysis.CSharp.Scripting`.
- An "app" = named collection of reactions + scripts.
- Everything is journaled, hot, immediately active, and composes with the existing broadcast timeline, `NeuronActivated`, `Signal`, and `FireAsync`.

This is **orthogonal** to the existing pack rail (`IPackBehavior`, `PackAlcEmbodier`, `GeneratedNeuron`, marketplace). Use full packs for rich UI/content/monetized experiences. Use this for the 80% case of fast self-writing, personal automations, and reactive glue.

**Key user desire enabled directly:**
```text
when MyNeuron.Lifetime.Activated then ...
```
(or `when Signal:TelegramMessageReceived then run "shared.daily-brief"`).

## Goals (prioritized)

1. **Iteration speed** — Change behavior in seconds. No rebuild of binaries, no full pack lifecycle, no ALC for micro-changes.
2. **Self-writing + store/reuse/share** — The system can emit definitions, persist them, activate them, reference scripts by name across reactions/neurons, and share tiny definitions.
3. **Reactivity first** — Natural `when ... then` over the existing `NeuronActivated` + broadcast + `Signal` system.
4. **C# scripting as the action language** — Real (small) C# that LLMs generate easily. No new DSL.
5. **Clean & easy implementation** — Minimal new surface. Heavy reuse of journals, broadcast, `Neuron` base, `Signal` pattern, `Fire`. One grain + one pure runner. No new `.csproj`.
6. **Sustainable** — Does not grow the kernel surface long-term. Complements (does not replace) the bundle distribution rail.
7. **Fast inner loop** — Testable with `dotnet test --filter` in the same style as `BundleHarness` / existing neuron tests.

Non-goals (for this plan):
- Full visual graph editor / dataflow UI (can be built later on top of these definitions).
- Replacing `IPackBehavior` or moving existing packs.
- Open/untrusted execution (stay within trusted-publisher model for now).
- Heavy parser or complex rule engine (string-based matching to start — keep it trivial).

## Architecture Overview (keep it brutally simple)

```
Timeline broadcast (existing)
        │
        ▼
AutomationNeuron (always subscribes)
  • Projects active Scripts + Reactions from its own journals (same pattern as MarketplaceNeuron / GeneratedNeuron)
  • Matches incoming (NeuronActivated, Signal, other Synapse) against registered reactions
  • Resolves script (by id or "inline:...") 
  • Executes via ScriptRunner (CSharpScript + narrow Globals)
  • Fires resulting Synapses (via the caller's Fire delegate for proper lineage)
        │
        ▼
Existing neurons / packs / UI / Telegram etc. (N+1 already works)
```

"App" storage: A conventional grouping (by prefix or a lightweight `AutomationApp` record). Activating an app = (re)registering its pieces. Sharing = serializing the small definition records.

Everything stays inside the same trust/activation model as current packs.

## Core Types (add to Core — minimal)

Create or extend `DigitalBrain.Core/Automations.cs` (or place the records in `Signals.cs` to touch even fewer files — prefer a dedicated small file for clarity).

```csharp
[GenerateSerializer]
public record RegisterScript(
    [property: Id(0)] string Id,
    [property: Id(1)] string Code,              // small C# body
    [property: Id(2)] string Description = "",
    [property: Id(3)] IReadOnlyList<string> DeclaredEmits = null!) 
    : Synapse(nameof(RegisterScript), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record RegisterReaction(
    [property: Id(0)] string Id,
    [property: Id(1)] string When,              // "NeuronActivated", "Signal:XXX", "Synapse:YYY", "*"
    [property: Id(2)] string? Target = null,    // "MyNeuron", "my-*", null = any
    [property: Id(3)] string ScriptRef,         // "script-id" or "inline: return new[] { ... };"
    [property: Id(4)] IReadOnlyList<string> DeclaredEmits)
    : Synapse(nameof(RegisterReaction), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record AutomationApp(
    [property: Id(0)] string AppId,
    [property: Id(1)] string Description,
    [property: Id(2)] IReadOnlyList<RegisterScript> Scripts,
    [property: Id(3)] IReadOnlyList<RegisterReaction> Reactions)
    : Synapse(nameof(AutomationApp), DateTimeOffset.UtcNow);

// Convenience for Ino/LLM (optional sugar — grain can also accept the primitives directly)
[GenerateSerializer]
public record CreateAutomationApp(string AppId, string Description /* + payload */) 
    : Synapse(nameof(CreateAutomationApp), DateTimeOffset.UtcNow);
```

Also add the interface in the same file (or `Synapse.cs`):

```csharp
public interface IAutomationNeuron : INeuron
{
    // For direct calls / MCP if desired. Most usage goes through FireAsync of the records above.
    Task<IReadOnlyList<string>> ListActiveScriptsAsync();
    Task<IReadOnlyList<string>> ListActiveReactionsAsync();
}
```

All records get `[GenerateSerializer]`. Use sequential `[Id(n)]`.

## ScriptRunner (pure, narrow, C# scripting)

**Location:** `DigitalBrain.Kernel/Foundry/ScriptRunner.cs` (reuses the Foundry folder that already contains executors; keep the class tiny and self-contained).

```csharp
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

public static class ScriptRunner
{
    public sealed record ScriptGlobals(
        Synapse Synapse,
        NeuronId Self,
        Func<Synapse, Task> Fire   // provided by host so lineage / FireAsync rules are honored
    );

    private static readonly ScriptOptions _options = ScriptOptions.Default
        .AddReferences(
            typeof(Synapse).Assembly,
            typeof(Signal).Assembly,
            typeof(NeuronId).Assembly /* add only what's truly needed */)
        .AddImports("System", "System.Collections.Generic", "DigitalBrain.Core");

    public static async Task<IReadOnlyList<Synapse>> ExecuteAsync(
        string scriptBody, Synapse input, NeuronId self, Func<Synapse, Task> fire)
    {
        if (scriptBody.StartsWith("inline:", StringComparison.OrdinalIgnoreCase))
            scriptBody = scriptBody["inline:".Length..].Trim();

        var globals = new ScriptGlobals(input, self, fire);

        try
        {
            var result = await CSharpScript.EvaluateAsync<IReadOnlyList<Synapse>?>(
                scriptBody, _options, globals: globals);

            return result ?? Array.Empty<Synapse>();
        }
        catch (Exception ex)
        {
            // Return a diagnostic emission instead of throwing so one bad script doesn't kill the host
            return new[] { new PackEmission("automation", input.Type, "script-error:" + ex.Message) };
        }
    }
}
```

**Design notes for cleanliness:**
- Globals surface is deliberately tiny (document it well).
- Scripts can either `return new[] { ... }` or call `await Fire(...)` (or both).
- First implementation can be simple `EvaluateAsync`. Later we can cache `Script` objects for repeated execution.
- No full ALC, no compilation to assemblies for the common case.

## AutomationNeuron (the reactive host)

**Location:** `DigitalBrain.Kernel/AutomationNeuron.cs`

```csharp
[GrainType("digitalbrain.automation.v1")]
public class AutomationNeuron(ILogger<AutomationNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IAutomationNeuron
{
    private Dictionary<string, string>? _scripts;           // id -> code
    private List<RegisterReaction>? _reactions;

    protected override bool ShouldSubscribeToTimeline => true;  // dynamic, like GeneratedNeuron

    protected override async Task DispatchSynapse(Synapse synapse)
    {
        EnsureProjections();
        await TryRunMatchingReactionsAsync(synapse);
        await base.DispatchSynapse(synapse); // or handle registrations here
    }

    public override async Task OnNextAsync(Synapse item, StreamSequenceToken? token = null)
    {
        await RecordBroadcastReceivedAsync(item);
        EnsureProjections();
        await TryRunMatchingReactionsAsync(item);
    }

    // Handle registration commands (via IHandle or direct in Dispatch)
    // ...

    private async Task TryRunMatchingReactionsAsync(Synapse synapse)
    {
        // Simple string matching (keep it trivial and fast)
        var matches = _reactions!.Where(r => Matches(r, synapse)).ToList();

        foreach (var r in matches)
        {
            var code = r.ScriptRef.StartsWith("inline:") 
                ? r.ScriptRef 
                : _scripts!.GetValueOrDefault(r.ScriptRef, "");

            if (string.IsNullOrWhiteSpace(code)) continue;

            var outputs = await ScriptRunner.ExecuteAsync(
                code, synapse, Self, s => FireAsync(StampCurrent(s)).AsTask());

            foreach (var o in outputs)
                await Broadcast(StampCurrent(o));   // or direct FireAsync with proper stamping
        }
    }

    private void EnsureProjections()
    {
        // Replay from OutgoingJournal + IncomingJournal (exact same style as MarketplaceNeuron.EnsureCache and GeneratedNeuron)
        if (_scripts is not null) return;
        _scripts = new(StringComparer.OrdinalIgnoreCase);
        _reactions = new();

        foreach (var s in OutgoingJournal.Concat(IncomingJournal).OfType<RegisterScript>())
            _scripts[s.Id] = s.Code;

        foreach (var r in OutgoingJournal.Concat(IncomingJournal).OfType<RegisterReaction>())
            _reactions.Add(r);

        // Also support AutomationApp records by expanding them
    }

    private static bool Matches(RegisterReaction r, Synapse s)
    {
        if (r.When == "*" || r.When == s.Type) return TargetMatches(r.Target, s);
        if (r.When == "NeuronActivated" && s is NeuronActivated) return TargetMatches(r.Target, s);
        if (r.When.StartsWith("Signal:") && s is Signal sig)
            return sig.Name == r.When["Signal:".Length..] && TargetMatches(r.Target, s);
        // extend conservatively as needed
        return false;
    }

    private static bool TargetMatches(string? target, Synapse s)
    {
        if (string.IsNullOrEmpty(target)) return true;
        // support simple patterns or exact NeuronId for activations
        if (s is NeuronActivated act)
            return act.Neuron.Value.Contains(target.TrimEnd('*'), StringComparison.OrdinalIgnoreCase);
        return true; // for non-activation, target is usually "any"
    }
}
```

Handle registration commands by updating the in-memory projection + letting the journal be the source of truth (no extra store).

## "Apps", Storage, Reuse & Sharing

- An **AutomationApp** record (or convention `app:MyApp/*` ids) groups scripts + reactions.
- Registering an app expands into the individual `RegisterScript` / `RegisterReaction` entries.
- Reuse: reference the same script id from many reactions.
- Sharing: the definition records are tiny and serializable. Send via deep link, MCP, Telegram, or wrap as a minimal "automation pack" (still uses the existing publish path but carries only script data).
- Journal is the durable store (follows Core Law — journals are never deleted).

## Activation, Broadcast & Wiring

1. Define `IAutomationNeuron` in Core.
2. In `Program.cs` (ApplicationStarted warmup block, next to `ILlmResponderNeuron`):
   ```csharp
   await grainFactory.GetGrain<IAutomationNeuron>("automation-main").GetTimelineAsync();
   ```
3. The grain uses the global `"DigitalBrainTimeline"` stream (inherited from `Neuron`).
4. `NeuronActivated` already flows when grains activate — no changes needed there.

## Integration Points (reuse existing)

- **Ino / LLM self-writing**: Ino (or `CodeFoundry`) simply fires `RegisterScript` / `RegisterReaction` / `AutomationApp` records. The neuron makes them live.
- **MCP tools**: Existing mutation tools (`FireAsync` on grains) work immediately. Add thin `register_automation_script` etc. wrappers later if ergonomics demand it (low priority).
- **Packs**: A pack can emit signals that trigger reactions, or a reaction can emit signals that a pack handles. Composition is free via the `Signal` convention already used by `PersonalAssistantNeuron`, `TelegramResponderNeuron`, etc.
- **UI surfaces**: Reactions/scripts can emit `UiSurface`, `RfwCard`, `PackEmission`, etc. exactly like packs.
- **Tests**: Use the same `TestDigitalBrain` / grain resolution patterns. Fast path = direct grain activation + journal inspection + signal firing. No need for full Aspire for unit coverage.

## Implementation Tasks (ordered, TDD, small steps)

Follow AGENTS.md strictly:
- Fast inner loop: `dotnet build && dotnet test --filter "..."` after every logical change.
- After significant changes: `dotnet build`, `dotnet test`, `aspire doctor` (or relevant).
- Prefer reuse and deletions over additions.
- Write the failing test first for new behavior.

### Task 0 — Prep & Baseline
- [ ] Read `docs/LIGHTWEIGHT-REACTIVE-AUTOMATIONS-PLAN.md` (this file), relevant parts of `SYSTEM_DESIGN.md`, `Neuron.cs`, `GeneratedNeuron.cs`, `MarketplaceNeuron.cs`, `Signals.cs`, `Program.cs`.
- [ ] Run baseline: `dotnet build` and `dotnet test --filter "Neuron|Broadcast|Signal" --no-build` (note current green count).
- [ ] Create the plan review checkpoint if needed.

### Task 1 — Core contracts (pure, no behavior)
**Files:**
- `DigitalBrain.Core/Automations.cs` (new — or append records to `Signals.cs` if you want zero new files)
- Update `DigitalBrain.Core/Synapse.cs` (add `IAutomationNeuron` if you prefer everything in one place)

**Steps:**
- [ ] Add the four record types + interface with proper `[GenerateSerializer]` and `[Id(n)]`.
- [ ] Add any needed `using` or small constants (e.g. well-known automation key).
- [ ] Write a tiny unit test in `DigitalBrain.Core` or `DigitalBrain.Tests` that roundtrips the records through serialization (or just compiles against them).
- Verify: `dotnet build DigitalBrain.Core` + relevant test.

### Task 2 — ScriptRunner (pure helper, C# scripting)
**Files:**
- `DigitalBrain.Kernel/Foundry/ScriptRunner.cs` (new file)

**Steps:**
- [ ] Implement the narrow `ScriptGlobals` + `ExecuteAsync` using `CSharpScript.EvaluateAsync`.
- [ ] Add basic error handling that emits a diagnostic instead of crashing the host.
- [ ] Write fast unit tests (no Orleans) in `DigitalBrain.Tests/Foundry/` or a new `ScriptRunnerTests.cs`:
  - Simple return list of synapses.
  - Calling the Fire delegate.
  - Inline: prefix handling.
  - Error path.
- Verify: `dotnet test --filter ScriptRunner`.

### Task 3 — AutomationNeuron skeleton + journal projection
**Files:**
- `DigitalBrain.Kernel/AutomationNeuron.cs` (new)
- Update `DigitalBrain.Core` (interface if not done)

**Steps:**
- [ ] Basic class inheriting `Neuron`, `ShouldSubscribeToTimeline => true`.
- [ ] `EnsureProjections()` that replays `RegisterScript` / `RegisterReaction` from journals (copy the style exactly from `MarketplaceNeuron.EnsureCache`).
- [ ] Handle registration by letting the journal + replay do the work (or explicit handlers).
- [ ] Stub `TryRunMatchingReactionsAsync` (no execution yet).
- [ ] Add `IAutomationNeuron` implementation for listing.
- [ ] Write tests using `TestDigitalBrain` / grain factory:
  - Register a script + reaction.
  - Fire a matching synapse.
  - Assert the reaction was recorded in the projection (via timeline or direct call).
- Verify build + tests frequently.

### Task 4 — Matching + Script execution integration
**Files:** `DigitalBrain.Kernel/AutomationNeuron.cs` + tests

**Steps:**
- [ ] Implement `Matches(...)` for the four cases: `NeuronActivated`, `Signal:xxx`, plain type, `*`.
- [ ] Wire `ScriptRunner.ExecuteAsync` inside the match path. Pass a delegate that does `await FireAsync(StampCurrent(...))`.
- [ ] Support both named script refs and inline.
- [ ] Special case for `NeuronActivated` (the user's primary example).
- [ ] Add tests:
  - `when NeuronActivated on "MyNeuron" then script runs and emits`.
  - Script receives the correct `Synapse` and `Self`.
  - Multiple reactions, shared scripts.
  - Non-matching does nothing.
- Verify: fast tests pass.

### Task 5 — OnNextAsync + DispatchSynapse paths + Broadcast
- Make sure both point-to-point delivery and broadcast timeline reach the runner (mirror `GeneratedNeuron`).
- Use existing `Broadcast` / `FireAsync` helpers from base `Neuron`.
- Test cross-grain broadcast scenarios.

### Task 6 — Startup activation (production reachability)
**Files:**
- `DigitalBrain.Kernel/Program.cs`

**Steps:**
- [ ] Add the warmup line in the `ApplicationStarted` block (right next to `ILlmResponderNeuron`):
  ```csharp
  await grainFactory.GetGrain<IAutomationNeuron>("automation-main").GetTimelineAsync();
  ```
- [ ] Add any necessary DI or grain interface registration if the resolver pattern requires it (see `NeuronResolver.cs`).
- [ ] Verify that a fresh kernel start receives an activation-triggered script.

### Task 7 — "App" grouping + convenience
- Implement expansion of `AutomationApp` / `CreateAutomationApp` into the individual registrations.
- Add simple `ListActive...` and perhaps a surface emission for installed automations (optional, low priority).
- Test: create an app → both scripts and reactions become active.

### Task 8 — Ino / MCP / authoring ergonomics (make self-writing delightful)
- Ensure Ino can discover the automation grain and fire the registration synapses (it already can via generic paths).
- Optional: add a small helper method or a thin MCP tool that accepts a higher-level description and turns it into the records.
- Document a 2-minute "write a reaction" example (update or create a short section in `docs/authoring-a-bundle.md` or a new small guide).
- Add at least one end-to-end style test that goes through an Ino-like flow or direct grain calls.

### Task 9 — Polish, capabilities, error handling, cleanup
- Simple declared-emits check (light version of `CapabilityGate`).
- Good logging on script execution and errors.
- Make sure one bad script never poisons the neuron (isolation per execution).
- Review for duplication; delete anything unnecessary.
- Add a couple of realistic examples in seed data or tests (e.g. "on activation of personal assistant, run a brief script").
- Run full relevant test filter + build.

### Task 10 — Documentation + acceptance
- Update this plan with "Implemented" notes or create a short `docs/REACTIVE-AUTOMATIONS.md` usage guide.
- Verify against acceptance metrics below.
- Run broader test suite: `dotnet test --filter "Automation|Neuron|Broadcast|Signal|Ino"`.
- (If Aspire changes were touched) `aspire doctor`.

## Testing Strategy (fast inner loop first)

- Unit tests for `ScriptRunner` — no Orleans, pure.
- Grain-level tests using `DigitalBrain.TestKit` + `NeuronTestBase` patterns (fast, in-memory journals).
- Broadcast reactivity tests (reuse `BroadcastReactivityTests` style).
- One or two "live render / end-to-end lite" tests that exercise `NeuronActivated` → reaction → emitted `UiSurface` or `Signal` (optional Playwright only when needed).
- Always prefer `dotnet test --filter "FullyQualifiedName~Automation"` or similar.

Do **not** require full Aspire stack for the core loop.

## Verification Commands (run after every task or logical chunk)

```powershell
dotnet build
dotnet test --filter "Automation|ScriptRunner|NeuronActivated|Broadcast" --no-build
# For broader safety:
dotnet test DigitalBrain.Tests --filter "Ino|Signal|Pack" --no-build
aspire doctor   # when relevant
```

Follow the "fast inner loop (default)" rule from AGENTS.md.

## Risks & Cleanliness Guardrails

- **Keep matching stupid-simple** at first (string prefix / exact + very limited wildcard). Do not build a full rule engine.
- Globals in `ScriptRunner` must stay narrow. Any new power must be explicitly justified and documented.
- The automation neuron must tolerate bad scripts (never let one script kill the host or other reactions).
- Journal churn: reactions and scripts are small and stable; still consider the tolerant-reader implications from D-DIST7 / cleanup docs if we later delete reaction types.
- Do not touch `PackAlcEmbodier`, `GeneratedNeuron`, `IPackBehavior`, or the marketplace publish path unless promoting an automation to a full pack (future, out of scope).
- One new grain + one helper class is the target surface. Challenge every added line.
- All new synapses/records must be `[GenerateSerializer]`.

## Acceptance Criteria / Metrics

- A developer (or Ino) can register a reaction on `NeuronActivated` + a 5-line script and see it fire on the next activation of a matching neuron **without** publishing a pack or restarting anything.
- Script can be stored by id and reused by name from multiple reactions.
- "App" can be created that bundles several scripts + reactions; activating the app makes them live.
- All core behavior covered by fast tests that run in < 10s total for the automation suite.
- No new `.csproj` files.
- Existing pack-based experiences continue to work unchanged.
- Warmup in production makes broadcast reactions reachable on fresh kernel start.
- `when MyNeuron.Lifetime.Activated then ...` is demonstrably possible with the primitives.

## Open / Later Decisions (do not block initial implementation)

- Exact syntax sugar for Ino prompts ("create a reaction that...").
- Whether to emit a nice `AutomationSurface` / marketplace facet for listing active automations.
- Caching compiled `Script` delegates for hot paths.
- Promotion story: "crystallize this set of reactions into a real NeuroPack".
- Multi-user scoping for personal automations (coordinate with ongoing multiuser work).
- Visual constructor (build a graph UI that emits the same `Register*` records).

---

**This plan deliberately stays small, focused, and fast.** It gives the self-writing + reactivity + sharing loop the system needs right now while the heavier distribution rail matures separately.

Once the basic loop is green (Tasks 1-6), the rest is incremental polish and integration.

Start with Task 1. Write the test that forces the first record or the ScriptRunner first. Keep changes tiny. Run the fast commands constantly.

Ready when the first `NeuronActivated` → custom script emission demo works end-to-end in a test cluster.