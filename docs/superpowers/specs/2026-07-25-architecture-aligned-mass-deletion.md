# Architecture-aligned mass deletion (2026-07-25)

Direction cut: delete non-product surface, leave a pseudocode-shaped spine. Durable decisions only —
implementation checklists and session progress tables were discarded after the cut landed.

## Non-negotiable: modules ship

**Do not delete module families.** AI, Tasks, Time, Google, Salesforce, and Quickstart are
out-of-the-box product vocabulary. Packages under `modules/` and `samples/DigitalBrain.Quickstart*`
stay in the solution.

Allowed against modules:

- rewrite or stub bad *implementation* (orchestration soup, god-files)
- delete tests that only lock trivia about them
- reorganize folders under the module package

Forbidden:

- removing a module package, contracts package, or AppHost `AddModule` path because "tests don't
  prove it" or "implementation is bad"
- treating an unproven module as trash for deletion of the product surface
- deleting `samples/DigitalBrain.AccountEnrichment` — multi-module behavior example (rewrite thin
  composition; keep the sample)

Authority: `docs/architecture.md`, hosting/testing design 2026-07-24, `CLAUDE.md` oracles.

## Must not return

These surfaces were deleted or collapsed and must not re-enter as public product:

| Surface | Why gone |
| --- | --- |
| `hosts/DigitalBrain.ProbeHost` | Raw `IGrainFactory` HTTP probe; not the author path |
| `src/DigitalBrain.DevTools` | Orleans dashboard / dev journal helper |
| `tests/DigitalBrain.Simulations` | Retired Simulation surface and generated Features leftovers |
| ModuleDriver + thick Gherkin Features | Second test module runtime parallel to product modules |
| Public Simulation/Scenario vocabulary | Superseded by `TestBrain` / `RunningAppHost` |
| AppHost `AddBrain` / storage-profile selection / `WithAzureStorage` | Superseded by `AddDigitalBrain(name)` |
| Public test artifact DTO zoo | Fail via exception message / attachment; diagnostics internal |
| Behaviors / `IBehavior` / calendar `IReminder` product API / central brain neuron | Unbuilt; inventing is failure |

## Public shape (still the target)

```csharp
// AppHost
var brain = builder.AddDigitalBrain("brain");
brain.AddModule<QuickstartModule>();
builder.AddProject<Projects.Silo>("silo").WithReference(brain);

// Silo
silo.AddDigitalBrain();
silo.AddDigitalBrainJournalStorage(config);

// Client — DI only in product path
IDigitalBrain client = ...;
var greeter = client.Get<IGreeter>("welcome");
await client.SendAsync<IGreeter>("welcome", new SayHello("Ada"));

// L1
await using var test = await fixture.CreateBrainAsync(ct);
var g = test.Neuron<IGreeter>();
await test.Client.SendAsync<IGreeter>("welcome", new SayHello("Ada"));
var fact = await g.Outgoing.NextAsync<Greeted>(ct);
await g.RestartHostAsync(ct);

// Time — reminder-primary Countdown only
var c = test.Client.Get<ICountdown>("t1");
await c.Start(new StartCountdown(CommandId.New(), TimeSpan.FromHours(1), dest));
await test.Clock.AdvanceAsync(TimeSpan.FromHours(1), ct);
```

No public Simulation/Scenario/AddBrain/storage profile. No second probe host.
`Connect(IGrainFactory)` may remain for Testing/Aspire DI wiring but is not the author story.

## Specification

`docs/specification.md` is authored markdown listing the retained test tiers. Restore a generator
from durable author-facing scenarios only when those scenarios exist again as product vocabulary.
