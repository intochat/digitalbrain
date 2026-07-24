# DigitalBrain Hosting and Testing Program

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement the linked plans task-by-task. Steps
> use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the approved compiled-module, one-call hosting, production-aligned testing,
external module authoring, and durable Countdown architecture as four green, reviewable stages.

**Architecture:** The approved design is implemented from the inside out. Compiled module capsules
and `AddDigitalBrain` establish one composition seam. L1 Testing consumes that seam without exposing
Orleans. L2 Testing wraps Aspire without exposing Aspire. Quickstart and Time then prove that an
external author can use the same Contracts/runtime/AppHost/test shape as every shipped module.

**Tech Stack:** .NET 10, Orleans 10.2.2-rc.2, Aspire 13.4.6, xUnit v3, Roslyn incremental source
generation, Azure Storage/Azurite, Reqnroll 3.3.4.

## Authority

- Approved design:
  `docs/superpowers/specs/2026-07-24-digitalbrain-hosting-and-testing-design.md`
- This index is the only live program order for that design.
- The four linked plans contain the executable file maps, red-green steps, commands, expected
  results, and commit boundaries.
- Work only in the current branch and preserve unrelated user changes.
- Complete plans in order. Do not parallelize stages that edit the same composition or Testing
  surface.
- A stage is complete only when its own completion gate passes and its changes are committed.

## Execution order

### Stage 1 — Compiled modules and one-call brain hosting

Plan:
`docs/superpowers/plans/2026-07-24-compiled-modules-and-brain-hosting.md`

Produces:

- semantic neuron method names without the redundant `Async` suffix;
- `[Alias(nameof(Method))]` for semantic neuron methods;
- generated fully qualified neuron interface aliases;
- generated typed module capsules and a typed compiled catalog;
- `AddDigitalBrain(name)` as the sole public brain-infrastructure call;
- typed module hosting extensions with no `ConditionalWeakTable` state recovery;
- deletion of `AddBrain`, public storage profiles, and reflection/string module activation.

Exit condition: all current hosts select modules through the generated capsule seam and obtain the
complete durable brain profile from one `AddDigitalBrain(name)` call.

### Stage 2 — L1 DigitalBrain Testing product

Plan:
`docs/superpowers/plans/2026-07-24-digitalbrain-testing-l1.md`

Consumes: Stage 1 generated capsules.

Produces:

- assembly-owned `DigitalBrainFixture`;
- one serialized method-scoped `TestBrain`;
- real `IDigitalBrain` access through `TestBrain.Client`;
- logical `TestOwner` and closed `TestNeuron<TNeuron>`;
- committed-journal observation as the synchronization authority;
- deterministic `TimeProvider` timers and reminder delivery;
- closed fault/restart controls and bounded failure artifacts;
- typed external-edge controls and thin generated Gherkin vocabulary;
- deletion of Simulation/Scenario terminology and process-global test state.

Exit condition: retained module semantic and durability proofs run through public Testing APIs
without Orleans, Aspire, raw DI, or wall-clock polling in their test source.

### Stage 3 — L2 AppHost Testing product

Plan:
`docs/superpowers/plans/2026-07-24-digitalbrain-testing-l2.md`

Consumes: Stage 1 hosting and the Stage 2 Testing package.

Produces:

- exclusive `DigitalBrainAppHostFixture<TAppHost>`;
- method-scoped `RunningAppHost`;
- one-name `HostedResource` handles;
- bounded Aspire resource/log evidence;
- graph-owned terminal cleanup;
- deletion of `HostedApplication`, `HostedScenario`, and raw Aspire test access.

Exit condition: host composition, endpoint, and process-restart proofs use only the closed L2
surface and leave every Aspire graph terminal after disposal.

### Stage 4 — External author proof and durable Time

Plan:
`docs/superpowers/plans/2026-07-24-quickstart-time-and-module-proof.md`

Consumes: Stages 1–3.

Produces:

- split Quickstart Contracts/runtime packages;
- Quickstart L1 and one-call AppHost proofs;
- composed kernel outbox wake-up with no reminder inheritance tax on module neurons;
- the settled durable one-shot `ICountdown` Time capability;
- deterministic Countdown race, restart, and recovery proofs;
- one enforced package/hosting/testing matrix for Quickstart, AI, Tasks, Time, Google, and
  Salesforce.

Exit condition: Quickstart demonstrates the complete external module-author path and Time ships
only the approved `ICountdown` boundary; recurring/calendar scheduling remains explicitly unbuilt.

## Approved design coverage

| Design sections | Owning stage |
|---|---|
| §2–§5 root decision, neuron aliases, one-call hosting, compiled capsules | Stage 1 |
| §6–§13 testing tiers, L1 lifecycle, client/control split, owners, neurons, observations, time, faults, edges | Stage 2 |
| §14 and L2 parts of §16 AppHost testing and evidence | Stage 3 |
| §15 Gherkin | Stage 2 |
| §16 L1 evidence | Stage 2 |
| §17 required deletion | Stages 1–4 at the boundary that removes each obsolete path |
| §18 first external proof | Stage 4 |
| §19 non-goals | Every stage |
| §20 acceptance | All stage completion gates plus the program gate below |

## Program completion gate

After all four stage commits, run:

```powershell
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build
git status --short
```

Expected:

- every command passes;
- only unrelated user-owned changes remain unstaged;
- no obsolete hosting, Simulation/Scenario, manual Quickstart host, reflection module catalog, or
  neuron reminder-inheritance path remains live;
- the public surfaces and package boundaries match the approved design spec;
- the architecture documentation distinguishes built `ICountdown` from unbuilt Behavior,
  `IReminder`, recurring, and calendar capabilities.
