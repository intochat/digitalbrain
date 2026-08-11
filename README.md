# DigitalBrain

Personal ΓÇ£alive OSΓÇ¥: Orleans **neurons** (durable grains) exchange **synapses** (facts) over a live **synapse graph** the owner and assistant rewrite at runtime.

![Architecture](plans/Architecture.svg)

**Kernel** ΓÇö single-threaded turns, journal-is-outbox, emit (graph-routed) vs send (directed).  
**Modules** ΓÇö AI, Execution, UI, MCP SaaS (Salesforce/Gmail), Memory, Time, ΓÇª  
**Run** ΓÇö `dotnet run --project src/Kernel/DigitalBrain.AppHost`
