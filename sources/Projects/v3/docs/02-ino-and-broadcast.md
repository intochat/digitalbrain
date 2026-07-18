# `.ino` files & the Broadcast question

## The Broadcast question, answered

> *"on some action neuron broadcasts some synapses... or should it be `Broadcast : Synapse`? idk."*

**Decision: routing is a property of the *act of firing*, carried in synapse metadata — NOT a synapse subtype. There is no `Broadcast : Synapse`.**

### Why not `Broadcast : Synapse`

A subtype bakes routing into the type system, but routing is orthogonal to payload:

- The same `Pong` may be **broadcast** to the timeline in one flow and **replied** point-to-point to one caller in another. Same data, two routings. A `Broadcast`/`Directed` type split would force two payload types for one fact, or duplicate every synapse.
- Routing answers *"who hears this?"*, which is a delivery concern. Payload answers *"what happened?"*. Keep them separate or they corrupt each other.

So routing is one enum on the header:

```csharp
enum RoutingMode { Broadcast, PointToPoint }
// lives in SynapseMetadata.Routing
```

### How a neuron expresses it — three verbs, one underlying action

| Intent | `.ino` verb | Routing set | Delivery |
|---|---|---|---|
| announce a fact to whoever cares | `emit Foo(...)` | `Broadcast` | timeline → all `IHandle<Foo>` neurons |
| request one specific neuron | `ask Target to Bar(...)` | `PointToPoint` | direct to `Target` |
| answer the neuron that called me | `reply Baz(...)` | `PointToPoint` | direct to incoming `Caller` |

All three compile to one private `Fire(synapse, routing)`. `emit` is broadcast; `ask`/`reply` are point-to-point. That is the entire routing model.

### The static emit-edge (so the graph knows *before* running)

The runtime `emit` is matched by a compile-time declaration so the constellation graph and the closed loop can see "this neuron broadcasts `Foo`" **without executing the handler**:

- **Software 1.0:** `IEmit<Foo>` on the Contracts interface.
- **Software 2.0:** a `broadcasts` line in the `.ino` header.

Optional refinement: split `IEmit<T>` (directed) vs `IBroadcast<T>` (timeline) if you want the graph to color edges by routing. For the minimum, one `IEmit<T>` marker is enough; the routing mode is a runtime header.

## The `.ino` file — one self-contained Software 2.0 document

An `.ino` file is the whole capsule in one notation. Each section lowers to a Software 1.0 artifact:

```ino
neuron Ping.Echo                              # ① identity + doc
  "Answers every Ping with a Pong and announces it to the room."

  using ping  = synapse(Ping.Ping)            # ② wiring → IHandle<>/IEmit<> + catalog edges
  using pong  = synapse(Ping.Pong)
  using room  = neuron(Ping.Room)

  broadcasts pong                             #    explicit emit-edge (→ IEmit<Pong>)
  handles    ping                             #    explicit in-edge   (→ IHandle<Ping>)

  @telemetry:counter:pings_handled            # ③ telemetry → OTel counter
  state lastSeen: text                        #    state     → journaled field

  on ping:                                    # ④ behavior → IHandle<Ping>.HandleAsync body
    count pings_handled
    set lastSeen = ping.from
    emit pong(to: ping.from)                  #    broadcast  (RoutingMode.Broadcast)
    # ask room to announce(who: ping.from)    #    point-to-point alternative

  ui:                                         # ⑤ ui → RFW widget (see below)
    Card(title: "Echo", body: lastSeen)

scenario "a ping is echoed as a broadcast pong"   # ⑥ simulation → a Simulation neuron (the gate)
  when  emit ping(from: "alice")
  then  broadcast pong observed with to == "alice"
  and   counter pings_handled == 1
```

### Section → artifact mapping

| `.ino` section | Lowers to |
|---|---|
| `neuron X "doc"` | `class X : Neuron, IX` + identity |
| `using` / `broadcasts` / `handles` | `IHandle<>` / `IEmit<>` on the `IX` interface + catalog entry |
| `@telemetry` | OTel counter/histogram registration |
| `state` | journaled state field |
| `on S:` with `emit`/`ask`/`reply` | `HandleAsync(S)` body |
| `ui:` | `.rfwtxt` widget served via `INeuronMetadata.UiLayoutJson` |
| `scenario` | a `Simulation` subclass run by the live silo ([03](03-simulations.md)) |

**Software 1.0 and 2.0 are the same shape in two notations.** A C# capsule and an `.ino` capsule are interchangeable peers; the transpiler is just a lowering.

## UI is neurons {#ui-is-neurons}

A widget is a neuron whose `ui:` block compiles to a Remote Flutter Widgets (`.rfwtxt`) tree with two bindings:

- `data.lastSeen` → **state binding**: the widget reads the neuron's state snapshot.
- `Button(onTap: event "ping" { from: "me" })` → **event = synapse**: a tap *fires a synapse* back into the substrate.

That closes the loop visually with no bespoke per-widget code:

```
neuron state ─► data.* binding ─► RFW renders ─► user taps ─► event fires synapse ─► on handler ─► state changes ─► …
```

The widget catalog (fqn · kind · fields · layout) is the **same** metadata scanned from Contracts in [01](01-substrate.md). The UI draws the constellation and the cards straight from it. Polish (glass, comets) is deferred; the *model* is this.
