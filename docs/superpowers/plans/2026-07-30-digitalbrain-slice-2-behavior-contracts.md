# Slice 2: Behavior Files, BDD, and Input Unions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make each behavior revision a signed `Behavior.cs` single-file app plus authoritative `Behavior.feature`, with one logical input contract, preview-union lowering, stable case identities, and executable BDD admission evidence.

**Architecture:** Preview C# is isolated to the pinned behavior compiler/worker toolchain. The CLR union is authoring syntax only; the manifest persists stable contract/case IDs and canonical `oneOf` schemas. Every Gherkin scenario has one executable binding and one result. The friendly overview is derived from the accepted scenarios.

**Tech Stack:** Roslyn C# 15 preview, canonical zip/JSON artifacts, xUnit v3, Gherkin parser already used by the behavior rail, out-of-process host seam.

## Global Constraints

- Do not enable preview repository-wide or change kernel `Synapse`.
- Do not load behavior assemblies in the silo.
- Do not create `.ino`, a project tree, a package manager, or editable generated manifests.
- Do not edit the client/capability catalog files owned by Slice 1.

---

## Task 1: Make the two authored files canonical

**Files:**
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/Manifest/BehaviorDefinitionManifest.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/Manifest/BehaviorEntryPoints.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors/Manifest/BehaviorContractManifest.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors/Manifest/BehaviorScenarioManifest.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/Artifacts/CanonicalArtifactWriter.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/Artifacts/CanonicalArtifactReader.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Tests/CanonicalArtifacts.Writer.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Tests/CanonicalArtifacts.Reader.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Tests/CanonicalArtifacts.Security.cs`

- [ ] Add failing tests proving exactly `Behavior.cs` and `Behavior.feature` are the editable authored files.
- [ ] Assert overview, schemas, assembly, evidence, and signature remain generated internals.
- [ ] Assert the artifact records SDK/Roslyn version and deterministic compiler policy.
- [ ] Assert path traversal, duplicate authored files, alternate casing, and secret-like generated content are rejected.
- [ ] Run:

```powershell
dotnet test src/core/behaviors/DigitalBrain.Behaviors.Tests -c Release --filter "CanonicalArtifacts"
```

Expected RED.

- [ ] Update canonical writer/reader and manifest without breaking content-addressed signing.
- [ ] Re-run focused tests.
- [ ] Commit: `feat: define canonical behavior source files`

## Task 2: Lower one root input union to a stable manifest

**Files:**
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/ContractOnlyBehaviorCompiler.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/BehaviorInputContractCompiler.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/BehaviorContractCompatibility.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/Rail/IBehaviorCompiler.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Tests/CompilerSmoke.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors.Tests/InputUnionCompilation.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors.Tests/InputContractCompatibility.cs`

- [ ] Use Context7 and Microsoft Learn through the configured MCPs to confirm the exact C# 15 preview union syntax and Roslyn symbol shape for the pinned SDK.
- [ ] Add failing tests for a root union containing one reusable module synapse and one behavior-owned record.
- [ ] Assert canonical `oneOf`, stable `BehaviorContractId`, `ContractMajorVersion`, `CaseId`, `CaseSchemaVersion`, payload schema, and result schema.
- [ ] Add rejection tests for default/null union values, ambiguous/overlapping cases, nested unions, mutable payloads, and more than one root input.
- [ ] Add compatibility tests: reorder is compatible; rename requires explicit case-ID mapping; add/remove/replace case requires a major version.
- [ ] Run the focused project; capture RED.
- [ ] Implement semantic-symbol lowering. Never persist the union CLR struct or assembly-qualified name.
- [ ] Keep `LanguageVersion.Preview` only inside behavior compiler options.
- [ ] Re-run focused tests.
- [ ] Commit: `feat: compile stable behavior input unions`

## Task 3: Bind every English scenario to executable evidence

**Files:**
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/InstallTestsBddGate.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/Rail/IBehaviorBddGate.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/Rail/BehaviorSnapshot.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors/Rail/BehaviorScenarioResult.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Tests/ProgramSurface.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors.Tests/ScenarioBindingGate.cs`

- [ ] Add failing tests for missing, duplicate, and orphaned scenario bindings.
- [ ] Add a failing scenario test and prove publication remains impossible.
- [ ] Require stable scenario IDs and one executable result per Gherkin scenario.
- [ ] Make the overview a deterministic projection from approved scenarios and include its digest in the signed revision.
- [ ] Run:

```powershell
dotnet test src/core/behaviors/DigitalBrain.Behaviors.Tests -c Release --filter "Scenario|ProgramSurface"
```

Expected RED.

- [ ] Implement the minimum binding/evidence changes.
- [ ] Ensure failures are reader-facing without embedding raw prompts, secrets, or protected payloads.
- [ ] Re-run focused and full behavior tests.
- [ ] Commit: `feat: enforce behavior scenario evidence`

## Task 4: Establish the single-file app SDK surface

**Files:**
- Modify: `src/core/kernel/DigitalBrain.Client/DigitalBrainClient.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors/BehaviorBrain.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors/BehaviorTrigger.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/IBehaviorContext.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Tests/ProgramSurface.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Tests/RailPrograms.cs`

- [ ] Execute this task only as the sequential Slice 2B lane after Slice 1 is integrated; it intentionally attaches to Slice 1 client primitives.
- [ ] Add compile tests for:

```csharp
await using var brain =
    await DigitalBrainClient.ConnectAsync<ResearchCompanyRequest>();

var request = brain.Trigger;
var gmail = brain.Get<IGmail>();
var result = await gmail.SendAsync(new GmailRequest(request.Prompt));
```

- [ ] Keep authored code independent from Orleans `IClusterClient`, grain IDs, MCP clients, service providers, and filesystem/process/network APIs.
- [ ] The SDK must link every operation to the worker attempt cancellation token even when authored code omits an explicit token; an optional narrower token may be linked by the caller.
- [ ] Compile-only hookup is completed here; Slice 3 supplies the isolated broker runtime.
- [ ] Remove `IIntentProgram<TRequest,TResponse>` if CodeGraph proves it is replaced and unreferenced; otherwise list the precise remaining migration caller for Slice 8.
- [ ] Run behavior compiler tests and capture RED/GREEN.
- [ ] Commit: `feat: add single-file behavior SDK`

## Task 5: Derive and enforce directed capability grants

**Files:**
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/Manifest/BehaviorCapabilityGrant.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/ContractOnlyBehaviorCompiler.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/BehaviorInputContractCompiler.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/BehaviorContractCompatibility.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors.Tests/DirectedCapabilityGrants.cs`

- [ ] Execute after Task 4 in the sequential Slice 2B lane.
- [ ] Add RED tests proving the compiler derives each requested edge from `brain.Get<TNeuron>(name)` plus the request/result synapse types passed to `SendAsync`.
- [ ] Persist stable target neuron contract ID, accepted request ID/version, emitted result ID/version, and target instance policy; do not persist method aliases.
- [ ] Add RED admission tests for an undeclared edge, inactive module, incompatible synapse version, widened result type, and legacy method-alias grant.
- [ ] Implement derivation with Roslyn semantic symbols and validate against the exact catalog from Slice 1.
- [ ] Re-run compiler/admission tests and commit: `feat: derive behavior synapse grants`

## Slice Verification

- [ ] `dotnet build src/core/behaviors/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release`
- [ ] `dotnet test src/core/behaviors/DigitalBrain.Behaviors.Tests -c Release`
- [ ] Inspect artifact contents from a test and confirm only the two authored files are exposed.
- [ ] CodeGraph `IIntentProgram`, `IBehaviorProgram`, `BehaviorProgramLoader`, and all preview-language settings.
- [ ] Run a separate read-only Grok review and return the standard handoff.
