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

## Salesforce automation via LLM self-evolution (recommended)

Use the describe tool or Ino to create:

create_automation_from_description description="when poll new leads from Salesforce then emit LeadCreated with name and email"

The LLM (Foundry with local Qwen) identifies the intent, generates script using available caps (e.g. Http for Salesforce REST or Llm for processing), + RegisterReaction with poll trigger, stages proposal for approval.

Example generated script (LLM will adapt):

```csharp
// Poll new leads (use with 'poll' in When)
var leadsJson = await Caps.HttpGetAsync("https://<instance>.salesforce.com/services/data/v60.0/query?q=SELECT+Id,Name,Email+FROM+Lead+WHERE+CreatedDate=TODAY");
 // parse and emit
return new[] { new Signal("LeadCreated", new Dictionary<string,object?> { ["data"] = leadsJson }) };
```

Register and approve via the rail. The system self-evolves the automation.

For better, the LLM can use vector search on DigitalBrain knowledge for exact Salesforce patterns if wired.

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
