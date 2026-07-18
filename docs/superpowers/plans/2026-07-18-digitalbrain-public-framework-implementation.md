# DigitalBrain Public Framework Implementation Plan

> **For Claude Code:** Execute this plan task-by-task using test-driven development. One Claude Code session is the sole orchestrator, implementer, verifier, reviewer, and committer. Read-only subagents may be used for review and exploration only; editing is never delegated.

**Goal:** Turn the approved durable neuron foundation into an easy-to-consume public DigitalBrain NuGet framework with separate Aspire hosting and client integrations, real OpenAI and Anthropic role binding, package-only quickstart samples, and development-only Orleans Dashboard and Agent Framework DevUI hosts.

**Architecture:** `DigitalBrainResource` composes official Orleans, Azure Storage, model, embedding, and secret resources. Kernel references receive privileged configuration; `AsClient()` references receive only Orleans client discovery. Public packages target `net8.0`. Samples consume locally packed packages and prove the framework from outside its source graph.

**Primary stack:** Aspire `13.4.6`; Orleans Core/Client/Server `10.2.2-rc.2`; `Microsoft.Orleans.Journaling` and `.AzureStorage` `10.2.2-rc.2.alpha.1`; Microsoft.Extensions.AI `10.8.0`; stable `Aspire.Hosting.OpenAI` `13.4.6`; Anthropic official C# SDK `12.36.0`; OpenAI official SDK `2.12.0`; `Microsoft.Orleans.Dashboard` `10.2.2-rc.2` aligned with the Orleans client, keeping the explicit live compatibility gate; Microsoft Agent Framework DevUI `1.13.0-preview.260703.1` isolated as development-only; Azure Storage/Azurite; xUnit; NuGet pack; SourceLink.

**TFM strategy:** Every public DigitalBrain package and package-only quickstart project targets `net8.0`. Existing repository hosts/tests may remain `net11.0` and consume those packages. The official Orleans journaling packages contain both `net8.0` and `net10.0` assets; the plan does not claim that the selected Orleans or DevUI packages are stable.

## Operator model

- One Claude Code session in `E:\brain` on local branch `master` is the sole orchestrator, implementer, verifier, reviewer, and committer.
- All edits happen in the main session. Read-only subagents are allowed for review and exploration only; editing is never delegated.
- No branches, no worktrees, never push. Each task is committed with the plan's exact commit message once its gates are green.
- The plan checkboxes are updated by the same session as tasks complete.
- The user's documentation override applies: verify every package/framework API with Microsoft Learn, official Aspire/Orleans/OpenAI/Anthropic documentation, and official NuGet metadata (api.nuget.org) before writing code against it.
- Use CodeGraph before reading or changing indexed source and for blast radius before and after each task.
- Any external publish (NuGet test service or nuget.org) requires explicit in-chat user approval first.
- `sources/**` is read-only historical evidence and is never copied as authority.

## Per-task loop

Before every task:

1. Confirm a clean worktree; record HEAD.
2. Query CodeGraph for the task's assigned symbols and blast radius.
3. Run the official API/NuGet preflight for every API the task touches.

During every task, strict TDD:

1. Add the assigned failing test; run the owning project and record the real failure.
2. Implement the minimum supported behavior; rerun the owning project to green; refactor only while green.
3. Never use `dotnet test --filter`.
4. Stay within the task's assigned paths; restore any out-of-scope edit before proceeding.

After every task:

1. Run the task's gates exactly as listed, plus the exact root suite (`dotnet test`) at integration points and the aspire doctor/publish gates where specified.
2. At review checkpoints — security-sensitive, durability, Aspire-resource, package-boundary, dev-tools, demolition, and final tasks — run a code review of the task diff with a read-only reviewer at high effort; fix every actionable finding in the same session and re-run the owning gates.
3. Run `git diff --check`.
4. Commit with the plan's exact message; tick the plan checkboxes.
5. Continue immediately to the next task without routine pauses.

## Task 0: Archive and remove the failed Task 7 attempt; record baseline

**Known failed paths:**

- `Brain.slnx`
- `Directory.Packages.props`
- `hosts/Brain.Kernel.Host/Brain.Kernel.Host.csproj`
- `hosts/Brain.Kernel.Host/Program.cs`
- `modules/AI/**`
- `tests/DigitalBrain.Tests/AI/**`

**Operator actions:**

- [x] Confirm these are the only dirty paths.
- [x] Save a binary patch outside the repository at `E:\brain-recovery\2026-07-18-failed-task7.patch`.
- [x] Record HEAD, SDKs, package pins, Aspire doctor, and current test totals.
- [x] Restore only the listed failed Task 7 paths to HEAD and remove only the listed untracked Task 7 directories.
- [x] Confirm the worktree is clean.
- [x] Run `git diff --check`.
- [x] Run the existing DigitalBrain tests and root solution build.
- [x] Commit no code for the cleanup unless a tracked recovery record is deliberately added.
- [x] Adapt this plan for direct single-actor execution and record the dependency verification below.

**Kill condition:** Stop if any dirty path is not part of the known failed Task 7 output.

**Execution record (2026-07-18):** Baseline HEAD `59bd14a25e30233c48cc7fc3a45b30af4ffe9e07` on `master`. The dirty paths matched the six known failed Task 7 paths exactly; the binary patch captures all ten files including untracked content. After restore and deletion the worktree is clean and `git diff --check` passes. SDKs: dotnet `11.0.100-preview.6.26359.118`, aspire CLI `13.4.6`. `aspire doctor --non-interactive`: 5 passed, 0 warnings, 0 failed. Root build: 0 errors. Root test suite: DigitalBrain.Tests 51 passed / 0 failed / 0 skipped; Brain.FeasibilityTests 11 passed / 0 failed / 0 skipped. Live api.nuget.org flat-container verification: Orleans Core family latest is `10.2.2-rc.2` with no stable `10.2.2`; `Microsoft.Orleans.Journaling` and `.AzureStorage` latest are `10.2.2-rc.2.alpha.1` with no stable journaling release; `Microsoft.Orleans.Dashboard` latest is `10.2.2-rc.2`, so the pin moves from `10.2.2-rc.1` to `10.2.2-rc.2` while keeping the live compatibility gate; `Microsoft.Extensions.AI` and `.OpenAI` latest stable `10.8.0`; `Anthropic` latest `12.36.0`; `OpenAI` latest stable `2.12.0`; `Microsoft.Agents.AI.DevUI` latest `1.13.0-preview.260703.1`; `Aspire.Hosting` and `Aspire.Hosting.OpenAI` latest stable `13.4.6`.

## Task 1: Establish public package identity and packaging quality

**Assigned paths:**

- `Directory.Build.props`
- `Directory.Packages.props`
- `Brain.slnx`
- `kernel/**`
- `hosts/**`
- `modules/**`
- `behaviors/**`
- `eng/pack.ps1`
- `eng/package-metadata.ps1`
- `assets/nuget/**`
- `tests/DigitalBrain.Tests/**`
- `tests/Brain.FeasibilityTests/**`
- `tests/DigitalBrain.PackageTests/**`

**Red tests:**

- Package topology expects `DigitalBrain.Abstractions`, `DigitalBrain.Client`, and `DigitalBrain.Kernel`.
- Public assemblies and namespaces use `DigitalBrain.*`.
- Package metadata, README, icon, XML docs, symbols, deterministic build, and SourceLink are present.
- Public packages target `net8.0`.
- public package graphs contain no dependency on `src/**`, `edge/**`, failed `modules/AI/**`, test projects, or repository-only projects.
- `DigitalBrain.Client` contains no OpenAI, Anthropic, journal-storage, or DevUI dependency.

**Implementation:**

- [x] Rename the active durable foundation projects and namespaces under test.
- [x] Preserve all existing durable behavior and public contract typing.
- [x] Add centralized pack metadata and package-specific descriptions.
- [x] Keep old `src/**`, `edge/**`, and historical trees outside every public package graph until their explicit deletion task.
- [x] Add package README/icon/license inclusion and symbols.
- [x] Add deterministic local pack scripts.
- [x] Pack and inspect each `.nupkg` and `.snupkg`.
- [x] Verify the packages from an empty local feed.

**Execution record (2026-07-18):** Namespaces are `DigitalBrain` for Abstractions/Client and `DigitalBrain.Kernel` for the kernel. The review checkpoint confirmed and this task resolved: the unused `Microsoft.Extensions.Hosting` dependency was removed from `DigitalBrain.Client`; grain-interface identities are pinned with `[Alias(nameof(...))]` on `INeuron`, `IGmail`, and `ISalesforce`; the package-boundary tests forbid provider, journaling, and dev-tool dependency markers per package instead of whitelisting all `Microsoft.*`; the pack fixture and `eng/pack.ps1` discover packable projects and derive the package version from MSBuild instead of duplicating constants; `eng/pack.ps1` always rebuilds the feed directory. The tracked `behaviors/smoke` and `behaviors/inbox-brief` scripts reference `BrainCluster` and modules deleted by the approved durable-neuron demolition and were already dead at this task's start; their deletion belongs to Task 10. The empty-feed restore gate requires nuget.org access by design.

**Gates:**

```powershell
dotnet test .\tests\DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release
dotnet test .\tests\DigitalBrain.PackageTests\DigitalBrain.PackageTests.csproj -c Release
.\eng\pack.ps1
dotnet build .\Brain.slnx -c Release
```

**Commit:** `build: establish public DigitalBrain packages`

## Task 2: Define the typed durable conversation and model-role contracts

**Assigned paths:**

- `kernel/DigitalBrain.Abstractions/AI/**`
- `kernel/DigitalBrain.Abstractions/Conversations/**`
- `kernel/DigitalBrain.Client/AI/**`
- `kernel/DigitalBrain.Client/Conversations/**`
- `kernel/DigitalBrain.Kernel/BrainOwnerIncomingCallFilter.cs`
- `kernel/DigitalBrain.Kernel/Conversations/**`
- `tests/DigitalBrain.Tests/AI/**`
- `tests/DigitalBrain.Tests/Conversations/**`
- `tests/DigitalBrain.Tests/Security/**`

**Red tests:**

- `GptFast`, `ClaudeBalanced`, `GptReasoning`, and `TextEmbedding` descriptors expose provider, model, and capability without secrets.
- Fast, balanced, and reasoning role clients are distinct compile-time types.
- No keyed provider DI or string-based runtime role lookup is present.
- Invalid duplicate or missing role declarations fail deterministically.
- `ConversationId`, `ConversationTurnId`, `ConversationRole`, `ConversationTurnRequest`, `ConversationTurnResult`, and `ConversationSnapshot` are typed Orleans-serializable contracts.
- `IConversationNeuron` exposes only `SubmitTurnAsync` and `ReadAsync`; it has no `Ask`, generic JSON, or provider-specific method.
- `DigitalBrainClient.Conversations.Open(id)` derives an owner-scoped grain identity and cannot address another owner.
- conversation keys use exactly `v1.<base64url owner>.<base64url conversation>`; malformed, extra-segment, and forged-owner keys are rejected.
- the incoming owner filter uses typed composite-key authorization only for the conversation grain and preserves exact owner-key authorization for provider leaves.
- conversation notification streams use the complete canonical key and cannot cross owners.
- `ConversationTurnId` is validated and stable enough to serve as the later durable idempotency key.

**Implementation:**

- [ ] Add provider-neutral model descriptor contracts.
- [ ] Add role marker and typed client abstractions.
- [ ] Add immutable configuration snapshots.
- [ ] Add the typed durable conversation contracts.
- [ ] Add an owner-scoped conversation client facade over Orleans.
- [ ] Add the canonical base64url composite key encoder/parser and the internal typed conversation-grain marker.
- [ ] Update `BrainOwnerIncomingCallFilter` to parse the canonical key only for that typed marker; never use prefix matching.
- [ ] Add `DigitalBrainSessionFactory` so applications create a DI scope only after authentication produces a validated `BrainOwnerId`.
- [ ] Specify optional streamed progress as non-authoritative and final committed results as repairable through `ReadAsync`.
- [ ] Keep provider SDK types out of public contracts and out of `DigitalBrain.Aspire`.

**Gate:**

```powershell
dotnet test .\tests\DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release
```

**Commit:** `feat: define durable DigitalBrain conversations`

## Task 3: Bind real OpenAI, Anthropic, and embedding clients

**Assigned paths:**

- `Directory.Packages.props`
- `kernel/DigitalBrain.Kernel/AI/**`
- `kernel/DigitalBrain.Kernel/Conversations/**`
- `tests/DigitalBrain.Tests/AI/**`
- `tests/DigitalBrain.Tests/Conversations/**`

**Official preflight:**

- Inspect `Microsoft.Extensions.AI.OpenAI`, official `OpenAI`, official `Anthropic`, and embedding APIs using dotnet-inspect.
- Pin exact versions; document Anthropic beta risk.

**Red tests:**

- OpenAI chat descriptors create a real provider-backed `IChatClient`.
- Anthropic descriptors create the official SDK's `IChatClient`.
- embedding descriptors create a real `IEmbeddingGenerator`.
- fake HTTP transports verify request model, endpoint, auth header, response mapping, streaming, cancellation, and provider errors without external credentials.
- no production client unconditionally throws or returns canned data.
- provider clients and credentials resolve only in the privileged kernel service graph.
- `IConversationNeuron.SubmitTurnAsync` invokes the selected typed role inside the kernel and journals a committed `ConversationTurnResult`.
- repeated `ConversationTurnId` values return the single committed result and do not create another committed turn.

**Implementation:**

- [ ] Implement internal provider factories.
- [ ] Register kernel-only role-specific wrappers without keyed DI.
- [ ] Implement the conversation neuron using the durable operation ledger, journaled result, reminder recovery, and durable final notification delivery.
- [ ] Add startup options validation.
- [ ] Add health checks and OpenTelemetry instrumentation.
- [ ] Keep test transports inside tests.

**Gate:**

```powershell
dotnet test .\tests\DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release
```

**Commit:** `feat: bind real DigitalBrain AI providers`

## Task 4: Implement `DigitalBrainResource` and secure reference projections

**Assigned paths:**

- `Directory.Packages.props`
- `integrations/DigitalBrain.Aspire.Hosting/**`
- `hosts/DigitalBrain.AppHost/**`
- `tests/DigitalBrain.Tests/Aspire/**`
- `tests/DigitalBrain.Tests/Security/**`

**Red tests:**

- `AddDigitalBrain("brain")` creates a `DigitalBrainResource`.
- typed `WithLLM<T>()`, role assignment, and `WithEmbedding<T>()` build the approved model.
- `WithReference(brain)` contains kernel-only Orleans, storage, journal, reminder, and provider configuration.
- `WithReference(brain.AsClient())` contains only Orleans client discovery and safe metadata.
- generated environment variables prove no secret or storage leakage to a client.
- publish manifest represents the composite resources.

**Implementation:**

- [ ] Compose official Orleans and Azure Storage resources.
- [ ] Create separate Azurite tables/blobs/queues for clustering, reminders, grain storage, journal storage, streams, and durable outbox needs; do not reuse the journal blob as ordinary grain storage.
- [ ] Configure official `WithClustering`, `WithReminders`, and `WithStreaming` relationships.
- [ ] Use stable official Aspire OpenAI hosting resources.
- [ ] Add a minimal Anthropic connection resource with endpoint, secret parameter, and model property.
- [ ] Implement privileged and client projections.
- [ ] Add health dependencies and wait relationships.
- [ ] Add ATS-compatible exported APIs where supported.
- [ ] Replace the active AppHost's journal-only composition with `AddDigitalBrain`, typed model/embedding declarations, the privileged kernel reference, and at least one restricted test-client reference so the publish gate exercises the real resource.

**Gates:**

```powershell
dotnet test .\tests\DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release
aspire doctor --non-interactive
aspire publish --apphost .\hosts\DigitalBrain.AppHost\DigitalBrain.AppHost.csproj --output-path .\artifacts\aspire-host --non-interactive
```

**Review focus:** secret projection, composite resource ownership, provider-resource correctness, and forbidden architecture.

**Commit:** `feat: add DigitalBrain Aspire hosting resource`

## Task 5: Add the Aspire client integration

**Assigned paths:**

- `integrations/DigitalBrain.Aspire/**`
- `kernel/DigitalBrain.Client/**`
- `tests/DigitalBrain.Tests/Client/**`
- `tests/DigitalBrain.Tests/Aspire/**`

**Red tests:**

- `builder.AddDigitalBrainClient("brain")` connects through the restricted connection.
- `DigitalBrainSessionFactory` resolves from DI and creates an owner-bound scoped `DigitalBrainClient`.
- typed role and conversation clients are Orleans proxies and resolve without provider SDKs.
- missing or malformed connection data fails startup validation.
- health checks and telemetry are registered.
- multiple hosts remain testable without global mutable state.
- OpenAI, Anthropic, embedding, journal, and reminder services are absent from the client DI graph.

**Implementation:**

- [ ] Implement the conventional `IHostApplicationBuilder` integration.
- [ ] Consume `ConnectionStrings:brain` or the official Orleans client configuration emitted by Aspire.
- [ ] Register typed public Orleans-proxied clients, the owner-session factory, and provider-neutral telemetry.
- [ ] Implement fast, balanced, and reasoning client types only as non-grain helpers that set `ConversationRole` and call `IConversationNeuron.SubmitTurnAsync`; do not add a second chat/provider path.
- [ ] Avoid keyed provider DI.
- [ ] Do not reference OpenAI or Anthropic SDK packages from `DigitalBrain.Aspire` or `DigitalBrain.Client`.

**Gate:**

```powershell
dotnet test .\tests\DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release
```

**Commit:** `feat: add DigitalBrain Aspire client integration`

## Task 6: Integrate the public kernel host with official durability

**Assigned paths:**

- `kernel/DigitalBrain.Kernel/**`
- `hosts/Brain.Kernel.Host/**`
- `tests/DigitalBrain.Tests/Kernel/**`
- `tests/Brain.FeasibilityTests/Journaling/**`

**Red tests:**

- `AddDigitalBrainKernel("brain")` consumes privileged configuration.
- missing durable storage prevents production startup.
- official journal recovery survives silo restart.
- external-operation and notification recovery still pass.
- reminders recover pending work.
- streams deliver committed notifications but cannot mutate authority.

**Implementation:**

- [ ] Wire the renamed public kernel package.
- [ ] Preserve official Orleans journaling and Azure Storage.
- [ ] Remove `UseLocalhostClustering`, in-memory reminder registration, and any volatile production journal path from production hosts.
- [ ] Consume the AppHost-projected Orleans clustering, reminder, stream, grain-storage, and distinct journal-storage configuration.
- [ ] Bind typed model roles to kernel services.
- [ ] Preserve owner filters, operation ledger, outbox, reminders, and stream semantics.

**Gates:**

```powershell
dotnet test .\tests\Brain.FeasibilityTests\Brain.FeasibilityTests.csproj -c Release
dotnet test .\tests\DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release
dotnet build .\Brain.slnx -c Release
```

**Review focus:** durability authority, recovery, idempotency, stream semantics, and production storage.

**Commit:** `feat: expose the durable DigitalBrain kernel`

## Task 7: Add optional development tools

**Assigned paths:**

- `Directory.Packages.props`
- `integrations/DigitalBrain.DevTools/**`
- `tests/DigitalBrain.Tests/DevTools/**`

**Red tests:**

- a standalone Orleans Dashboard host joins through `brain.AsClient()` and maps the official dashboard.
- a DevUI host discovers fast, balanced, and reasoning agents backed by owner-bound DigitalBrain conversation proxies.
- neither adapter resolves provider credentials or kernel storage.
- both default to loopback/development-safe access.
- production environment requires explicit opt-in.
- DevUI agent discovery and turns require an explicit owner session and fail closed when the owner parameter is absent.

**Implementation:**

- [ ] Pin `Microsoft.Orleans.Dashboard` `10.2.2-rc.2` and prove compatibility with the `10.2.2-rc.2` Orleans client.
- [ ] Pin `Microsoft.Agents.AI.DevUI` preview only in `DigitalBrain.DevTools`.
- [ ] Add minimal host registration and endpoint helpers.
- [ ] Adapt owner-bound DigitalBrain conversation proxies to Agent Framework agents.
- [ ] Bind the same Development-only `digitalbrain-owner` parameter used by the console and create an owner session before registering DevUI agents.
- [ ] Add access-control and environment guards.

**Gate:**

```powershell
dotnet test .\tests\DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release
```

**Review focus:** preview dependency isolation, credential isolation, local access controls, and accidental production enablement.

**Commit:** `feat: add DigitalBrain development tools`

## Task 8: Build the package-only quickstart

**Assigned paths:**

- `samples/DigitalBrain.Quickstart/**`
- `eng/pack.ps1`
- `eng/test-quickstart.ps1`
- `NuGet.config`
- `tests/DigitalBrain.PackageTests/**`
- `hosts/DigitalBrain.AppHost/**`

**Projects:**

- `DigitalBrain.Quickstart.AppHost`
- `DigitalBrain.Quickstart.Kernel`
- `DigitalBrain.Quickstart.Console`
- `DigitalBrain.Quickstart.OrleansDashboard`
- `DigitalBrain.Quickstart.DevUI`

**Red tests:**

- no quickstart project contains a framework `ProjectReference`.
- restoring from the local package feed succeeds from an empty package cache.
- console startup resolves `DigitalBrainClient`.
- console startup creates an explicit Development owner session before resolving `DigitalBrainClient`.
- `/role`, `/new`, `/conversation`, `/help`, and `/exit` behave deterministically.
- dashboard and DevUI hosts start only in Development by default.
- the AppHost manifest contains kernel, console, dashboard, DevUI, Orleans, storage, and declared model resources.

**Implementation:**

- [ ] Create the five small consumer projects.
- [ ] Use only `PackageReference` for DigitalBrain.
- [ ] Add the interactive console loop.
- [ ] Add the explicit `digitalbrain-owner` Development parameter and owner-session creation; never bypass the owner call filters.
- [ ] Add development-only dashboard and DevUI projects.
- [ ] Add a two-command README and troubleshooting.
- [ ] Add local package feed orchestration.
- [ ] Ensure the sample never calls provider SDKs directly.

**Gates:**

```powershell
.\eng\pack.ps1
.\eng\test-quickstart.ps1
aspire publish --apphost .\samples\DigitalBrain.Quickstart\DigitalBrain.Quickstart.AppHost\DigitalBrain.Quickstart.AppHost.csproj --output-path .\artifacts\quickstart-publish --non-interactive
```

**Review focus:** true package consumption, setup simplicity, secret flow, and direct provider bypasses.

**Commit:** `samples: add DigitalBrain package quickstart`

## Task 9: Prove live framework behavior and restart recovery

**Assigned paths:**

- `tests/DigitalBrain.PackageTests/**`
- `eng/test-quickstart.ps1`
- `samples/DigitalBrain.Quickstart/**`

**Automated proof:**

- [ ] Start the quickstart AppHost with a test-only HTTP provider resource, explicit OpenAI/Anthropic endpoint overrides, and synthetic secret parameters.
- [ ] Prove that the normal privileged kernel provider factories and official SDK adapters call that HTTP resource; do not replace `IChatClient` or use ambient credentials.
- [ ] Wait for Azurite, Orleans, kernel, console test driver, dashboard, and DevUI to become healthy.
- [ ] Send a durable turn through `DigitalBrain.Client`.
- [ ] Verify the selected role reached the correct provider adapter.
- [ ] Restart the kernel.
- [ ] Resume the same conversation and verify durable state.
- [ ] Verify Orleans Dashboard endpoint.
- [ ] Verify DevUI entity discovery and one role-backed turn.
- [ ] Capture Aspire resource states, traces, and logs.
- [ ] Stop all resources and prove no orphaned processes.

**Optional real-provider proof:**

- Run one OpenAI turn when `OPENAI_API_KEY` exists.
- Run one Claude turn when `ANTHROPIC_API_KEY` exists.
- Missing real credentials do not invalidate deterministic offline gates, but must be reported.

**Gates:**

```powershell
.\eng\test-quickstart.ps1 -Live
aspire ps --non-interactive
```

**Commit:** `test: prove DigitalBrain package quickstart recovery`

## Task 10: Remove superseded active architecture

**Assigned paths:**

- `src/**`
- `edge/**`
- superseded active projects identified by the approved durable-neuron demolition tasks
- any reintroduced `modules/AI/**` throw-stub project
- `Brain.slnx`
- architecture tests

**Red tests:**

- the active solution contains only the approved DigitalBrain framework, integrations, hosts, modules, tests, and samples.
- forbidden old generic invocation and MCP surfaces are absent.
- no active project references `sources/**`.
- no duplicate `Brain.*` foundation package competes with `DigitalBrain.*`.
- no throw-only AI hosting project or `ConfigurationBoundChatClient` remains.

**Implementation:**

- [ ] Follow the deletion inventory from the approved durable-neuron plan.
- [ ] Delete superseded code rather than adapting it.
- [ ] Confirm the failed `modules/AI` experiment was deleted or its approved contracts were absorbed into the public DigitalBrain packages; never retain both.
- [ ] Preserve only explicitly approved provider modules and behaviors.
- [ ] Update topology tests.

**Gates:**

```powershell
dotnet test .\Brain.slnx -c Release
rg -n "DispatchProxy|InvokeMcpTool|\\bAsk\\b|Kind routing|JsonElement" kernel integrations hosts modules samples tests
git diff --check
```

**Review focus:** deletion completeness, hidden compatibility paths, duplicate package identity, generic routing, and MCP duplication.

**Commit:** `refactor: remove superseded DigitalBrain architecture`

## Task 11: Complete NuGet release engineering

**Assigned paths:**

- `.github/workflows/**`
- `Directory.Build.props`
- `eng/**`
- `docs/**`
- `packages/DigitalBrain/**`
- package project files
- package README files

**Red tests:**

- clean Release pack creates all expected packages and symbols.
- package dependencies contain no repository-only projects.
- public APIs match the approved baseline.
- README and icon render from each package.
- a clean consumer install succeeds.
- vulnerable, deprecated, and unexpected preview dependencies fail the release gate; the approved DevUI preview is isolated and explicitly allowlisted.

**Implementation:**

- [ ] Add CI build/test/pack/package-consumer jobs.
- [ ] Add the optional `DigitalBrain` convenience package referencing only `DigitalBrain.Abstractions`, `DigitalBrain.Client`, and `DigitalBrain.Aspire`; it must not reference Kernel, Aspire.Hosting, DevTools, OpenAI, Anthropic, or storage packages.
- [ ] Add API compatibility baselines only after Task 10 removes competing public surfaces.
- [ ] Add SourceLink and reproducibility checks.
- [ ] Add prerelease versioning and changelog.
- [ ] Add NuGet test-service instructions.
- [ ] Document prefix reservation and publishing credential requirements.
- [ ] Never put a NuGet API key in the repository.

**Gates:**

```powershell
dotnet test .\Brain.slnx -c Release
.\eng\pack.ps1 -Clean
.\eng\test-quickstart.ps1 -CleanCache
git diff --check
```

**Commit:** `build: prepare DigitalBrain NuGet release`

## Task 12: Final verification, documentation, and review

- [ ] Run all solution tests in Release.
- [ ] Pack from a clean checkout-equivalent state.
- [ ] Restore and build the quickstart from the local feed and empty cache.
- [ ] Run the live controlled-provider restart test.
- [ ] Run optional real OpenAI and Anthropic turns when credentials exist.
- [ ] Inspect the Aspire publish manifest.
- [ ] Verify Orleans Dashboard and DevUI.
- [ ] Run `git diff --check`.
- [ ] Query CodeGraph for the final architecture and blast radius.
- [ ] Run a fresh read-only architecture review.
- [ ] Run a second read-only review focused on NuGet usability, samples, secret boundaries, durability, and forbidden shortcuts.
- [ ] Fix every actionable finding in this session and repeat the owning gates.
- [ ] Confirm clean worktree.
- [ ] Prepare prerelease packages and checksums.
- [ ] Stop before public NuGet push unless the operator has explicit API-key authority.

**Final commands:**

```powershell
dotnet test .\Brain.slnx -c Release
.\eng\pack.ps1 -Clean
.\eng\test-quickstart.ps1 -CleanCache -Live
aspire doctor --non-interactive
aspire publish --apphost .\samples\DigitalBrain.Quickstart\DigitalBrain.Quickstart.AppHost\DigitalBrain.Quickstart.AppHost.csproj --output-path .\artifacts\quickstart-publish --non-interactive
git status --short
git diff --check
```

**Final commit:** `docs: complete DigitalBrain public framework`

## Kill conditions

Stop only when:

- an unknown user-owned change overlaps assigned paths;
- official API behavior disproves the approved design;
- a required package has an unresolved security or license problem;
- durable recovery cannot be achieved with the official Orleans integrations;
- provider credentials are required for a non-optional gate and unavailable;
- public package IDs have become unavailable;
- the architecture must materially change.

Do not stop for routine task confirmation.
