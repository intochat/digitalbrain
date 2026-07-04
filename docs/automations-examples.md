# Automations Examples (real C# + reactions)

Lightweight reactive automations live in the AutomationNeuron. Register via synapses, MCP, or Ino. Scripts are **real** C#.

## Quick via MCP (stdio)

```text
define_reaction id=hello-activate when=NeuronActivated target=personal-assistant script='return new[] { new ListSurface("Auto", new[] {"hello from reaction"}) };'

create_automation_from_description description="when Signal:Ping then emit Pong" 

list_automations
```

## Seeds (active on start)

See DigitalBrain.Kernel/Program.cs ApplicationStarted:
- auto-brief-on-activation: emits ListSurface Ui on any NeuronActivated
- signal-context-reactor: reacts to DailyBriefRequested Signal
- shared script id used by two reactions (brief-*-activate)
- scoped demo (user scope)

## Script bodies that work (return or side effect)

```csharp
// return
return new[] { new Signal("X", null) };

// side + return
await Fire(new Signal("Y", new Dictionary<string,object?> { ["k"] = 1 }));
return Array.Empty<Synapse>();

// Ui surface
return new[] { new ListSurface("Title", new[] { "item" }) };

// inline prefix also supported
inline: return new[] { new Signal("Z", null) };
```

Globals: Synapse input, NeuronId Self, Func<Synapse,Task> Fire

## Surfaces

- AutomationSurface: reactions + scripts + counts (emitted on change/query)
- AutomationGraphSurface: nodes/edges for visual (data only)
- ListSurface fallbacks for compat

GetScriptCodeAsync / List* documented on IAutomationNeuron.

## Promotion

promote_automations_to_pack packName=foo version=0.1 reactionIdsCsv=brief-on-pa-activate

Yields AutomationPromoted + stub signal for pack pipeline.

## Scoping

Scope="default" global; else e.g. "user-123" only matches activations/signals for that user (see NeuronScope).

All changes keep backward compat.
