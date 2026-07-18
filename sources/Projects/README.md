# DigitalBrain Projects Workspace

Multiple iterations of the same vision (agent OS from neurons + synapses, marketplace for experiences/knowledge sharing).

## Canonical Clean Version
See [final/](/final) — fresh Aspire + clean core reboot.
- Start here for new work.
- Contains: quick LLM onboarding README, mcps/ for Grok (Context7 + aspire + reqnroll etc), docs/ with full comparison + vision + design.
- "Build step by step": core primitives + sims + Reqnroll BDD (incl dynamic handlers after bundle) first.

## Other Versions (archaeology only — do not extend)
See git history and subfolders for prior iterations. Final/ is the current clean reboot. Mine patterns from them when needed; do not cargo-cult old process.

## Common Vision (all versions)
- Only neurons (actors) and synapses (messages).
- Broadcast on timeline + p2p.
- Wiring via IHandle<T>/IEmit<T> on contracts interfaces (scannable).
- Simulations + .ino/.feature as readable executable specs/gates (human + LLM).
- Marketplace bundles install new handlers (dynamic: 1 -> 2+ on same broadcast/system events).
- IDigitalBrain can control Aspire (run apphost, restart resources).
- E2E covers generated UI.
- Aspire + Orleans substrate.
- Knowledge sharing system via installable experiences.

## For LLMs / Agents Working on This
- Primary: cd final
- Core Law (neurons + synapses only) + the dynamic handler growth proof (DistributionDynamicHandlers.feature) are non-negotiable.
- Latest packages, no boilerplate summaries, self-explanatory names.
- Relevant tests for changes. Fast path: dotnet run start.cs (REPL client) + targeted tests.
- Use Reqnroll/Simulation only for the distribution contract (install grows handlers + reaction).
- Pure: neurons + synapses.

Pick best patterns from history when needed (final is the current canonical).

## Quick Start (final)
```pwsh
cd final
dotnet run start.cs     # fast REPL client to brain (preferred for most work)
# or: aspire run        # when changing hosting / resources
```

See final/README.md for current status + commands. Old docs/ for deep history.
