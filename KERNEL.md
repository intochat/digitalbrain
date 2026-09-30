# Kernel Redesign — spec + plan

Ratified 2026-09-30 (owner). Working doc; delete when implemented. Naming: **Option 3 — Kernel is
the place (the privileged ring of projects), Core is the thing (the neuron runtime project).**

## Spec

1. **Brain comes home (A).** `BrainNeuron` + `BrainState` move from Identity into the Core
   runtime's `Brain/` folder. GrainType `"brain"` and alias `brain.state` unchanged — no data
   migration. Identity keeps `IIdentityDirectory` and keeps calling `IBrain.Establish`. Future
   brain growth (Fork, BrainCreated/BrainShared) lands in Core.
2. **Enforcement completes (E).** `DeveloperMode` moves from `Composition/` to `Enforcement/`.
   The privileged ring reads: stamper, `BrainScope`, `BrainRoutes`, `BrainAccessFilter`,
   `BrainAccess`, `DeveloperMode` — one folder, namespace `DigitalBrain.Core.Enforcement`.
3. **Names sync (C, Option 3).** Projects rename; namespaces do NOT change (merge-cheap):
   - `DigitalBrain` → `DigitalBrain.Core` (namespace already `DigitalBrain.Core`)
   - `DigitalBrain.Aspire` → `DigitalBrain.Kernel.Aspire`
   - `DigitalBrain.Aspire.Hosting` → `DigitalBrain.Kernel.Aspire.Hosting`
   - `DigitalBrain.Runtime.Tests.Unit/E2E` → `DigitalBrain.Core.Tests.Unit/E2E`
   The word "Runtime" as a kernel name dies.
4. **Entry points disambiguate (D).** `AddDigitalBrain(IHostApplicationBuilder)` →
   `AddDigitalBrainRuntime`. AppHost `AddDigitalBrain(IDistributedApplicationBuilder)` and
   `AddDigitalBrainClient` keep their names.
5. **Sdk splits by audience (B) — PHASE 2, blocked on the master merge (HYGIENE.md §2).**
   New `DigitalBrain.Platform` project (platform-only ring; never a script contracts assembly —
   `ModuleInventory`/`ScriptContracts` exclude it by assembly identity; `[PlatformOnly]` remains
   as defense-in-depth): Secrets impl, Integrations registration/accounts/capability impls,
   `SecretsModule`, `IntegrationsModule`, and the `IIntegrationRegistration` contract itself.
   `DigitalBrain.Sdk` keeps only module/script-visible contracts: `IntegrationDefinition`,
   `Capability`, `ICapabilitySource`, account contracts/records, `Auth/`, `HttpSurfaces/`,
   `Webhooks/`. Phase 2 gets its own plan after the merge.

Target tree: see the conversation record / HYGIENE.md §5; the essence is
Contracts (serialized truth) / Core (runtime + Brain + Enforcement) / Platform (credential ring)
/ Sdk (module+script contracts) / Client / Kernel.Aspire pair / Deployment.

## Plan — Phase 1 (safe before the master merge)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development or
> superpowers:executing-plans. Global constraints: persisted aliases/GrainType/[Id] never change;
> no boilerplate ///summary; suites per project, never .slnx; known failure set is exactly 1
> (HostedDeployment TelemetryCollector).

### Task 1: Brain comes home + enforcement tidy

**Files:** Move `src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity/BrainNeuron.cs`
→ `src/Modules/DigitalBrain/Kernel/DigitalBrain/Brain/BrainNeuron.cs` + `BrainState.cs` (split:
one type per file; namespace `DigitalBrain.Core`; internal→public only if Identity's grain
registration needs it — Orleans discovers grains per assembly, so the KERNEL silo registration
must now pick it up: verify how grains register (assembly scanning in UseOrleans) and that the
kernel assembly's grains are scanned; BrainFacts move from Identity tests to Core tests).
Move `Composition/DeveloperMode.cs` → `Enforcement/DeveloperMode.cs` (same namespace).

- [ ] Step 1: move files; `GrainType("brain")`, alias `brain.state`, `[Id]`s byte-identical.
- [ ] Step 2: Identity compiles without the types (it references Contracts' `IBrain` only);
      `EstablishBrainAsync` call sites unchanged.
- [ ] Step 3: move `BrainFacts` to Core tests; run Core + Identity suites → green.
- [ ] Step 4: commit.

### Task 2: Project renames (no namespace changes)

**Files:** rename 5 project folders/csproj per spec §3; fix every `<ProjectReference>`, the
`.slnx`, Dockerfile/docker-compose if they name the assemblies, `InternalsVisibleTo` attributes
naming old assembly names (grep `"DigitalBrain.Aspire"` and `"DigitalBrain.Runtime.Tests"`),
and the module-hosting convention resolver if it special-cases kernel assembly names (it should
not — verify). AssemblyName changes: grep for assembly-qualified strings.

- [ ] Step 1: rename + reference fix; build everything that referenced the five.
- [ ] Step 2: run Core tests (new name), Identity, one module suite (CSharp), IntoChat Unit.
- [ ] Step 3: `aspire run` → all Healthy (the AppHost references renamed projects).
- [ ] Step 4: commit.

### Task 3: `AddDigitalBrainRuntime` + CLAUDE.md

- [ ] Step 1: rename the `IHostApplicationBuilder` overload in Kernel.Aspire; fix call sites
      (Program.cs, ModuleHostProgram, test hosts).
- [ ] Step 2: CLAUDE.md "Working in this repo" gains one line: Kernel = the privileged ring of
      projects under `Kernel/`; Core = the neuron runtime; Platform (phase 2) = the credential
      ring scripts can never see.
- [ ] Step 3: run IntoChat Unit + Core suites; commit.
