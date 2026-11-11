# Project: DigitalBrain DDD Refactoring

## Architecture
DigitalBrain is a distributed, reactive actor runtime built on Microsoft Orleans 10.0 and .NET 11.
This Domain-Driven Design (DDD) refactoring restructures the kernel so that DigitalBrain owns its core domain: an atomic journal protocol (state + signals + cursor committed together, notified to subscribers, caught up from cursors, under unforgeable caller access).

### Core Bounded Contexts
1. **Kernel Context (`DigitalBrain.Kernel`)**: `BrainJournal`, `CommitResult`, `CallCatalog`, `Subscriptions`, `Delivery`, and pure kernel rules (`CallPolicy`, `ReactionTable`, `RetentionMath`, `ScheduleDiff`).
2. **Access Context (`DigitalBrain.Access`)**: Closed `Actor` hierarchy, unforgeable `Caller` value object, `AccessPolicy`, and `AccessGate`.
3. **Units Context (`DigitalBrain.Units`)**: Pure deterministic `UnitMachine`, `StoredUnit<TState>`, and `UnitLoader`.
4. **Simulation Context (`DigitalBrain.Simulation`)**: Standalone test-framework-free in-memory deterministic engine (`SimulatedBrain`, `NeuronHarness`, `ScenarioDifferential`).
5. **Hosting Adapters (`DigitalBrain.Runtime.Orleans`)**: Orleans virtual actor host, grain lifecycle, `NeuronHostGrainBase`, and `[OneWay]` signal nudges.

---

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| F1 | Dead Types & Edge Elimination (R01) | Ruthlessly delete 18 dead fossil types, dead delivery engine (`IBrainSignalLog`, 8 test files / 2,145 LOC), `DigitalBrain.Client`, and empty `AllowedApis.cs` | M1 | Survey §R01 |
| F2 | Two-Phase Gate Saga Elimination (R03) | Eradicate `GateRunner`, `TestGates`, `ProvisionalGateQueue`, `Check`, `GateVerdict`, and 2PC grain methods from `INeuronHost`/`NeuronHostGrainBase` | M1 | Survey §R03 |
| F3 | Pure Kernel Rules Unification (R02) | Extract 4 pure static rule classes in `DigitalBrain/Kernel/` (`CallPolicy`, `ReactionTable`, `RetentionMath`, `ScheduleDiff`) used by both Orleans and Simulator | M2 | Survey §R02 |
| F4 | Static Call Catalog & Dispatch (R06) | Replace dynamic runtime reflection (`NeuronBinder`, `BoundCall`) with pre-compiled `CallCatalog` and `CallDescriptor` built once at registration | M2 | Survey §R06 |
| F5 | Unified Atomic Write Protocol (R04) | Define `IBrainJournal` port and `Commit` model; unify write pipeline to commit state, signals, and cursor in single atomic transaction | M3 | Survey §R04 |
| F6 | Journal-Backed Subscriptions & Delivery (R05) | Eliminate Orleans streaming and Azure queue `signals-0`; persist subscriptions in journal; deliver via single-flight `Delivery` loop and `[OneWay]` nudge | M3 | Survey §R05 |
| F7 | Unforgeable Caller Context & Access Gate (R07) | Replace 5 ambient `RequestContext` strings with closed `Actor` hierarchy and unforgeable `Caller` struct enforced by host `AccessGate` | M4 | Survey §R07 |
| F8 | StateNeuron Machine Decomposition (R08) | Decompose `StateNeuron` into pure `UnitMachine` (turn transitions, view outcome) and `StoredUnit<TState>`, eliminating collectible ALC leaks | M4 | Survey §R08 |
| F9 | Unit-Host Test Observable Re-Expression | Rewrite 11 unit-host test files that used gate refusal as an observable to assert reactions and state reads instead | M4 | Survey §TestExplorer |
| F10 | Standalone Simulation Package (R14.1) | Extract 42 files (6,094 LOC) from `DigitalBrain.Testing` to standalone, test-runner-free `DigitalBrain.Simulation.csproj` targeting `net11.0` | M5 | Survey §R14 |
| F11 | Twin Silo Facts Elimination & Adapter Smoke (R14.2) | Delete 13 duplicated `*SiloFacts` files (1,913 LOC); introduce 6-fact `SiloSmoke` and `ScenarioDifferential` test kits | M5 | Survey §R14 |
| F12 | Full Build & Test Suite Verification | Verify 100% compilation with 0 warnings/errors and passing of all active tests across all assemblies | M6 | Survey §AC |
| F13 | Forensic Integrity Verification | Binary verification by `teamwork_auditor` confirming authentic implementation without dummy facades or hardcoded bypasses | M6 | Survey §AC |

---

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Ruthless Legacy Elimination & Dead Code Purge | Delete R01 dead fossil types, dead signal log engine, dead client/edge projects, and R03 two-phase gate saga | none | DONE |
| M2 | Pure Kernel Rules & Static Call Catalog | Implement pure kernel rules (`CallPolicy`, `ReactionTable`, `RetentionMath`, `ScheduleDiff`) and static `CallCatalog` (R02, R06) | M1 | DONE |
| M3 | Atomic Journal Write Protocol & Delivery Stream Elimination | Implement `IBrainJournal.Commit` atomic protocol and journal-backed `Delivery` replacing Orleans streams (R04, R05) | M2 | PLANNED |
| M4 | Unforgeable Caller Context & StateNeuron Decomposition | Implement closed `Actor`/`Caller` hierarchy, `AccessGate`, and decompose `StateNeuron` into pure `UnitMachine` (R07, R08, F9) | M3 | PLANNED |
| M5 | Standalone Simulation Package & Twin Test Elimination | Extract `DigitalBrain.Simulation` without xUnit dependency, eliminate 13 twin `*SiloFacts`, and introduce `SiloSmoke` (R14) | M4 | PLANNED |
| M6 | Complete Solution Verification & Forensic Audit | End-to-end `dotnet build` and `dotnet test` validation, independent Reviewer & Challenger verification, and Forensic Integrity Audit | M5 | PLANNED |

---

## Interface Contracts

### 1. `IBrainJournal` Port (Kernel ↔ Storage Adapters)
```csharp
namespace DigitalBrain.Kernel;

public readonly record struct Position(long Value);
public readonly record struct CursorMove(NeuronId Subscriber, long Sequence);

public sealed record Commit(
    NeuronId Neuron,
    long ExpectedVersion,
    ReadOnlyMemory<byte> StateBytes,
    IReadOnlyList<Signal> Signals,
    CursorMove? Cursor);

public sealed record CommitResult(
    long NewVersion,
    Position Position,
    IReadOnlyList<Signal> CommittedSignals);

public interface IBrainJournal
{
    Task<CommitResult> CommitAsync(BrainId brain, Commit commit, CancellationToken ct = default);
    Task<IReadOnlyList<Signal>> ReadSignalsAsync(BrainId brain, Position from, int count, CancellationToken ct = default);
    Task<(long Version, ReadOnlyMemory<byte> StateBytes)> LoadStateAsync(BrainId brain, NeuronId neuron, CancellationToken ct = default);
}
```

### 2. `CallCatalog` (Kernel ↔ Invocation Dispatch)
```csharp
namespace DigitalBrain.Kernel;

public sealed record CallDescriptor(
    string MethodName,
    int Arity,
    Type ReturnType,
    Func<object, object?[], Task<object?>> Invoker);

public sealed class CallCatalog
{
    public static CallCatalog Build(Type contractType);
    public bool TryResolve(string methodName, int arity, out CallDescriptor descriptor);
}
```

### 3. `Caller` & `Actor` (Access ↔ Kernel / Host)
```csharp
namespace DigitalBrain.Access;

public abstract record Actor;
public sealed record UserActor(AccountId Account, PrincipalId Principal) : Actor;
public sealed record AgentActor(AccountId Account, PrincipalId Principal, string AgentId) : Actor;
public sealed record AppActor(AccountId Account, string AppId) : Actor;
public sealed record PlatformActor : Actor;

public readonly record struct Caller(
    BrainId Brain,
    Actor Actor,
    IReadOnlySet<string> Roles);
```

### 4. `UnitMachine` (Units Context)
```csharp
namespace DigitalBrain.Units;

public sealed class UnitMachine<TState>
{
    public static (TState NextState, IReadOnlyList<Signal> EmittedSignals, ViewNode? View) ExecuteTurn(
        TState currentState,
        RuntimeInvocation invocation,
        CallCatalog catalog);
}
```

---

## Code Layout
- `src/DigitalBrain/Kernel/`: Pure domain rules, `IBrainJournal`, `Commit`, `CallCatalog`, `Subscriptions`, `Delivery`.
- `src/DigitalBrain/Access/`: `Actor`, `Caller`, `AccessPolicy`, `AccessGate`.
- `src/DigitalBrain/Units/`: `UnitMachine`, `StoredUnit`, `UnitLoader`.
- `src/DigitalBrain.Simulation/`: Standalone deterministic test OS and harness.
- `src/DigitalBrain.Runtime.Orleans/`: Orleans grain hosting, storage adapters, `NeuronHostGrainBase`, `ISignalNudge`.
