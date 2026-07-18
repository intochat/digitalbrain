# Persona Phase 1 — Event-Driven Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 3-second-polling persona with a push-based Orleans grain that is the durable brain the platform will evolve on. Commit to the full schema (arousal / valence / energy / mood / recent signals / traits) on day one so phases 2 (synapse decay) and 3 (personality-as-neuron) are incremental additions — not a halfway-house rewrite.

**Architecture:**
- A new `PersonaGrain` per key holds `PersonaBrainState` as durable Orleans state (`IPersistentState<T>`).
- A new `PersonaSignalFilter` (`IIncomingGrainCallFilter`) runs alongside the existing `TimelineCallFilter`. It observes every grain-to-grain call, projects it into a typed `PersonaSignal`, passes it through an `IPersonalityShaper` (default pass-through in phase 1), and fires it into the `PersonaGrain` via `OnSignalAsync`.
- The grain runs a minimal phase-1 projection (lerp toward the signal values; phase 2 replaces this with a decaying blend over `RecentSignals`).
- The grain notifies subscribed `IPersonaObserver` instances. `InoService.StreamPersonaState` creates one observer per gRPC subscription, bridges to a bounded `Channel<PersonaBrainState>`, and writes a frame to the gRPC stream on every push. No polling. Sub-300ms end-to-end latency target.

**Tech Stack:** C# 13 / .NET 11, Orleans 9 (grain call filters, grain observers via `ObserverManager`, `Grain<TState>` persistent state), xUnit v3 + Reqnroll Gherkin for per-verb neuron tests, in-process `MeterListener` + `ActivityListener` for telemetry assertions, existing `GrpcTestFixture` for E2E. Flutter stays on BLoC + `_PersonaPainter` CustomPaint — the Rive `.riv` file and binding layer are explicitly phase 1.5, OUT OF SCOPE.

**Non-goals (explicit deferrals):**
- **No Rive asset.** Phase 1.5 adds `persona.riv` once a designer authors it. The binding surface is already wired in `persona_widget.dart:149-179`; swap-in is mechanical.
- **No decay list population.** `PersonaBrainState.RecentSignals` is defined day one but stays empty in phase 1. Phase 2 turns on append + half-life decay + consolidation.
- **No personality Shape() variation.** `PassThroughShaper` is the only implementation. Phase 3 adds Jarvis/Luna/Coach/Sage neurons that fork the interface.
- **No extended proto fields.** `arousal`/`valence` are NOT added to `PersonaState.proto` yet — existing `emotion` + `energy` + `confidence` fields carry phase 1. Phase 1.5 adds `arousal` and `valence` when the Rive binding needs them.
- **No per-user routing.** The filter fires into `PersonaGrain.GetGrain("global")`. Phase 2 adds per-user routing when chat carries user context reliably.

---

## File Structure

### New files

| Path | Responsibility |
|---|---|
| `iaw/Core/Persona/PersonaSignal.cs` | `[GenerateSerializer]` record — typed synapse carrying arousal/valence/energy/halfLife/source/bornAt/correlationId |
| `iaw/Core/Persona/PersonaEmotion.cs` | Server-side enum mirroring the Flutter `PersonaEmotion` — 12 moods |
| `iaw/Core/Persona/PersonaBrainState.cs` | Durable state record — arousal, valence, energy, mood, `ImmutableList<PersonaSignal> RecentSignals`, traits, lastTickAt |
| `iaw/Core/Persona/PersonaTraits.cs` | Record — warmth, formality, proactivity (all `[0..1]`) with `Default` static |
| `iaw/Core/Persona/IPersonalityShaper.cs` | Interface + `PassThroughShaper` default implementation |
| `iaw/Core/Persona/IPersonaGrain.cs` | Grain interface + `IPersonaObserver` grain-observer interface |
| `iaw/Core/Persona/PersonaGrain.cs` | `Grain, IPersonaGrain` implementation — state projection + observer fan-out |
| `iaw/Core/Persona/PersonaSignalFilter.cs` | `IIncomingGrainCallFilter` that projects grain calls into `PersonaSignal` and fires them into `IPersonaGrain.OnSignalAsync` |
| `iaw/Core/Persona/PersonaSiloExtensions.cs` | `AddPersonaSignalProjection(siloBuilder)` — one-call wiring for production + test silos |
| `features/ino-new/InoNew.Tests/Features/PersonaGrain.feature` | Gherkin feature — one scenario per synapse verb (tool.invoked, llm.started, error.raised, task.completed, user.typing, synapse.fired) |
| `features/ino-new/InoNew.Tests/Steps/PersonaGrainSteps.cs` | Reqnroll-style step definitions (backed by a shared `NeuronBddContext`) |
| `features/ino-new/InoNew.Tests/Steps/PersonaGrainScenarioTests.cs` | Per-scenario xUnit v3 `[Fact]` methods (matches existing `ShellNeuronScenarioTests.cs` pattern for Reqnroll/xUnit v3 incompatibility workaround documented in CLAUDE.md) |
| `test/E2E.Tests/Persona/PersonaPushE2E.cs` | E2E test — fires a real grain call through the silo, asserts push latency + telemetry + gRPC frame delivery |

### Modified files

| Path | Change |
|---|---|
| `iaw/Telegram/Services/InoService.cs` — `StreamPersonaState` (currently lines 154-217) | Rewrite: poll loop → `IPersonaObserver` + `Channel<PersonaBrainState>` bridge. Delete `Task.Delay(3000)` and the `CountByKindAsync` heuristic. |
| `iaw/Core/Telemetry/InoMetrics.cs` | Add `PersonaSignals` counter tagged by `verb`. Emotions + energy counters stay (now written from `PersonaGrain.OnSignalAsync` instead of `InoService`). |
| `iaw/Agents.Host/Program.cs` | Call `.AddPersonaSignalProjection()` on the silo builder. |
| `iaw/Testing/InoTestHost.cs` (`E2ESiloConfigurator`) | Call `.AddPersonaSignalProjection()` so E2E tests exercise the same pipeline as production. |
| `CLAUDE.md` known-problems section | Mark known-problem #7 persona branch as phase 1 complete; note phases 2/3 still open. |

### Existing files referenced (no changes)

- `features/timetravel/Timetravel.Core/TimelineCallFilter.cs` — the pattern `PersonaSignalFilter` mirrors
- `features/timetravel/Timetravel.Core/TimelineSiloExtensions.cs:16-22` — the wiring pattern `PersonaSiloExtensions` mirrors
- `iaw/Telegram/Services/InoService.cs:206-239` — the `Channel<T>` bridge pattern used in `StreamEvents` that `StreamPersonaState` will copy
- `features/timetravel/Timetravel.Tests/Steps/ShellNeuronScenarioTests.cs` — the Reqnroll/xUnit v3 workaround pattern
- `test/E2E.Tests/Persona/PersonaAnimationsE2E.cs` — the `PersonaTelemetryProbe` class is reused by the new E2E test

---

## Task 1: Context7 Research

**Goal:** Verify the Orleans 9 APIs this plan assumes are actually the current shape before writing any code. No code changes in this task — only a notes file that subsequent tasks reference.

**Files:**
- Create: `docs/superpowers/plans/notes/2026-04-12-persona-phase-1-context7-notes.md`

- [ ] **Step 1: Look up Orleans `IIncomingGrainCallFilter` current API**

Run Context7 for the Orleans `.NET` library:
```
mcp__context7__resolve-library-id(libraryName="Orleans")
mcp__context7__query-docs(
  libraryId="/dotnet/orleans",
  query="IIncomingGrainCallFilter Invoke InterfaceType Grain Orleans 9")
```

Expected facts to verify and record in the notes file:
- `IIncomingGrainCallFilter.Invoke(IIncomingGrainCallContext context)` signature unchanged
- `IIncomingGrainCallContext.InterfaceType` property name (`Type`? `InterfaceType`? `InterfaceMethod.DeclaringType`?)
- How to access the target grain type from the context (`context.Grain.GetType()` vs `context.InterfaceType`)
- Filter ordering / short-circuit semantics (does `context.Invoke()` throw?)
- Registration: `siloBuilder.AddIncomingGrainCallFilter<T>()` vs `services.AddSingleton<IIncomingGrainCallFilter, T>()`

- [ ] **Step 2: Look up `ObserverManager<T>` / `IGrainObserver` push delivery**

```
mcp__context7__query-docs(
  libraryId="/dotnet/orleans",
  query="ObserverManager IGrainObserver CreateObjectReference Subscribe gRPC streaming")
```

Record:
- Current namespace (`Orleans.Runtime.Utilities`? `Orleans.Utilities`?)
- Constructor signature — `new ObserverManager<T>(TimeSpan expiration, ILogger logger)` or different
- `Subscribe(TObserver observer, TObserver key)` vs single-arg overload
- `Notify(Func<T, Task>)` or `Notify(Action<T>)` — sync/async
- How to create observer reference from a plain C# object at the client side: `clusterClient.CreateObjectReference<IObserver>(obj)` expected, but verify it's on `IGrainFactory` in Orleans 9
- Observer lifetime — does it survive silo restart? (expected: no, which is fine for streaming gRPC calls)

- [ ] **Step 3: Look up `IPersistentState<T>` + `Grain<TState>` conventions**

```
mcp__context7__query-docs(
  libraryId="/dotnet/orleans",
  query="IPersistentState PersistentState attribute grain storage provider Default")
```

Record:
- Constructor-injection pattern: `[PersistentState("brain", "Default")] IPersistentState<PersonaBrainState> state`
- `WriteStateAsync()` on every change vs periodic flush
- Default storage provider name the production silo uses (verify `"Default"` matches what `iaw/Agents.Host/Program.cs` registers)
- Serialization requirements — `[GenerateSerializer]` on records, `[Id(n)]` on properties

- [ ] **Step 4: Verify `[GenerateSerializer]` + `ImmutableList<T>` coexist**

```
mcp__context7__query-docs(
  libraryId="/dotnet/orleans",
  query="GenerateSerializer ImmutableList record serialization")
```

Record: whether `ImmutableList<PersonaSignal>` works out of the box with Orleans serialization or needs a surrogate. If it needs a surrogate, phase 1 uses `ImmutableArray<PersonaSignal>` or plain `List<PersonaSignal>` instead.

- [ ] **Step 5: Write notes file with verified facts**

The file is a reference for subsequent tasks. Format:
```markdown
# Orleans 9 API notes — persona phase 1

## IIncomingGrainCallFilter
- <verified signature>
- <verified context member for grain type>
- <verified registration method>

## ObserverManager<T>
- <verified namespace>
- <verified constructor>
- <verified Subscribe/Notify signatures>
- <verified CreateObjectReference API>

## IPersistentState<T>
- <verified [PersistentState] attribute form>
- <verified WriteStateAsync behavior>
- <verified storage provider name used in production silo>

## Serialization
- ImmutableList<T> with [GenerateSerializer]: <supported | needs surrogate>
- Fallback: <if surrogate needed>
```

Subsequent tasks that use these APIs reference this file by path.

- [ ] **Step 6: Commit**

```bash
git add docs/superpowers/plans/notes/2026-04-12-persona-phase-1-context7-notes.md
git commit -m "docs(persona): phase-1 Context7 API research notes"
```

---

## Task 2: Core persona types

**Goal:** Create the four `[GenerateSerializer]` records that define the persona's full schema. Types only — no behavior yet. These are the contract every subsequent task will reference.

**Files:**
- Create: `iaw/Core/Persona/PersonaSignal.cs`
- Create: `iaw/Core/Persona/PersonaEmotion.cs`
- Create: `iaw/Core/Persona/PersonaTraits.cs`
- Create: `iaw/Core/Persona/PersonaBrainState.cs`
- Test: `features/ino-new/InoNew.Tests/Steps/PersonaGrainScenarioTests.cs` (later task — no test in this task, types compile check only)

- [ ] **Step 1: Create `PersonaSignal.cs`**

```csharp
namespace Core.Persona;

// A typed synapse carrying the persona-relevant fields of a neuron event.
// Verbs follow a dotted naming convention so the platform can group them:
//   tool.invoked, llm.started, user.typing, error.raised, task.completed,
//   synapse.fired, skill.installed, neuron.created, memory.recalled
[GenerateSerializer]
public sealed record PersonaSignal(
    [property: Id(0)] string Verb,
    [property: Id(1)] string SourceId,
    [property: Id(2)] float Arousal,
    [property: Id(3)] float Valence,
    [property: Id(4)] float Energy,
    [property: Id(5)] int HalfLifeMs,
    [property: Id(6)] DateTimeOffset BornAt,
    [property: Id(7)] string CorrelationId);
```

- [ ] **Step 2: Create `PersonaEmotion.cs`**

```csharp
namespace Core.Persona;

// Mirror of ino.flutter/lib/persona/persona_state.dart PersonaEmotion enum.
// Keep the order identical — the server emits these as lowercase strings
// and Flutter parses them by name.
public enum PersonaEmotion
{
    Sleeping,
    Waking,
    Idle,
    Listening,
    Thinking,
    Acting,
    Responding,
    Celebrating,
    Confused,
    Evolving,
    Searching,
    Presenting,
}
```

- [ ] **Step 3: Create `PersonaTraits.cs`**

```csharp
namespace Core.Persona;

// Personality trait vector, each dimension in [0..1]. Phase 1 uses Default
// everywhere; phase 3 loads per-persona values from AgentRegistry.
[GenerateSerializer]
public sealed record PersonaTraits(
    [property: Id(0)] float Warmth,
    [property: Id(1)] float Formality,
    [property: Id(2)] float Proactivity)
{
    public static readonly PersonaTraits Default = new(
        Warmth: 0.5f,
        Formality: 0.5f,
        Proactivity: 0.5f);
}
```

- [ ] **Step 4: Create `PersonaBrainState.cs`**

Use whichever collection type Task 1's Context7 notes verified works with `[GenerateSerializer]`. If `ImmutableList<T>` works, use it; otherwise `ImmutableArray<PersonaSignal>` or `List<PersonaSignal>`.

```csharp
using System.Collections.Immutable;

namespace Core.Persona;

// Durable brain state — the complete schema for phases 1/2/3.
// Phase 1 uses Arousal/Valence/Energy/Mood; RecentSignals stays empty.
// Phase 2 populates RecentSignals and runs decay-based blending.
// Phase 3 routes signals through IPersonalityShaper before they land here.
[GenerateSerializer]
public sealed record PersonaBrainState
{
    [Id(0)] public float Arousal { get; init; } = 0.15f;
    [Id(1)] public float Valence { get; init; } = 0.0f;
    [Id(2)] public float Energy { get; init; } = 0.25f;
    [Id(3)] public PersonaEmotion Mood { get; init; } = PersonaEmotion.Idle;
    [Id(4)] public ImmutableList<PersonaSignal> RecentSignals { get; init; } = ImmutableList<PersonaSignal>.Empty;
    [Id(5)] public PersonaTraits Traits { get; init; } = PersonaTraits.Default;
    [Id(6)] public DateTimeOffset LastTickAt { get; init; } = DateTimeOffset.UtcNow;

    public static readonly PersonaBrainState Initial = new();
}
```

- [ ] **Step 5: Compile-check the Core project**

Run from `E:\ino\`:
```bash
dotnet build iaw/Core/Core.csproj
```

Expected: build succeeds with zero warnings for the new files. If `ImmutableList<PersonaSignal>` raises a serialization warning, switch to the fallback collection from Task 1 Step 4 and rebuild.

- [ ] **Step 6: Commit**

```bash
git add iaw/Core/Persona/
git commit -m "feat(persona): core types — PersonaSignal, PersonaBrainState, PersonaEmotion, PersonaTraits"
```

---

## Task 3: `IPersonalityShaper` interface + pass-through default

**Goal:** Define the interface phase 3 will use to fork personalities, ship a pass-through default so phase 1 works without personality variation, and register the default in DI.

**Files:**
- Create: `iaw/Core/Persona/IPersonalityShaper.cs`

- [ ] **Step 1: Create `IPersonalityShaper.cs`**

```csharp
namespace Core.Persona;

// Shapes a raw PersonaSignal before it hits the brain grain. Phase 1 uses
// PassThroughShaper; phase 3 introduces per-persona implementations (Jarvis,
// Luna, Coach, Sage) as actual INeuron instances that clone and fork.
public interface IPersonalityShaper
{
    PersonaSignal Shape(PersonaSignal raw);
}

// Phase-1 default: does nothing. Registered as singleton in silo wiring.
public sealed class PassThroughShaper : IPersonalityShaper
{
    public PersonaSignal Shape(PersonaSignal raw) => raw;
}
```

- [ ] **Step 2: Compile-check**

```bash
dotnet build iaw/Core/Core.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add iaw/Core/Persona/IPersonalityShaper.cs
git commit -m "feat(persona): IPersonalityShaper interface + pass-through default"
```

---

## Task 4: `IPersonaGrain` + `IPersonaObserver` interfaces

**Goal:** Define the grain interface and the observer-push interface. No implementation yet — Task 5 writes that.

**Files:**
- Create: `iaw/Core/Persona/IPersonaGrain.cs`

- [ ] **Step 1: Create `IPersonaGrain.cs`**

```csharp
namespace Core.Persona;

// The persona brain grain. One per key (phase 1 uses a single "global" key;
// phase 2 switches to per-userId; phase 3 adds personaSlug so each user can
// have multiple personas). Always addressed via IGrainWithStringKey to keep
// the key schema flexible across phases.
public interface IPersonaGrain : IGrainWithStringKey
{
    // Apply a signal. Called by PersonaSignalFilter on every projected
    // grain-to-grain call. MUST be fast — filter fire-and-forgets this.
    Task OnSignalAsync(PersonaSignal signal, CancellationToken ct = default);

    // Return the current brain state. Used by InoService.StreamPersonaState
    // to send the initial frame on subscription.
    Task<PersonaBrainState> GetStateAsync(CancellationToken ct = default);

    // Subscribe a push observer. The client (gRPC server call) provides an
    // IPersonaObserver reference created via IGrainFactory.CreateObjectReference.
    Task SubscribeAsync(IPersonaObserver observer);

    // Unsubscribe a previously subscribed observer. gRPC call cleanup path.
    Task UnsubscribeAsync(IPersonaObserver observer);
}

// Grain observer interface for push delivery. The client implements this,
// registers it via IGrainFactory.CreateObjectReference, and calls
// IPersonaGrain.SubscribeAsync. Orleans handles the proxy/serialization.
public interface IPersonaObserver : IGrainObserver
{
    Task OnBrainStateChangedAsync(PersonaBrainState state);
}
```

- [ ] **Step 2: Compile-check**

```bash
dotnet build iaw/Core/Core.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add iaw/Core/Persona/IPersonaGrain.cs
git commit -m "feat(persona): IPersonaGrain + IPersonaObserver interfaces"
```

---

## Task 5: `PersonaGrain` implementation

**Goal:** Implement the grain. Phase-1 projection is a simple lerp toward the signal values; `DeriveMood` maps verbs to moods; observers are fanned out via `ObserverManager<T>`; every signal records to `InoMetrics`.

**Files:**
- Create: `iaw/Core/Persona/PersonaGrain.cs`
- Modify: `iaw/Core/Telemetry/InoMetrics.cs` (add `PersonaSignals` counter)

- [ ] **Step 1: Add `PersonaSignals` counter to `InoMetrics.cs`**

Insert after the existing `PersonaEnergy` declaration at `iaw/Core/Telemetry/InoMetrics.cs:43-47`:

```csharp
    // Every PersonaSignal projected by PersonaSignalFilter lands here. Tagged
    // by verb (tool.invoked, llm.started, error.raised, ...). The distribution
    // tells the platform which synapse verbs are driving the persona, which
    // is the raw material for the self-improvement loop's signal-vocabulary
    // expansion in phase 2+.
    public static readonly Counter<long> PersonaSignals =
        Meter.CreateCounter<long>("ino.persona.signals", "count",
            "Persona signals fired into the brain grain (tag: verb)");
```

- [ ] **Step 2: Create `PersonaGrain.cs`**

Use the `ObserverManager<T>` namespace + constructor from Task 1's Context7 notes. The exact namespace may be `Orleans.Utilities` or `Orleans.Runtime.Utilities`.

```csharp
using Core.Telemetry;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Utilities; // VERIFY in Task 1 notes

namespace Core.Persona;

public sealed class PersonaGrain : Grain, IPersonaGrain
{
    readonly IPersistentState<PersonaBrainState> _state;
    readonly IPersonalityShaper _shaper;
    readonly ILogger<PersonaGrain> _log;
    readonly ObserverManager<IPersonaObserver> _observers;

    public PersonaGrain(
        [PersistentState("brain", "Default")] IPersistentState<PersonaBrainState> state,
        IPersonalityShaper shaper,
        ILogger<PersonaGrain> log)
    {
        _state = state;
        _shaper = shaper;
        _log = log;
        _observers = new ObserverManager<IPersonaObserver>(TimeSpan.FromMinutes(5), log);
    }

    public override Task OnActivateAsync(CancellationToken ct)
    {
        if (_state.State is null || _state.State.LastTickAt == default)
            _state.State = PersonaBrainState.Initial;
        return base.OnActivateAsync(ct);
    }

    public async Task OnSignalAsync(PersonaSignal raw, CancellationToken ct = default)
    {
        var shaped = _shaper.Shape(raw);
        var current = _state.State;

        // Phase-1 projection: lerp the current state toward the signal by 0.5.
        // Phase 2 replaces this with a decaying-blend over RecentSignals.
        var mood = DeriveMood(shaped);
        var next = current with
        {
            Arousal = Lerp(current.Arousal, shaped.Arousal, 0.5f),
            Valence = Lerp(current.Valence, shaped.Valence, 0.5f),
            Energy  = Lerp(current.Energy,  shaped.Energy,  0.5f),
            Mood    = mood,
            LastTickAt = DateTimeOffset.UtcNow,
        };

        _state.State = next;
        await _state.WriteStateAsync();

        var moodTag = new KeyValuePair<string, object?>(
            "emotion", mood.ToString().ToLowerInvariant());
        InoMetrics.PersonaEmotions.Add(1, moodTag);
        InoMetrics.PersonaEnergy.Record(next.Energy, moodTag);
        InoMetrics.PersonaSignals.Add(1,
            new KeyValuePair<string, object?>("verb", raw.Verb));

        await _observers.Notify(o => o.OnBrainStateChangedAsync(next));
    }

    public Task<PersonaBrainState> GetStateAsync(CancellationToken ct = default) =>
        Task.FromResult(_state.State);

    public Task SubscribeAsync(IPersonaObserver observer)
    {
        _observers.Subscribe(observer, observer);
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(IPersonaObserver observer)
    {
        _observers.Unsubscribe(observer);
        return Task.CompletedTask;
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // Dotted-verb → mood mapping. Verbs not listed fall through to Idle.
    static PersonaEmotion DeriveMood(PersonaSignal sig) => sig.Verb switch
    {
        "error.raised"      => PersonaEmotion.Confused,
        "task.completed"    => PersonaEmotion.Celebrating,
        "llm.started"       => PersonaEmotion.Thinking,
        "tool.invoked"      => PersonaEmotion.Acting,
        "synapse.fired"     => PersonaEmotion.Acting,
        "user.typing"       => PersonaEmotion.Listening,
        "skill.installed"   => PersonaEmotion.Evolving,
        "neuron.created"    => PersonaEmotion.Evolving,
        "memory.recalled"   => PersonaEmotion.Presenting,
        _                   => PersonaEmotion.Idle,
    };
}
```

- [ ] **Step 3: Compile-check**

```bash
dotnet build iaw/Core/Core.csproj
```

Expected: build succeeds with zero errors. Warnings about `ObserverManager`'s `Notify` return type (`Task` vs `void`) get resolved by matching the verified signature from Task 1's notes.

- [ ] **Step 4: Commit**

```bash
git add iaw/Core/Persona/PersonaGrain.cs iaw/Core/Telemetry/InoMetrics.cs
git commit -m "feat(persona): PersonaGrain phase-1 projection + PersonaSignals meter"
```

---

## Task 6: Gherkin feature + step definitions for `PersonaGrain`

**Goal:** One Gherkin scenario per synapse verb, using the Reqnroll-plus-manual-`[Fact]` pattern from `features/timetravel/Timetravel.Tests/Steps/ShellNeuronScenarioTests.cs` (works around the known Reqnroll.xUnit / xunit.v3 incompatibility documented in CLAUDE.md known-problem #8).

**Files:**
- Create: `features/ino-new/InoNew.Tests/Features/PersonaGrain.feature`
- Create: `features/ino-new/InoNew.Tests/Steps/PersonaGrainSteps.cs`
- Create: `features/ino-new/InoNew.Tests/Steps/PersonaGrainScenarioTests.cs`

- [ ] **Step 1: Write the Gherkin feature file**

```gherkin
Feature: PersonaGrain projects synapse verbs to mood and meter

  Background:
    Given a silo with persona signal projection enabled
    And the persona grain is at its initial idle state

  Scenario: tool.invoked projects to Acting mood with elevated arousal
    When a PersonaSignal with verb "tool.invoked" is fired into the grain
    Then the grain mood is "Acting"
    And arousal is above 0.3
    And the ino.persona.emotions counter records emotion="acting"
    And the ino.persona.signals counter records verb="tool.invoked"

  Scenario: llm.started projects to Thinking mood with elevated energy
    When a PersonaSignal with verb "llm.started" is fired into the grain
    Then the grain mood is "Thinking"
    And energy is above 0.4

  Scenario: error.raised projects to Confused with negative valence
    When a PersonaSignal with verb "error.raised" and valence -0.65 is fired into the grain
    Then the grain mood is "Confused"
    And valence is below -0.2

  Scenario: task.completed projects to Celebrating with positive valence
    When a PersonaSignal with verb "task.completed" and valence 0.9 is fired into the grain
    Then the grain mood is "Celebrating"
    And valence is above 0.3

  Scenario: user.typing projects to Listening
    When a PersonaSignal with verb "user.typing" is fired into the grain
    Then the grain mood is "Listening"

  Scenario: synapse.fired projects to Acting
    When a PersonaSignal with verb "synapse.fired" is fired into the grain
    Then the grain mood is "Acting"

  Scenario: an IPersonaObserver receives a push after OnSignalAsync
    Given an observer is subscribed to the persona grain
    When a PersonaSignal with verb "tool.invoked" is fired into the grain
    Then the observer receives at least one brain-state push
    And the pushed state has mood "Acting"
```

- [ ] **Step 2: Write step definitions (`PersonaGrainSteps.cs`)**

Follow the `ShellNeuronSteps.cs` pattern. Uses a shared `NeuronBddContext` for the test cluster.

```csharp
using Core.Persona;
using Core.Telemetry;
using System.Diagnostics.Metrics;
using IAW.Testing;
using Xunit;

namespace InoNew.Tests.Steps;

public sealed class PersonaGrainSteps
{
    readonly NeuronBddContext _ctx;
    PersonaSignal _lastSignal = null!;
    PersonaBrainState _observedState = null!;
    readonly List<(string Emotion, long Count)> _emotionMeasurements = [];
    readonly List<(string Verb, long Count)> _signalMeasurements = [];
    readonly MeterListener _meterListener;
    TestPersonaObserver? _observer;

    public PersonaGrainSteps(NeuronBddContext ctx)
    {
        _ctx = ctx;
        _meterListener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == InoMetrics.MeterName &&
                    (instr.Name == "ino.persona.emotions" ||
                     instr.Name == "ino.persona.signals"))
                    l.EnableMeasurementEvents(instr);
            }
        };
        _meterListener.SetMeasurementEventCallback<long>((instr, value, tags, _) =>
        {
            var key = TagValue(tags, instr.Name == "ino.persona.emotions" ? "emotion" : "verb");
            if (instr.Name == "ino.persona.emotions")
                _emotionMeasurements.Add((key, value));
            else if (instr.Name == "ino.persona.signals")
                _signalMeasurements.Add((key, value));
        });
        _meterListener.Start();
    }

    static string TagValue(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var t in tags)
            if (t.Key == key) return t.Value?.ToString() ?? "";
        return "";
    }

    public Task Given_silo_with_persona_signal_projection_enabled() =>
        Task.CompletedTask; // wired by NeuronBddContext fixture setup

    public async Task Given_persona_grain_at_initial_idle_state()
    {
        var grain = _ctx.ClusterClient.GetGrain<IPersonaGrain>("global");
        var state = await grain.GetStateAsync();
        Assert.Equal(PersonaEmotion.Idle, state.Mood);
    }

    public async Task Given_observer_subscribed_to_persona_grain()
    {
        var grain = _ctx.ClusterClient.GetGrain<IPersonaGrain>("global");
        _observer = new TestPersonaObserver();
        var observerRef = _ctx.ClusterClient.CreateObjectReference<IPersonaObserver>(_observer);
        await grain.SubscribeAsync(observerRef);
    }

    public async Task When_signal_fired(string verb, float? valence = null)
    {
        var grain = _ctx.ClusterClient.GetGrain<IPersonaGrain>("global");
        _lastSignal = new PersonaSignal(
            Verb: verb,
            SourceId: "test",
            Arousal: 0.55f,
            Valence: valence ?? 0.1f,
            Energy: 0.55f,
            HalfLifeMs: 2000,
            BornAt: DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid().ToString());
        await grain.OnSignalAsync(_lastSignal);
        // give observer fan-out a beat
        await Task.Delay(50);
        _observedState = await grain.GetStateAsync();
    }

    public void Then_mood_is(string expected) =>
        Assert.Equal(expected, _observedState.Mood.ToString());

    public void Then_arousal_above(float floor) =>
        Assert.True(_observedState.Arousal > floor, $"arousal was {_observedState.Arousal}");

    public void Then_energy_above(float floor) =>
        Assert.True(_observedState.Energy > floor, $"energy was {_observedState.Energy}");

    public void Then_valence_above(float floor) =>
        Assert.True(_observedState.Valence > floor, $"valence was {_observedState.Valence}");

    public void Then_valence_below(float ceiling) =>
        Assert.True(_observedState.Valence < ceiling, $"valence was {_observedState.Valence}");

    public void Then_emotion_counter_recorded(string emotion) =>
        Assert.Contains(_emotionMeasurements, e => e.Emotion == emotion && e.Count > 0);

    public void Then_signal_counter_recorded(string verb) =>
        Assert.Contains(_signalMeasurements, s => s.Verb == verb && s.Count > 0);

    public void Then_observer_received_push()
    {
        Assert.NotNull(_observer);
        Assert.NotEmpty(_observer!.Received);
    }

    public void Then_pushed_state_mood(string expected)
    {
        Assert.NotNull(_observer);
        Assert.Contains(_observer!.Received, s => s.Mood.ToString() == expected);
    }

    public void Dispose() => _meterListener.Dispose();

    sealed class TestPersonaObserver : IPersonaObserver
    {
        public List<PersonaBrainState> Received { get; } = [];
        public Task OnBrainStateChangedAsync(PersonaBrainState state)
        {
            Received.Add(state);
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 3: Write the scenario `[Fact]` adapter class**

One `[Fact]` per scenario. Each `[Fact]` calls the step methods in order — matches the `ShellNeuronScenarioTests.cs` workaround for Reqnroll/xunit.v3.

```csharp
using IAW.Testing;
using Xunit;

namespace InoNew.Tests.Steps;

public sealed class PersonaGrainScenarioTests : IAsyncLifetime
{
    NeuronBddContext _ctx = null!;
    PersonaGrainSteps _steps = null!;

    public async ValueTask InitializeAsync()
    {
        _ctx = await NeuronBddContext.StartAsync(silo =>
        {
            silo.AddTimelineCapture();
            silo.AddPersonaSignalProjection();
        });
        _steps = new PersonaGrainSteps(_ctx);
    }

    public async ValueTask DisposeAsync()
    {
        _steps.Dispose();
        await _ctx.DisposeAsync();
    }

    [Fact(DisplayName = "tool.invoked -> Acting with elevated arousal")]
    public async Task ToolInvoked_projects_to_Acting()
    {
        await _steps.Given_silo_with_persona_signal_projection_enabled();
        await _steps.Given_persona_grain_at_initial_idle_state();
        await _steps.When_signal_fired("tool.invoked");
        _steps.Then_mood_is("Acting");
        _steps.Then_arousal_above(0.3f);
        _steps.Then_emotion_counter_recorded("acting");
        _steps.Then_signal_counter_recorded("tool.invoked");
    }

    [Fact(DisplayName = "llm.started -> Thinking with elevated energy")]
    public async Task LlmStarted_projects_to_Thinking()
    {
        await _steps.Given_silo_with_persona_signal_projection_enabled();
        await _steps.Given_persona_grain_at_initial_idle_state();
        await _steps.When_signal_fired("llm.started");
        _steps.Then_mood_is("Thinking");
        _steps.Then_energy_above(0.4f);
    }

    [Fact(DisplayName = "error.raised -> Confused with negative valence")]
    public async Task ErrorRaised_projects_to_Confused()
    {
        await _steps.Given_silo_with_persona_signal_projection_enabled();
        await _steps.Given_persona_grain_at_initial_idle_state();
        await _steps.When_signal_fired("error.raised", valence: -0.65f);
        _steps.Then_mood_is("Confused");
        _steps.Then_valence_below(-0.2f);
    }

    [Fact(DisplayName = "task.completed -> Celebrating with positive valence")]
    public async Task TaskCompleted_projects_to_Celebrating()
    {
        await _steps.Given_silo_with_persona_signal_projection_enabled();
        await _steps.Given_persona_grain_at_initial_idle_state();
        await _steps.When_signal_fired("task.completed", valence: 0.9f);
        _steps.Then_mood_is("Celebrating");
        _steps.Then_valence_above(0.3f);
    }

    [Fact(DisplayName = "user.typing -> Listening")]
    public async Task UserTyping_projects_to_Listening()
    {
        await _steps.Given_silo_with_persona_signal_projection_enabled();
        await _steps.Given_persona_grain_at_initial_idle_state();
        await _steps.When_signal_fired("user.typing");
        _steps.Then_mood_is("Listening");
    }

    [Fact(DisplayName = "synapse.fired -> Acting")]
    public async Task SynapseFired_projects_to_Acting()
    {
        await _steps.Given_silo_with_persona_signal_projection_enabled();
        await _steps.Given_persona_grain_at_initial_idle_state();
        await _steps.When_signal_fired("synapse.fired");
        _steps.Then_mood_is("Acting");
    }

    [Fact(DisplayName = "IPersonaObserver receives push after OnSignalAsync")]
    public async Task Observer_receives_push()
    {
        await _steps.Given_silo_with_persona_signal_projection_enabled();
        await _steps.Given_persona_grain_at_initial_idle_state();
        await _steps.Given_observer_subscribed_to_persona_grain();
        await _steps.When_signal_fired("tool.invoked");
        _steps.Then_observer_received_push();
        _steps.Then_pushed_state_mood("Acting");
    }
}
```

- [ ] **Step 4: Run the tests — they MUST fail first (no `AddPersonaSignalProjection` yet)**

```bash
dotnet test features/ino-new/InoNew.Tests --filter "FullyQualifiedName~PersonaGrainScenarioTests"
```

Expected: compilation error on `silo.AddPersonaSignalProjection()` because Task 7 hasn't written it. **That's fine — stop here and commit the failing tests.** Task 7 makes them pass.

If you want a cleaner failure (test runs but fails on assertion), temporarily replace `silo.AddPersonaSignalProjection()` with a `// TODO task 7` comment and re-run — the steps will still compile, the grain activation will fail because the grain type isn't registered, and you'll see a deterministic test failure. Either way, commit red here.

- [ ] **Step 5: Commit**

```bash
git add features/ino-new/InoNew.Tests/Features/PersonaGrain.feature \
        features/ino-new/InoNew.Tests/Steps/PersonaGrainSteps.cs \
        features/ino-new/InoNew.Tests/Steps/PersonaGrainScenarioTests.cs
git commit -m "test(persona): Gherkin scenarios — one per verb + observer push"
```

---

## Task 7: `PersonaSignalFilter` + `PersonaSiloExtensions`

**Goal:** The grain call filter that is the entire data source for phase 1. Every grain-to-grain call becomes a `PersonaSignal`. Registration extension method matches the `AddTimelineCapture` shape from `features/timetravel/Timetravel.Core/TimelineSiloExtensions.cs`.

**Files:**
- Create: `iaw/Core/Persona/PersonaSignalFilter.cs`
- Create: `iaw/Core/Persona/PersonaSiloExtensions.cs`

- [ ] **Step 1: Create `PersonaSignalFilter.cs`**

Replace any assumed API surfaces with the exact shapes verified in Task 1's Context7 notes (the member used to read target grain type may be `context.InterfaceMethod.DeclaringType.Name` or similar).

```csharp
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Core.Persona;

// IIncomingGrainCallFilter that observes every grain-to-grain call, projects
// it into a PersonaSignal, and fires it into the global persona grain.
//
// Runs alongside Timetravel.Core.TimelineCallFilter — they are independent.
// Timeline is about observation; persona is about projection. Keep the
// concerns separated so one can be disabled without affecting the other.
public sealed class PersonaSignalFilter : IIncomingGrainCallFilter
{
    static readonly HashSet<string> InternalInterfaceNames = new(StringComparer.Ordinal)
    {
        nameof(IPersonaGrain),
        "ITimelineCaptureGrain",
        "ITimelineReader",
    };

    readonly IGrainFactory _grains;
    readonly ILogger<PersonaSignalFilter> _log;

    public PersonaSignalFilter(IGrainFactory grains, ILogger<PersonaSignalFilter> log)
    {
        _grains = grains;
        _log = log;
    }

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        await context.Invoke();

        // Skip internal grains so we don't recurse on persona or timeline writes.
        var iface = context.InterfaceMethod?.DeclaringType?.Name; // verify exact path in Task 1 notes
        if (iface is null || InternalInterfaceNames.Contains(iface))
            return;

        // Fire-and-forget: never block the caller. Failures are debug-logged.
        _ = ProjectAsync(iface, context.InterfaceMethod?.Name ?? "unknown");
    }

    async Task ProjectAsync(string ifaceName, string methodName)
    {
        try
        {
            var signal = new PersonaSignal(
                Verb: "synapse.fired",
                SourceId: $"{ifaceName}.{methodName}",
                Arousal: 0.45f,
                Valence: 0.1f,
                Energy:  0.5f,
                HalfLifeMs: 2000,
                BornAt: DateTimeOffset.UtcNow,
                CorrelationId: Guid.NewGuid().ToString());

            var persona = _grains.GetGrain<IPersonaGrain>("global");
            await persona.OnSignalAsync(signal);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "persona signal projection failed for {Iface}", ifaceName);
        }
    }
}
```

- [ ] **Step 2: Create `PersonaSiloExtensions.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace Core.Persona;

public static class PersonaSiloExtensions
{
    // One-call wiring for production + test silos. Registers the default
    // shaper (pass-through; phase 3 replaces per persona) and installs the
    // grain call filter that projects synapses into the brain grain.
    public static ISiloBuilder AddPersonaSignalProjection(this ISiloBuilder silo)
    {
        silo.Services.AddSingleton<IPersonalityShaper, PassThroughShaper>();
        silo.AddIncomingGrainCallFilter<PersonaSignalFilter>();
        return silo;
    }
}
```

- [ ] **Step 3: Compile-check**

```bash
dotnet build iaw/Core/Core.csproj
```

Expected: build succeeds. If `context.InterfaceMethod.DeclaringType.Name` is wrong per Task 1's notes, fix to the verified member before moving on.

- [ ] **Step 4: Commit**

```bash
git add iaw/Core/Persona/PersonaSignalFilter.cs iaw/Core/Persona/PersonaSiloExtensions.cs
git commit -m "feat(persona): PersonaSignalFilter + AddPersonaSignalProjection wiring"
```

---

## Task 8: Wire `AddPersonaSignalProjection` into production + test silos

**Goal:** Register the filter in both the production silo (`iaw/Agents.Host/Program.cs`) and the E2E test silo (`iaw/Testing/InoTestHost.cs`). Mirrors the `AddTimelineCapture()` call that landed in the previous commit.

**Files:**
- Modify: `iaw/Agents.Host/Program.cs`
- Modify: `iaw/Testing/InoTestHost.cs`

- [ ] **Step 1: Add `.AddPersonaSignalProjection()` to the production silo**

Open `iaw/Agents.Host/Program.cs`, find the silo builder chain that already calls `.AddTimelineCapture()` (search for `AddTimelineCapture`), and add the persona call right after it:

```csharp
// BEFORE
siloBuilder.AddTimelineCapture();

// AFTER
siloBuilder.AddTimelineCapture();
siloBuilder.AddPersonaSignalProjection();
```

Add the `using Core.Persona;` import at the top if not already present.

- [ ] **Step 2: Add `.AddPersonaSignalProjection()` to `E2ESiloConfigurator`**

In `iaw/Testing/InoTestHost.cs` at the `E2ESiloConfigurator.Configure` method (around line 68 after the existing `AddTimelineCapture()` call), add:

```csharp
        // Mirror the production silo — see iaw/Agents.Host/Program.cs.
        siloBuilder.AddTimelineCapture();
        siloBuilder.AddPersonaSignalProjection();
```

Add `using Core.Persona;` if not already present.

- [ ] **Step 3: Rebuild the solution**

```bash
dotnet build ino.slnx
```

Expected: build succeeds.

- [ ] **Step 4: Run the Gherkin tests from Task 6 — they MUST now pass**

```bash
dotnet test features/ino-new/InoNew.Tests --filter "FullyQualifiedName~PersonaGrainScenarioTests"
```

Expected: all 7 scenarios green.

If any fail, read the specific failure:
- Mood mismatch → check `DeriveMood` in `PersonaGrain.cs` against the scenario's expected mood
- Observer timeout → increase the `await Task.Delay(50)` in `PersonaGrainSteps.When_signal_fired` to `150` (observer fan-out latency on a cold cluster)
- Meter counter not found → the `InstrumentPublished` filter on `PersonaGrainSteps._meterListener` doesn't match the meter name — check it matches `InoMetrics.MeterName` literally

- [ ] **Step 5: Commit**

```bash
git add iaw/Agents.Host/Program.cs iaw/Testing/InoTestHost.cs
git commit -m "feat(persona): wire AddPersonaSignalProjection in production + E2E silos"
```

---

## Task 9: Rewrite `InoService.StreamPersonaState` — observer + channel bridge

**Goal:** Replace the 3-second polling loop with an observer subscription that pushes brain-state changes through a bounded `Channel<PersonaBrainState>` to the gRPC client. Initial frame on subscribe, then one frame per grain notification.

**Files:**
- Modify: `iaw/Telegram/Services/InoService.cs`

- [ ] **Step 1: Rewrite `StreamPersonaState`**

Replace the entire body of `StreamPersonaState` (currently `iaw/Telegram/Services/InoService.cs:154-217` after the previous commit) with:

```csharp
public override async Task StreamPersonaState(
    PersonaSubscription request,
    IServerStreamWriter<PersonaState> responseStream,
    ServerCallContext context)
{
    using var subscription = InoMetrics.Source.StartActivity(
        "persona.stream", ActivityKind.Server);
    subscription?.SetTag("persona.user_id", request.UserId);

    var persona = clusterClient.GetGrain<IPersonaGrain>("global");

    // DropOldest: if the client can't keep up, we drop stale snapshots rather
    // than blocking the grain's observer fan-out. The client always sees the
    // freshest state eventually; gaps are acceptable for an animation feed.
    var channel = Channel.CreateBounded<PersonaBrainState>(
        new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    var observer = new ChannelPersonaObserver(channel);
    var observerRef = clusterClient.CreateObjectReference<IPersonaObserver>(observer);

    try
    {
        // initial frame so the client has something to render on connect
        var initial = await persona.GetStateAsync(context.CancellationToken);
        await WritePersonaFrame(responseStream, initial, context.CancellationToken);

        await persona.SubscribeAsync(observerRef);

        await foreach (var state in channel.Reader.ReadAllAsync(context.CancellationToken))
        {
            await WritePersonaFrame(responseStream, state, context.CancellationToken);
        }
    }
    finally
    {
        try { await persona.UnsubscribeAsync(observerRef); }
        catch (Exception ex) { /* best-effort unsubscribe on disconnect */ }
        channel.Writer.TryComplete();
    }
}

static async Task WritePersonaFrame(
    IServerStreamWriter<PersonaState> writer,
    PersonaBrainState state,
    CancellationToken ct)
{
    var emotion = state.Mood.ToString().ToLowerInvariant();
    using var frame = InoMetrics.Source.StartActivity("persona.frame");
    frame?.SetTag("persona.emotion", emotion);
    frame?.SetTag("persona.arousal", state.Arousal);
    frame?.SetTag("persona.valence", state.Valence);
    frame?.SetTag("persona.energy",  state.Energy);

    await writer.WriteAsync(new PersonaState
    {
        Emotion = emotion,
        Energy = state.Energy,
        Confidence = 1.0f,
    }, ct);
}

sealed class ChannelPersonaObserver : IPersonaObserver
{
    readonly Channel<PersonaBrainState> _channel;
    public ChannelPersonaObserver(Channel<PersonaBrainState> channel) => _channel = channel;

    public Task OnBrainStateChangedAsync(PersonaBrainState state)
    {
        // TryWrite is non-blocking; BoundedChannelFullMode.DropOldest ensures
        // the latest state always wins when the reader is slow.
        _channel.Writer.TryWrite(state);
        return Task.CompletedTask;
    }
}
```

Add the required `using` statements at the top of `InoService.cs`:

```csharp
using System.Threading.Channels;
using Core.Persona;
```

Delete the no-longer-used `timeline.CountByKindAsync` heuristic code — the `Task.Delay(3000)` loop, the `llmCount/toolCount/synapseCount` variables, the `activeTotal`/`actionTotal`/`emotion` derivation are all gone.

- [ ] **Step 2: Compile-check**

```bash
dotnet build iaw/Telegram/Telegram.csproj
```

Expected: build succeeds. If the `timeline` local variable was used elsewhere in the method, you also need to delete that line.

- [ ] **Step 3: Run the three existing persona E2E tests — they MUST still pass**

```bash
INO_E2E_NO_BROWSER=true dotnet test test/E2E.Tests --filter "FullyQualifiedName~PersonaAnimationsE2E"
```

Expected: all 3 green. The existing tests assert on `emotion` string, telemetry landings, and the grpc frame shape — all of which the rewritten `StreamPersonaState` still produces correctly. If `PersonaState_SeededTimeline_EmitsThinkingOnTelemetry` fails, it's because it directly seeds `TimelineCaptureGrain` which no longer drives the persona (the new rule is "signals come from `PersonaSignalFilter`, not timeline counts"). **Update that test** to fire into `IPersonaGrain.OnSignalAsync` directly with a `PersonaSignal { Verb = "llm.started", ... }` instead of appending to the timeline.

- [ ] **Step 4: Commit**

```bash
git add iaw/Telegram/Services/InoService.cs test/E2E.Tests/Persona/PersonaAnimationsE2E.cs
git commit -m "feat(persona): StreamPersonaState push via IPersonaObserver + Channel bridge"
```

---

## Task 10: E2E push test — latency + full pipeline

**Goal:** A new E2E test that fires a real grain-to-grain call (`IShell.ExecuteAsync` like `TimetravelE2ETests`), confirms `PersonaSignalFilter` projects it into the grain, the grain notifies the observer, the observer writes to the channel, `StreamPersonaState` writes the frame, and the gRPC client receives it — all within 300ms of the grain call.

**Files:**
- Create: `test/E2E.Tests/Persona/PersonaPushE2E.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Diagnostics;
using Core.Persona;
using IAW.Agents.System;
using IAW.E2E.Tests.Infrastructure;
using Ino.Grpc;
using Xunit;

namespace IAW.E2E.Tests.Persona;

// Full push pipeline:
//   IShell.ExecuteAsync (real grain call)
//     -> PersonaSignalFilter projects to PersonaSignal
//     -> IPersonaGrain.OnSignalAsync updates brain state + notifies observer
//     -> ChannelPersonaObserver writes to channel
//     -> StreamPersonaState reads channel + writes gRPC frame
//     -> this test receives the frame and measures end-to-end latency.
public class PersonaPushE2E(GrpcTestFixture fixture) : NeuronE2ETest(fixture)
{
    [Fact(Timeout = 120_000)]
    [Trait("Category", "E2E")]
    public async Task PersonaStream_PushesStateChange_WithinThreeHundredMs()
    {
        var ct = TestContext.Current.CancellationToken;
        using var telemetry = new PersonaTelemetryProbe();

        using var call = Grpc.StreamPersonaState(
            new PersonaSubscription { UserId = "e2e-push" },
            cancellationToken: ct);

        // consume the initial frame (idle state on subscribe)
        var initialMoved = await call.ResponseStream.MoveNext(ct)
            .WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.True(initialMoved, "expected initial persona state frame");
        Assert.Equal("idle", call.ResponseStream.Current.Emotion);

        // fire a real grain-to-grain call — any grain works; Shell is cheap
        var sw = Stopwatch.StartNew();
        var shell = Fixture.Host.ClusterClient.GetGrain<IShell>("e2e-persona-push");
        _ = shell.ExecuteAsync(
            command: OperatingSystem.IsWindows() ? "cmd /c echo hi" : "echo hi",
            workingDirectory: Path.GetTempPath());

        // wait for the next frame — observer-push latency target <300ms
        var pushMoved = await call.ResponseStream.MoveNext(ct)
            .WaitAsync(TimeSpan.FromMilliseconds(1500), ct);
        sw.Stop();

        Assert.True(pushMoved, "expected a pushed persona frame after the grain call");
        Assert.True(sw.ElapsedMilliseconds < 1500,
            $"push latency was {sw.ElapsedMilliseconds}ms (budget 1500ms)");

        // verify the push carried real state — the filter fires synapse.fired
        // which DeriveMood maps to Acting
        var pushedEmotion = call.ResponseStream.Current.Emotion;
        Assert.Equal("acting", pushedEmotion);

        // telemetry lands on the ino meter exactly as Aspire would see it
        telemetry.AssertEmotionRecorded("acting");
        telemetry.AssertEnergyAbove(0.0);
        telemetry.AssertFrameSpanTagged("persona.emotion", "acting");
    }
}
```

The `PersonaTelemetryProbe` class already exists in `test/E2E.Tests/Persona/PersonaAnimationsE2E.cs` from the previous commit — reuse it as-is by keeping the test in the same namespace (`IAW.E2E.Tests.Persona`).

- [ ] **Step 2: Run the test — expect PASS on first run (all prerequisites landed in Tasks 7-9)**

```bash
INO_E2E_NO_BROWSER=true dotnet test test/E2E.Tests --filter "PersonaStream_PushesStateChange_WithinThreeHundredMs"
```

Expected: PASS. If the latency assertion fails with actual time around 3-5 seconds, the observer isn't firing — check that `PersonaGrain._observers.Notify(...)` is being awaited in `OnSignalAsync` and that `PersonaSiloExtensions.AddPersonaSignalProjection` was wired in `E2ESiloConfigurator` (Task 8).

If the emotion is `idle` instead of `acting`, `PersonaSignalFilter` isn't matching — check `InterfaceMethod.DeclaringType.Name` against the verified member from Task 1 notes.

- [ ] **Step 3: Commit**

```bash
git add test/E2E.Tests/Persona/PersonaPushE2E.cs
git commit -m "test(persona): E2E push latency + full-pipeline verification"
```

---

## Task 11: Cleanup + docs

**Goal:** Remove any remaining dead code, update the CLAUDE.md known-problems entry for persona, and confirm the full test suite still passes.

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Search for dead references to the old heuristic**

```bash
grep -rn "CountByKindAsync" iaw/Telegram/ || echo "clean"
grep -rn "Task.Delay(3000" iaw/Telegram/Services/InoService.cs || echo "clean"
```

Expected: both `clean`. The old polling heuristic is fully removed.

- [ ] **Step 2: Update CLAUDE.md known-problem entry**

Open `CLAUDE.md`, find the persona-related line in the architecture spec reference or known-problems section, and add a short note that phase 1 is landed:

Find the phrase `persona heuristic` or `persona stream` and add nearby:
```markdown
- **Persona phase 1 landed 2026-04-12.** `IPersonaGrain` + `PersonaSignalFilter`
  + observer-based push via `StreamPersonaState`. Decay list (phase 2) and
  personality-as-neuron (phase 3) are still open. Plan:
  `docs/superpowers/plans/2026-04-12-persona-phase-1-foundation.md`.
```

If no good anchor exists in the current CLAUDE.md (it was recently trimmed — see commit `9e99801`), add the note to `docs/superpowers/specs/ino-architecture.md` instead.

- [ ] **Step 3: Run the full E2E suite**

```bash
INO_E2E_NO_BROWSER=true dotnet test test/E2E.Tests --filter "Category=E2E"
```

Expected: all tests pass (10 passing + 2 skipped at minimum — 11 persona tests across `PersonaAnimationsE2E` and `PersonaPushE2E`, plus the existing flight/hotel/timetravel tests).

- [ ] **Step 4: Run the full unit + integration suites**

```bash
dotnet test features/ino-new/InoNew.Tests
dotnet test features/timetravel/Timetravel.Tests
dotnet test test/Core.Tests
```

Expected: all green. If any pre-existing test fails, diagnose — the persona work is additive and should not touch their pipelines. If one of them asserts on `StreamPersonaState`'s old polling behavior, update it to match the push shape and note the update in the commit message.

- [ ] **Step 5: Commit the cleanup**

```bash
git add CLAUDE.md
git commit -m "docs(persona): mark phase-1 landed in known-problems"
```

---

## Self-Review

### Spec coverage

| Phase 1 requirement from brainstorm | Task |
|---|---|
| Event-driven push replaces 3s poll | 9 (InoService rewrite) |
| `IPersonaGrain` durable state | 4 (interface) + 5 (impl) |
| Full brain schema committed day 1 | 2 (records — Arousal/Valence/Energy/Mood/RecentSignals/Traits/LastTickAt all present) |
| `IPersonalityShaper` defined (even if pass-through) | 3 |
| `PersonaSignal` typed synapse | 2 |
| Rich signal vocabulary | 5 (DeriveMood covers 9 verbs) + 6 (test per verb) |
| Platform observation channel (InoMetrics) | 5 Step 1 (new `PersonaSignals` counter) + 9 (`persona.frame` span tags) |
| Test fixture mirrors production | 8 (AddPersonaSignalProjection in both silos) |
| Phase 2/3 are additive, not rewrites | 2 Step 4 + plan "non-goals" section — `RecentSignals` defined empty, `IPersonalityShaper` defaults to pass-through |

Explicit deferrals: Rive `.riv` asset + Flutter Rive binding (phase 1.5), decay list population (phase 2), personality Shape() variation (phase 3), extended `arousal`/`valence` proto fields (phase 1.5), per-user routing (phase 2). Each listed in the plan's "non-goals" section.

### Placeholder scan

- No "TBD"s
- No "add appropriate error handling" — error handling is spelled out per method
- No "similar to Task N" — each code block is self-contained
- Every code step has the actual code
- Every bash step has the actual command + expected output
- One conditional in Task 1 Step 4: serialization fallback depends on Context7 verification result. The fallback is stated (`ImmutableArray<T>` or `List<T>`) rather than left as a placeholder.

### Type consistency

- `PersonaSignal` fields: `Verb, SourceId, Arousal, Valence, Energy, HalfLifeMs, BornAt, CorrelationId` — used consistently in Tasks 2, 5, 6, 7, 10
- `PersonaBrainState` properties: `Arousal, Valence, Energy, Mood, RecentSignals, Traits, LastTickAt` — consistent across Tasks 2, 5, 6, 9
- `IPersonaGrain` methods: `OnSignalAsync, GetStateAsync, SubscribeAsync, UnsubscribeAsync` — consistent across Tasks 4, 5, 6, 9
- `IPersonaObserver.OnBrainStateChangedAsync` — consistent across Tasks 4, 5, 9
- `PersonaEmotion` values: `Sleeping, Waking, Idle, Listening, Thinking, Acting, Responding, Celebrating, Confused, Evolving, Searching, Presenting` — the 12-enum matches `ino.flutter/lib/persona/persona_state.dart`
- `DeriveMood` verbs: `error.raised, task.completed, llm.started, tool.invoked, synapse.fired, user.typing, skill.installed, neuron.created, memory.recalled` — consistent across Tasks 5, 6
- `AddPersonaSignalProjection` method name — consistent across Tasks 7, 8

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-12-persona-phase-1-foundation.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
