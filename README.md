# DigitalBrain

Personal “alive OS”: Orleans **neurons** (durable grains) exchange **synapses** (facts) over a live **synapse graph** the owner and assistant rewrite at runtime.

![Architecture](plans/Architecture.svg)

**Kernel** — single-threaded turns, journal-is-outbox, emit (graph-routed) vs send (directed).  
**Modules** — AI, Execution, UI, MCP SaaS (Salesforce/Gmail), Memory, Time, …  
**Run** — `dotnet run --project src/Kernel/DigitalBrain.AppHost`
