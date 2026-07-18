# The required minimum & the build order

## What "minimum" means here

The smallest substrate on which **one neuron broadcasts a synapse, another handles it and replies, and a Simulation proves it** — on a real Orleans silo, with the wiring scannable from metadata. Nothing for marketplace, UI polish, LLMs, persistence, or federation.

## The seed (this folder)

```
v2/
├─ docs/                          00..04 — the design (you are here)
├─ Directory.Build.targets        shields v2 from the repo-root production-package guard
├─ DigitalBrain.V2.slnx
└─ src/
   ├─ DigitalBrain.V2.Core/       the two primitives + brain (pure substrate, no test deps)
   │   ├─ Synapses/Synapse.cs, RoutingMode.cs, NeuronId.cs
   │   ├─ Runtime/INeuron.cs (INeuron + IHandle<T> + IEmit<T>), Neuron.cs, SynapseStream.cs
   │   └─ Brain/IDigitalBrain.cs, Brain.cs
   ├─ DigitalBrain.V2.Testing/    Substrate (localhost silo + in-memory streams) + Simulation base
   ├─ DigitalBrain.V2.Catalog/    reflection catalog neuron + IEmit/IHandle edge graph
   ├─ DigitalBrain.V2.Ino/        .ino parser, transpiler, and Roslyn compile gate
   ├─ DigitalBrain.V2.Creator/    Architect -> Implementer -> Gate closed loop
   ├─ capsules/Ping/
       ├─ Ping.Contracts/         IPingNeuron (: IHandle<Ping>, IEmit<Pong>), Ping, Pong
       ├─ Ping/                   PingNeuron (+ Ping.ino)
       └─ Ping.Simulations/       PingSimulation
   └─ capsules/Greeter/
       ├─ Greeter.Contracts/      Greeter/Room/Bystander contracts + Hello/Announce synapses
       ├─ Greeter/                Greeter asks Room point-to-point; Room emits Announced
       └─ Greeter.Simulations/    Ask/Reply point-to-point proof + negative broadcast assertion
```

> **Placement note.** The `Simulation` base lives in `DigitalBrain.V2.Testing`, not Core. It
> derives from xUnit's `IAsyncLifetime` and hosts a silo — keeping that out of Core leaves the
> substrate free of test/server dependencies. Core stays the two primitives + the brain.

## Dependency budget (the *only* packages)

| Package | Why it is required |
|---|---|
| `Microsoft.Orleans.Sdk` / `.Server` | a neuron is a grain — non-negotiable |
| `Microsoft.Orleans.Streaming` (+ in-memory provider) | the broadcast timeline |
| `Microsoft.Orleans.Serialization` | synapses cross grain calls |
| `Microsoft.CodeAnalysis.CSharp` | compile generated `.ino` artifacts in the gate |
| `xUnit` | run simulations |

Explicitly **not** pulled: journaling/persistence, reminders, Aspire hosting, Postgres, Stripe, Extensions.AI. Each must justify itself against a neuron that needs it.

## Build order (each step independently runnable, green before the next)

1. **Synapse + RoutingMode + metadata.** Stamping (correlation/causation) + serialization. *Proof: a unit test round-trips a synapse.*
2. **Neuron base.** Receive (timeline subscribe + `DeliverAsync`), dispatch `IHandle<T>`, fire (`Emit`/`Ask`/`Reply`), cycle guard. *Proof: a neuron handles a directly-delivered synapse.*
3. **Brain neuron + `IDigitalBrain.Fire`.** Inject a synapse from outside; broadcast routes via timeline, ask routes direct. *Proof: `Fire(broadcast)` reaches a subscribed neuron.*
4. **Simulation base.** `Fire` + `Expect` over the live silo + `SubstrateHostBuilder`. *Proof: an empty simulation boots a silo and tears down.*
5. **Ping capsule.** `Ping.Contracts` (interface with `IHandle<Ping>`+`IEmit<Pong>`), `PingNeuron`, `Ping.ino`, `PingSimulation`. *Proof: `Ping_is_echoed_as_broadcast_pong` is green.*
6. **Catalog scan.** Walk Contracts assemblies, build the `IEmit`→`IHandle` edge graph. *Proof: the graph shows `PingNeuron --Pong--> (subscribers)`.*

Steps 1–5 were the minimum vertical slice. Step 6 unlocked the UI graph and the closed-loop Architect; Slices A-D are now green on top of it.

## Completed follow-up slices

1. **Slice A: point-to-point Ask/Reply proof.** The Greeter capsule broadcasts `Hello`, Greeter asks Room directly with `Announce`, Room emits `Announced`, and the Simulation asserts a non-subscriber never receives the broadcast.
2. **Slice B: catalog constellation.** `CatalogNeuron` handles `DescribeConstellation`, reflects Contracts interfaces for `IHandle<T>`/`IEmit<T>`, records synapse fields, emits `ConstellationDescribed`, and the Simulation prints/asserts the Ping and Greeter graph.
3. **Slice C: `.ino` transpiler.** `DigitalBrain.V2.Ino` parses `Ping.ino`, emits v2 C# neuron + Simulation artifacts, compiles them with Roslyn, loads the generated assembly, and runs the generated scenario on the live substrate.
4. **Slice D: closed loop.** Architect reads the catalog, Implementer authors `.ino`, Gate transpiles/compiles in a collectible `AssemblyLoadContext`, runs the authored scenario as a Simulation, and emits `NeuronActivated` when green.

## Deferred, in priority order (add back when a neuron needs it)

1. Journaled state (swap in `Orleans.Journaling` behind the same `Neuron.State` facet).
2. RFW UI from the `ui:` block + catalog.
3. Distribution (install a signed capsule, verify, gate green) — and nothing more of the marketplace.

## Unions (C# 15 / .NET 11 preview) — where they fit {#unions}

- ✅ Use for **loop/result outcomes** (`union CompileResult(Compiled, CompileErrors)`, `union GateOutcome(Passed, Failed)`) — kills the `bool Success + List<string>` pattern with exhaustive `switch`. Used by the Creator gate behind `<LangVersion>preview</LangVersion>`.
- ❌ Do **not** union the `Synapse` hierarchy — it is open (capsules add types at runtime) and unions are closed/compile-time.
- ⚠️ Do **not** put unions on serialized grain contracts yet (boxing + unproven Orleans serializer support in preview). Keep them to in-process control flow.

## Definition of done for the seed

`dotnet test v2` is green across Ping, Greeter, Catalog, Ino, and Creator simulations. The catalog scan prints the constellation, generated-from-`.ino` code passes its own scenario, and the closed loop authors, gates, and activates a neuron end-to-end.
