# Code Foundry

Runtime code-generation & hot-load pipeline. Lets the self-improving loop generate,
compile, run, and durably load real C# at runtime.

## Tiers
- **Tier 1 (Run):** `CodeRunNeuron` → `InProcessAlcExecutor`. Roslyn compile in memory,
  run in a collectible `AssemblyLoadContext`, unload. No restart. For logic/experiences.
- **Tier 2 (Deploy):** `CodeDeployNeuron`. Verify-build to a temp project (Orleans codegen
  runs, silo untouched) → on success commit source to `Generated/` → request Aspire silo
  restart → Orleans auto-registers the new grain types. Journals (Redis) survive.

## Entry points
- MCP: `run_code_foundry(spec, tier, autoApply)`
- Synapse: fire `FoundryRequest` at grain `foundry-main` (`ICodeFoundryLoopNeuron`).

## Safety
- Checkpoint before apply; capability gate (`CapabilityGate`) bans dangerous symbols at
  compile time; Tier-2 restarts only after a passing verify-build; failures roll back.
- The in-process `AssemblyLoadContext` is a guardrail, NOT a security sandbox (.NET has no
  CAS). `Sandbox/OutOfProcessSandbox.cs` (`ISandboxedExecutor`, `SandboxTier.OutOfProcess`)
  is the built hardening over it: it runs `CapabilityGate.FindViolations` on the compilation
  *before* ever emitting it (`OutOfProcessSandbox.cs:28` — same gate as Tier-1, applied a
  second time as defense-in-depth), then launches the emitted binary as a separate `dotnet`
  child process, giving real OS-process isolation on top of the allowlist. It is registered
  in DI (`FoundryServices.cs`) but nothing currently calls it from either neuron's dispatch
  path — it exists as a hardened primitive, not yet wired in as the executor behind
  `CodeRunNeuron`/`CodeDeployNeuron`. A WASM tier (Wasmtime) remains the next, not-yet-built
  step beyond it.
- **Naming note, to avoid the confusion this file previously invited:** "Tier 1/Tier 2"
  above names the *Run vs. Deploy* neurons (`CodeRunNeuron`/`CodeDeployNeuron`). That is a
  different axis from the `SandboxTier` enum (`InProcessGated`/`OutOfProcess`/`Wasm`), which
  ranks *isolation strength* for `ISandboxedExecutor` implementations. `OutOfProcessSandbox`
  is not "Tier 2" in the first sense; it is gated and OS-isolated, and unrelated to the
  Tier-2 deploy path below.
- **Tier-2 (Deploy) gate gap — still real:** `CapabilityGate` does not run in
  `CodeDeployNeuron`'s verify-build path (`ProcessBuildRunner.VerifyBuildAsync`, which does a
  plain `dotnet build` of a temp project referencing the whole Kernel). That path's safety
  net is verify-build plus checkpoint/rollback, not the gate; wiring `CapabilityGate` into
  Tier-2 deploy source remains planned hardening.
