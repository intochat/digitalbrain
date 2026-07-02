# CapabilityGate Hardening — Tracked Follow-Up

**Status:** tracked, not scheduled

This is a short, durable record of known, deliberately-deferred security/cost gaps surfaced while
building `spec/neuron-pack-testing-architecture`. None of these are fixed by that branch; they are
recorded here so they don't get silently forgotten. No implementation work is proposed by this doc —
it is a pointer for a future dedicated hardening pass.

## 1. Reflection/Activator bypass of `CapabilityGate`'s allowlist

`CapabilityGate` (`DigitalBrain.Kernel/Foundry/CapabilityGate.cs`) allows the full `System.` namespace
minus 6 explicit exclusions. `System.Type.GetType(...)` + `System.Activator.CreateInstance(...)` (also
`System.Reflection.Assembly.Load`) are themselves inside that broad allowance and are not excluded, so a
pack can reflectively construct/invoke any of the explicitly-banned APIs (`System.Net`, `System.IO`,
`System.Diagnostics.Process`, ...) via a string-keyed type name with zero statically-resolvable symbol
reference for the Roslyn walker to catch. This is real and confirmed (empirically verified during Task 6
of the plan that produced this branch), not theoretical. It matters because `CapabilityGate` is the only
compile-time safety net for Tier-1 (`CodeRunNeuron`) and the second-tier check for
`Sandbox/OutOfProcessSandbox.cs`; today it is a guardrail against accidental misuse, not a boundary that
holds against adversarial pack code. Rough shape of a fix: an AST-level ban on `System.Reflection.*`,
`System.Activator`, and `Type.GetType` regardless of the general `System.` allowance, or moving to a
stricter allowlist model (enumerate safe leaf APIs instead of allowing a namespace minus exclusions).

## 2. `CodeDeployNeuron` / `ProcessBuildRunner.VerifyBuildAsync` has no `CapabilityGate` check

`CodeDeployNeuron`'s verify-build path (`ProcessBuildRunner.VerifyBuildAsync`) does a plain `dotnet build`
of a temp project referencing the whole Kernel, then on success commits source into `Generated/` and
triggers an Aspire silo restart — `CapabilityGate.FindViolations` never runs against that source at all.
This was found and documented in `Foundry/README.md` during this branch's Task 7, but no tracked
follow-up task existed to actually close it until now. It matters more than the Tier-1 gap above: there is
no ALC boundary here at all, only verify-build-plus-checkpoint/rollback, and a passing gap here commits
arbitrary source straight into the live kernel. Rough shape of a fix: run `CapabilityGate.FindViolations`
against the deploy source before `ProcessBuildRunner.VerifyBuildAsync` starts the build, rejecting on any
violation the same way Tier-1 does.

## 3. Broadcast durable-write amplification

Every timeline-subscribed neuron now durably journals every broadcast it observes, whether or not it has
a handler for that synapse type (`Neuron.RecordBroadcastReceivedAsync` runs unconditionally in
`OnNextAsync`, ahead of the handled-type filter). This is correct for the current single-in-memory-journal
prototype, but against the production Azure Blob journal, under real broadcast volume and real subscriber
counts, it means N subscriber writes per broadcast regardless of relevance — a real write-amplification
and cost concern that is currently untested and unmeasured. Rough shape of a fix: benchmark actual
Azure Blob journal write cost under representative subscriber counts/broadcast rates before deciding
whether to journal only handled broadcasts, batch writes, or introduce a cheaper "observed but unhandled"
record shape.
