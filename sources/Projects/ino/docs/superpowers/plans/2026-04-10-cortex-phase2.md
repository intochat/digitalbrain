# Phase 2: 5 Specialist Neurons + BDD — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build 5 specialist neurons (Shell, FileDelivery, Scheduler, Recall, Summarizer) with BDD coverage, wired through the Cortex routing engine from Phase 1.

**Architecture:** Specialists are handler classes implementing `ISynapseHandler`. `NeuronGrain.HandleAsync` dispatches to keyed DI services by grain ID. This keeps the universal `INeuron` interface intact — specialists don't need separate grain types. BDD tests verify the full chain: user message → Cortex routing → specialist handling → timeline capture.

**Tech Stack:** C# / Orleans 10 / Microsoft.Extensions.AI / xunit.v3 / Gherkin BDD

**Spec:** `docs/superpowers/specs/2026-04-10-cortex-tui-universes-design.md` — Section 2

---

## File Map

| Action | File | Responsibility |
|--------|------|---------------|
| Create | `features/ino-new/InoNew.Core/ISynapseHandler.cs` | Handler interface for specialist dispatch |
| Modify | `features/ino-new/InoNew.Core/NeuronGrain.cs` | Dispatch HandleAsync to keyed ISynapseHandler |
| Modify | `features/ino-new/InoNew.Core/InoNewSiloExtensions.cs` | Register specialist handlers in DI |
| Create | `features/ino-new/InoNew.Core/Specialists/ShellHandler.cs` | Executes OS commands |
| Create | `features/ino-new/InoNew.Core/Specialists/RecallHandler.cs` | Queries timeline as memory |
| Create | `features/ino-new/InoNew.Core/Specialists/SummarizerHandler.cs` | LLM synthesis over timeline |
| Create | `features/ino-new/InoNew.Core/Specialists/FileDeliveryHandler.cs` | Finds and delivers files |
| Create | `features/ino-new/InoNew.Core/Specialists/SchedulerHandler.cs` | Time-triggered reminders |
| Create | `features/ino-new/InoNew.Tests/Features/ShellSpecialist.feature` | BDD for shell |
| Create | `features/ino-new/InoNew.Tests/Features/RecallSpecialist.feature` | BDD for recall |
| Create | `features/ino-new/InoNew.Tests/Features/SummarizerSpecialist.feature` | BDD for summarizer |
| Create | `features/ino-new/InoNew.Tests/Features/FileDeliverySpecialist.feature` | BDD for file delivery |
| Create | `features/ino-new/InoNew.Tests/Features/SchedulerSpecialist.feature` | BDD for scheduler |
| Create | `features/ino-new/InoNew.Tests/Steps/SpecialistSteps.cs` | Shared step definitions |
| Create | `features/ino-new/InoNew.Tests/Steps/ShellSpecialistTests.cs` | xunit runner |
| Create | `features/ino-new/InoNew.Tests/Steps/RecallSpecialistTests.cs` | xunit runner |
| Create | `features/ino-new/InoNew.Tests/Steps/SummarizerSpecialistTests.cs` | xunit runner |
| Create | `features/ino-new/InoNew.Tests/Steps/FileDeliverySpecialistTests.cs` | xunit runner |
| Create | `features/ino-new/InoNew.Tests/Steps/SchedulerSpecialistTests.cs` | xunit runner |

---

### Task 1: ISynapseHandler interface + NeuronGrain dispatch

**Files:**
- Create: `features/ino-new/InoNew.Core/ISynapseHandler.cs`
- Modify: `features/ino-new/InoNew.Core/NeuronGrain.cs`

- [ ] **Step 1: Create ISynapseHandler**

```csharp
// features/ino-new/InoNew.Core/ISynapseHandler.cs
namespace InoNew.Core;

// Specialist handler interface. Keyed DI services implement this to provide
// custom HandleAsync logic for specific neuron IDs. NeuronGrain dispatches
// to the handler matching its grain key, falling back to the default no-op.
public interface ISynapseHandler
{
    Task<SynapseResult> HandleAsync(Synapse synapse, IGrainFactory grains, CancellationToken ct);
}
```

- [ ] **Step 2: Update NeuronGrain.HandleAsync to dispatch**

Replace the HandleAsync method body to look up a keyed service:

```csharp
    public async Task<SynapseResult> HandleAsync(Synapse synapse, CancellationToken ct = default)
    {
        _state.State.HandledCount++;
        await _state.WriteStateAsync();

        var handler = ServiceProvider.GetKeyedService<ISynapseHandler>(this.GetPrimaryKeyString());
        if (handler is not null)
        {
            _log.LogInformation("Neuron {Id} dispatching to specialist handler (verb={Verb})",
                this.GetPrimaryKeyString(), synapse.Verb);
            return await handler.HandleAsync(synapse, GrainFactory, ct);
        }

        _log.LogInformation("Neuron {Id} handled synapse verb={Verb} from={Source} (default)",
            this.GetPrimaryKeyString(), synapse.Verb, synapse.SourceNeuronId);
        return new SynapseResult(Success: true, Payload: string.Empty, Verb: synapse.Verb);
    }
```

Add `using Microsoft.Extensions.DependencyInjection;` for GetKeyedService.

- [ ] **Step 3: Build and run existing tests**

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj -v normal`
Expected: All 57 existing tests pass (no handler registered = default behavior).

- [ ] **Step 4: Commit**

`feat(ino-new): ISynapseHandler + keyed DI dispatch in NeuronGrain.HandleAsync`

---

### Task 2: RecallHandler + BDD (simplest specialist — uses only existing grains)

**Files:**
- Create: `features/ino-new/InoNew.Core/Specialists/RecallHandler.cs`
- Create: `features/ino-new/InoNew.Tests/Features/RecallSpecialist.feature`
- Create: `features/ino-new/InoNew.Tests/Steps/SpecialistSteps.cs`
- Create: `features/ino-new/InoNew.Tests/Steps/RecallSpecialistTests.cs`
- Modify: `features/ino-new/InoNew.Core/InoNewSiloExtensions.cs`

- [ ] **Step 1: Create RecallHandler**

```csharp
// features/ino-new/InoNew.Core/Specialists/RecallHandler.cs
using Timetravel.Core;

namespace InoNew.Core.Specialists;

public sealed class RecallHandler : ISynapseHandler
{
    public async Task<SynapseResult> HandleAsync(Synapse synapse, IGrainFactory grains, CancellationToken ct)
    {
        var timeline = grains.GetGrain<ITimelineReader>("global");
        var latest = await timeline.GetLatestSequenceAsync(ct);

        if (latest < 0)
            return new SynapseResult(true, "No events recorded yet.", "recall_result");

        var events = await timeline.GetEventsInRangeAsync(0, latest, ct: ct);
        var summary = $"{events.Count} event(s) on the timeline.";

        var synapseEvents = events.Where(e => e.Kind == TimelineEventKind.SynapseFired).ToList();
        if (synapseEvents.Count > 0)
            summary += $" {synapseEvents.Count} synapse(s) fired.";

        return new SynapseResult(true, summary, "recall_result");
    }
}
```

- [ ] **Step 2: Register in InoNewSiloExtensions**

```csharp
using InoNew.Core.Specialists;
using Microsoft.Extensions.DependencyInjection;

namespace InoNew.Core;

public static class InoNewSiloExtensions
{
    public static ISiloBuilder AddInoNew(this ISiloBuilder silo)
    {
        silo.Services.AddKeyedSingleton<ISynapseHandler, RecallHandler>("recall");
        return silo;
    }
}
```

- [ ] **Step 3: Write RecallSpecialist.feature**

```gherkin
Feature: RecallSpecialist

  Scenario: Recall recent activity from timeline
    Given a running test cluster with timeline capture and specialists
    And the Cortex neuron is registered
    And a specialist "recall" is registered with schema "recall_recent: queries timeline for recent events"
    And behavior memory contains an example for "recall" with body "what happened, recent events, recall activity"
    And 3 synapses have been fired on the timeline
    When the user sends "what happened recently?"
    Then Cortex routes to specialist "recall"
    And the specialist responds with a message containing "event"
```

- [ ] **Step 4: Write SpecialistSteps shared steps**

Create shared BDD step class with steps that all specialist tests reuse (cluster setup, cortex registration, specialist registration, behavior memory, assertions). Extend from the CortexSteps pattern.

- [ ] **Step 5: Write RecallSpecialistTests**

xunit.v3 [Fact] runner. Configure mock LLM to route to "recall" when the prompt contains "recall".

- [ ] **Step 6: Run tests**

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj --filter "RecallSpecialistTests" -v normal`

- [ ] **Step 7: Commit**

`feat(ino-new): RecallHandler specialist + BDD — queries timeline as memory`

---

### Task 3: SummarizerHandler + BDD

**Files:**
- Create: `features/ino-new/InoNew.Core/Specialists/SummarizerHandler.cs`
- Create: `features/ino-new/InoNew.Tests/Features/SummarizerSpecialist.feature`
- Create: `features/ino-new/InoNew.Tests/Steps/SummarizerSpecialistTests.cs`
- Modify: `features/ino-new/InoNew.Core/InoNewSiloExtensions.cs`

SummarizerHandler queries timeline + calls IChatClient to synthesize a natural language summary. In tests, the mock LLM returns a canned summary.

- [ ] **Step 1: Create SummarizerHandler**

Uses `IChatClient` injected via constructor. Queries timeline for events, builds prompt with event descriptions, calls LLM for natural language summary.

- [ ] **Step 2: Register in InoNewSiloExtensions**

`silo.Services.AddKeyedSingleton<ISynapseHandler, SummarizerHandler>("summarizer");`

- [ ] **Step 3: Write feature + tests**
- [ ] **Step 4: Run and commit**

`feat(ino-new): SummarizerHandler specialist + BDD — LLM synthesis over timeline`

---

### Task 4: ShellHandler + BDD

**Files:**
- Create: `features/ino-new/InoNew.Core/Specialists/ShellHandler.cs`
- Create: `features/ino-new/InoNew.Tests/Features/ShellSpecialist.feature`
- Create: `features/ino-new/InoNew.Tests/Steps/ShellSpecialistTests.cs`
- Modify: `features/ino-new/InoNew.Core/InoNewSiloExtensions.cs`

ShellHandler executes a command via `System.Diagnostics.Process` (the simplest approach — no dependency on compile-time IShell agent). In tests, mock the execution by checking for a known test command.

- [ ] **Step 1: Create ShellHandler**

Parses command from synapse payload, executes via Process, returns stdout + exit code.

- [ ] **Step 2: Register + write feature + tests**
- [ ] **Step 3: Run and commit**

`feat(ino-new): ShellHandler specialist + BDD — executes OS commands`

---

### Task 5: FileDeliveryHandler + BDD

**Files:**
- Create: `features/ino-new/InoNew.Core/Specialists/FileDeliveryHandler.cs`
- Create: `features/ino-new/InoNew.Tests/Features/FileDeliverySpecialist.feature`
- Create: `features/ino-new/InoNew.Tests/Steps/FileDeliverySpecialistTests.cs`
- Modify: `features/ino-new/InoNew.Core/InoNewSiloExtensions.cs`

FileDeliveryHandler searches for a file by name, reports its path and size. Actual delivery to a surface (Telegram, etc.) is deferred — the handler finds the file and reports what it found.

- [ ] **Step 1: Create FileDeliveryHandler**

Searches for file matching query in a configurable search path. Returns found path + size.

- [ ] **Step 2: Register + write feature + tests**
- [ ] **Step 3: Run and commit**

`feat(ino-new): FileDeliveryHandler specialist + BDD — finds files for delivery`

---

### Task 6: SchedulerHandler + BDD

**Files:**
- Create: `features/ino-new/InoNew.Core/Specialists/SchedulerHandler.cs`
- Create: `features/ino-new/InoNew.Tests/Features/SchedulerSpecialist.feature`
- Create: `features/ino-new/InoNew.Tests/Steps/SchedulerSpecialistTests.cs`
- Modify: `features/ino-new/InoNew.Core/InoNewSiloExtensions.cs`

SchedulerHandler records a reminder in the neuron's state (persisted via the synapse trail). The actual time-triggered firing via Orleans reminders is a follow-up — this slice proves the scheduling intent is captured correctly.

- [ ] **Step 1: Create SchedulerHandler**

Parses task + time from payload, records it as a synapse to timeline, returns confirmation with the scheduled time.

- [ ] **Step 2: Register + write feature + tests**
- [ ] **Step 3: Run and commit**

`feat(ino-new): SchedulerHandler specialist + BDD — records scheduling intent`

---

### Task 7: Full verification + feature file sync

- [ ] **Step 1: Build full solution**
- [ ] **Step 2: Run all tests**
- [ ] **Step 3: Verify all .feature files match implemented tests**
- [ ] **Step 4: Commit any sync fixes**
