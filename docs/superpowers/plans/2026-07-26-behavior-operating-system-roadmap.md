# Behavior Operating System Roadmap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the compiled composition layer with an owner-scoped, journaled Behavior operating system whose approved one-file C# programs execute through exact module contracts.

**Architecture:** Five independently reviewable plans build the rail from the inside out: immutable admission evidence, durable neuron semantics, a Windows LPAC worker, assistant discovery/proposal APIs, and product migration with deletion of the old path. Each plan leaves the root solution green; later plans consume only the exact interfaces named here.

**Tech Stack:** .NET SDK 10.0.302 and .NET 10 file-based apps, Orleans 10.2.2-rc.2 plus Journaling 10.2.2-rc.2.alpha.1, Reqnroll 3.3.4/xUnit v3, Azure Blob Storage 12.29.1, ASP.NET Core gRPC 2.80.0 over Kestrel named pipes, Protobuf 3.31.1, Windows LPAC/Job Objects through CsWin32 0.3.298.

## Global Constraints

- Framework means neuron/synapse mechanics; installed Behaviors are the operating system; modules alone add public CLR neuron and synapse vocabulary.
- Repository and Behavior compilation use the supported Microsoft .NET SDK `10.0.302` with `rollForward: disable` and `allowPrerelease: false`.
- `BehaviorNeuron : Neuron, IBehavior`; a Behavior program implements SDK program contracts and never inherits `Neuron`.
- Behavior identity is `(OwnerId, BehaviorId)` and every approved revision is immutable and addressed by lowercase SHA-256.
- Event subscriptions and schema-validated intent invocation are first-class entry points.
- Search ranks candidates only; exact catalog identity, active revision, schema, owner, visibility, and grants authorize.
- Restore, build, metadata inspection, BDD, and unknown-code execution occur outside the silo with no network or infrastructure credentials.
- Production executes the exact admitted DLL and artifact envelope; invocation never restores, builds, or recompiles.
- Unknown code uses one LPAC process per execution on Windows; trusted signed boot/recovery revisions may select the in-process executor without changing their artifact, context, journal, or tests.
- Every program effect crosses a generated, exact-type module adapter and a result-aware one-use capability delegation.
- No new actor, workflow, queue, event-store, dynamic-proxy, custom-RPC-framing, or vector-database framework is introduced.
- Deletion follows green replacement BDD. Git is the fallback; no dormant legacy route remains.
- Every completed slice runs formatting, analyzers, documentation checks, root Release build, and unfiltered root Release tests.

---

## Execution order

| Order | Plan | Independently green result |
| --- | --- | --- |
| 1 | [`2026-07-26-behavior-foundation-and-admission.md`](2026-07-26-behavior-foundation-and-admission.md) | A canonical one-file Behavior can be compiled once, policy-checked, hashed, signed/verified, stored, and exercised through trusted BDD vocabulary without loading it into the silo; unknown-code approval remains closed. |
| 2 | [`2026-07-26-behavior-kernel-runtime.md`](2026-07-26-behavior-kernel-runtime.md) | One generic Behavior neuron owns approvals/executions/state; aliases, catalog selection, durable queues, exact capability replay, intent receipts, and a signed in-process executor work end to end. |
| 3 | [`2026-07-26-behavior-windows-sandbox.md`](2026-07-26-behavior-windows-sandbox.md) | Unknown admitted code executes in a verified LPAC/Job process and can reach only the per-execution gRPC capability broker. |
| 4 | [`2026-07-26-behavior-assistant-discovery.md`](2026-07-26-behavior-assistant-discovery.md) | An assistant can deterministically discover exact module/Behavior candidates, invoke approved intents, and submit—but never self-approve—new revisions. |
| 5 | [`2026-07-26-behavior-os-migration-and-cleanup.md`](2026-07-26-behavior-os-migration-and-cleanup.md) | UI boot and account enrichment are OS Behaviors; compositions/sample process neurons and contradictory documentation are deleted. |

Do not run plans 2–5 in parallel. Their shared public contracts and deletion gates are intentionally sequential.

## Research basis

The exact APIs, package versions, source internals, safety limits, and rejected alternatives behind
these tasks are recorded in:

- [`../../research/2026-07-26-dotnet-file-based-apps-for-behaviors.md`](../../research/2026-07-26-dotnet-file-based-apps-for-behaviors.md)
- [`../../research/2026-07-26-behavior-compiler-and-testing-stack.md`](../../research/2026-07-26-behavior-compiler-and-testing-stack.md)
- [`../../research/2026-07-26-behavior-runtime-official-stack.md`](../../research/2026-07-26-behavior-runtime-official-stack.md)
- [`../../research/2026-07-26-behavior-security-storage-discovery-stack.md`](../../research/2026-07-26-behavior-security-storage-discovery-stack.md)

Context7 was attempted first as required, but its monthly quota was exhausted. Research therefore
uses Microsoft Learn, official NuGet metadata, exact Orleans/.NET/Azure/ASP.NET source, Windows API
documentation, official upstream project documentation/source, and local API inspection.

## Approved-spec coverage

| Approved design section | Implemented by |
| --- | --- |
| Framework/modules/OS split and one generic Behavior identity | Foundation Tasks 1–3; Kernel Tasks 3, 5, 7 |
| Stable owner/Behavior/revision/execution identity | Foundation Tasks 2–3; Kernel Task 7 |
| Event subscriptions and intent entry points | Kernel Tasks 3–4, 7–8 |
| One-file safe program/context model | Foundation Tasks 2, 4–6 |
| Compile/analyze/BDD/evidence/approval pipeline | Foundation Tasks 3, 5–9; Windows Task 6; Assistant Tasks 3–4 |
| Short durable turns, execution queue, state commit | Kernel Tasks 5–8 |
| Exact capability broker and replay | Kernel Tasks 2, 7–8; Windows Tasks 4–7 |
| Windows isolation and hosted-tier boundary | Windows Tasks 2–7 |
| AI discovery, invocation, proposal, human approval | Assistant Tasks 1–6 |
| Exact BDD artifact and product evidence | Foundation Task 8; Kernel Task 8; Windows Tasks 6–7; Assistant Task 6; Migration Tasks 1–4 |
| Package/hosting ownership | Foundation Task 1; Windows Task 1; Migration Task 4 |
| UI and account-enrichment migration/deletion | Kernel Task 7; Migration Tasks 1–5 |
| Documentation truth and zero-trash completion | Migration Tasks 6–7 |
| Rejected alternatives stay absent | Every plan's dependency constraints plus Migration Task 7 searches |

## Locked project boundaries

| Project/home | Responsibility |
| --- | --- |
| `src/DigitalBrain.Abstractions` | Stable IDs, generic lifecycle/intent receipts, `IBehavior`, and hidden Orleans control contracts |
| `src/DigitalBrain.Behaviors` | Packable author SDK: program/context interfaces, manifests, schemas, grants, artifact/evidence DTOs; no Orleans runtime |
| `src/DigitalBrain.SourceGeneration` | Stable synapse aliases, module capability catalogs, worker adapters, broker invokers, and result codecs |
| `src/DigitalBrain.Kernel` | `BehaviorNeuron`, owner catalog neuron, execution/admission queue neurons, journals, revision selection, replay, routing, and broker grain |
| `src/DigitalBrain.Behaviors.Runtime` | Trusted compiler/test/artifact/schema/executor adapters and hosted queue pumps; no domain authority of its own |
| `src/DigitalBrain.Behaviors.Protocol` | Fixed Protobuf control envelope shared by trusted broker service and worker |
| `src/DigitalBrain.Behaviors.Windows` | Windows-only LPAC/Job/ACL process boundary and named-pipe host/client wiring |
| `hosts/DigitalBrain.BehaviorBuilder` | Sandboxed exact-SDK restore/build/metadata-admission executable |
| `hosts/DigitalBrain.BehaviorWorker` | Self-contained one-execution runtime process with no Orleans/Azure/provider references |
| `os/DigitalBrain.OperatingSystem` | Signed built-in one-file programs, manifests, schemas, and Gherkin features |
| `modules/*` | Compile-time vocabulary and provider/runtime ownership only |
| `hosts/*`, UI, MCP | Authentication, transport, projections, and pixels; no operating-system policy |

## Cross-plan public contract

Plans may add fields but must not rename these identities or change their meanings:

```csharp
public readonly record struct BehaviorId(string Value);
public readonly record struct BehaviorRevisionId(string Value);
public readonly record struct BehaviorExecutionId(Guid Value);

public sealed record BehaviorIntentAddress(
    BehaviorId Behavior,
    string SchemaId,
    int SchemaVersion);

public sealed record BehaviorExecutionReceipt(
    BehaviorExecutionId Execution,
    BehaviorRevisionId Revision);

public interface IBehaviorProgram<in TTrigger> where TTrigger : Synapse
{
    ValueTask ExecuteAsync(
        TTrigger trigger,
        IBehaviorContext context,
        CancellationToken cancellationToken);
}

public interface IIntentProgram<TRequest, TResponse>
{
    ValueTask<TResponse> ExecuteAsync(
        TRequest request,
        IBehaviorContext context,
        CancellationToken cancellationToken);
}
```

`BehaviorRevisionId` hashes the deterministic unsigned artifact envelope. A detached COSE signature is provenance over that digest and never changes or authorizes it.

Test snippets use `fixture` for task-local test setup. Implement those helpers in the named test
file or its existing suite fixture, backed by real DigitalBrain public/internal seams; a fixture
helper is not permission to add a product abstraction, bypass the production rail, or assert a
mocked substitute as product proof.

## Dependency decisions

Add only these direct dependencies when the owning plan reaches them:

```xml
<PackageVersion Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="5.6.0" />
<PackageVersion Include="Microsoft.Orleans.Serialization" Version="10.2.2-rc.2" />
<PackageVersion Include="Azure.Storage.Blobs" Version="12.29.1" />
<PackageVersion Include="Aspire.Azure.Storage.Blobs" Version="13.4.6" />
<PackageVersion Include="System.Security.Cryptography.Cose" Version="10.0.10" />
<PackageVersion Include="JsonSchema.Net" Version="9.3.0" />
<PackageVersion Include="Grpc.AspNetCore" Version="2.80.0" />
<PackageVersion Include="Grpc.Net.Client" Version="2.80.0" />
<PackageVersion Include="Grpc.Tools" Version="2.80.0" />
<PackageVersion Include="Grpc.Core.Api" Version="2.80.0" />
<PackageVersion Include="Google.Protobuf" Version="3.31.1" />
<PackageVersion Include="Microsoft.Windows.CsWin32" Version="0.3.298" />
```

Keep the complete Orleans family on `10.2.2-rc.2` while Journaling requires that RC. Do not add `Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes`; it ships in `Microsoft.AspNetCore.App`. Do not add a vector provider in this roadmap.

`JsonSchema.Net` 9.3.0 has the required draft-2020-12 implementation but its binary carries the Open Source Maintenance Fee EULA. Before its first package restore, record explicit owner/legal acceptance in `eng/approved-dependencies.json`. If acceptance is absent, execution stops at that gate; it must not silently use an old release or a home-grown validator.

## Root evidence gate after every plan

Run from the repository root:

```powershell
dotnet format DigitalBrain.slnx --verify-no-changes
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build
npm --prefix docs test
npm --prefix docs run build
git diff --check
```

The plan-specific focused tests run before this gate. Do not filter the final root test command.

## Final repository audit

The last plan must make each command succeed:

```powershell
rg -n "OpenHomeOnActivationBehavior|IAccountEnrichment|EnrichmentModule|DigitalBrain\.Compositions|ActivateDigitalBrain|BootOnActivation|OpenHome|PostAuthBootstrap" src modules hosts os samples tests README.md CLAUDE.md docs --glob "!docs/archive/**" --glob "!docs/research/**" --glob "!docs/superpowers/plans/**"
rg -n "Type\\.FullName|GetType\\(\\)\\.FullName" src/DigitalBrain.Kernel src/DigitalBrain.SourceGeneration
rg -n "DispatchProxy|MethodInfo\\.Invoke|IClusterClient|Microsoft\\.Orleans\\.Client" hosts/DigitalBrain.BehaviorWorker src/DigitalBrain.Behaviors.Windows
rg -n "TODO|TBD|HACK|TEMP|NotImplementedException|throw new NotSupportedException" src modules hosts os samples tests README.md CLAUDE.md docs --glob "!docs/archive/**" --glob "!docs/research/**" --glob "!docs/superpowers/plans/**"
git ls-files | rg "(^|/)(bin|obj|TestResults|artifacts|\\.vs|\\.idea)/|\\.user$|\\.suo$"
```

The migration vocabulary and trash-marker searches deliberately exclude archived history, research
evidence, and implementation plans. Production, tests, samples, and current-state documentation
must return no matches. Every other command must either return no match or an intentional,
documented test assertion.

### Task 1: Ratify the execution ledger

**Files:**
- Modify: `docs/superpowers/plans/2026-07-26-behavior-operating-system-roadmap.md`
- Create during execution: `docs/architecture/behavior-os-implementation-ledger.md`

**Interfaces:**
- Consumes: The five plan files linked above and the approved runtime design.
- Produces: One checked execution ledger with plan commit IDs, evidence commands, and deletion gates.

- [ ] **Step 1: Create the implementation ledger before product edits**

```markdown
# Behavior OS implementation ledger

| Slice | Plan | Start commit | Completion commit | Root gate | Review |
| --- | --- | --- | --- | --- | --- |
| Admission | behavior-foundation-and-admission | | | pending | pending |
| Kernel | behavior-kernel-runtime | | | pending | pending |
| Windows | behavior-windows-sandbox | | | pending | pending |
| Assistant | behavior-assistant-discovery | | | pending | pending |
| Migration | behavior-os-migration-and-cleanup | | | pending | pending |
```

- [ ] **Step 2: Record the current commit and confirm a clean baseline**

Run: `git rev-parse HEAD; git status --short`

Expected: a commit ID followed by no status entries.

- [ ] **Step 3: Run the root evidence gate**

Run the five commands under “Root evidence gate after every plan.”

Expected: all exit `0`.

- [ ] **Step 4: Commit the ledger**

```powershell
git add docs/architecture/behavior-os-implementation-ledger.md
git commit -m "docs: open behavior operating system implementation ledger"
```
