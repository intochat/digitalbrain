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
- `kernel/DigitalBrain.Client/DigitalBrainClient.cs`
- `kernel/DigitalBrain.Client/DigitalBrainClientExtensions.cs`
- `kernel/DigitalBrain.Client/DigitalBrainSessionFactory.cs`
- `kernel/DigitalBrain.Client/AI/**`
- `kernel/DigitalBrain.Client/Conversations/**`
- `kernel/DigitalBrain.Kernel/BrainOwnerIncomingCallFilter.cs`
- `kernel/DigitalBrain.Kernel/Conversations/**`
- `kernel/DigitalBrain.Kernel/NeuronTypeCatalogBuilder.cs`
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

- [x] Add provider-neutral model descriptor contracts.
- [x] Add role marker and typed client abstractions.
- [x] Add immutable configuration snapshots.
- [x] Add the typed durable conversation contracts.
- [x] Add an owner-scoped conversation client facade over Orleans.
- [x] Add the canonical base64url composite key encoder/parser and the internal typed conversation-grain marker.
- [x] Update `BrainOwnerIncomingCallFilter` to parse the canonical key only for that typed marker; never use prefix matching.
- [x] Add `DigitalBrainSessionFactory` so applications create a DI scope only after authentication produces a validated `BrainOwnerId`.
- [x] Specify optional streamed progress as non-authoritative and final committed results as repairable through `ReadAsync`.
- [x] Keep provider SDK types out of public contracts and out of `DigitalBrain.Aspire`.

**Gate:**

```powershell
dotnet test .\tests\DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release
```

**Commit:** `feat: define durable DigitalBrain conversations`

**Execution record (2026-07-18):** Baseline HEAD was `e67c2031a0554a9f4137835082ec982fda0baac6` on `master`; the starting 19 dirty paths matched the approved Task 2 handoff, and the assigned-path amendment above accounts for the client/session/catalog files required by the approved design. Official Microsoft Learn guidance confirmed generated Orleans grain references, incoming call filters, `RequestContext` propagation, serializer aliases, memory streams, and direct client subscriptions; exact `10.2.2-rc.2` inspection confirmed `IIncomingGrainCallContext`, `IIncomingGrainCallFilter`, `IGrainFactory`, `IStreamPubSub`, `QualifiedStreamId`, `StreamId`, and `AliasAttribute` signatures. Official OpenAI and Anthropic model references confirmed the approved `gpt-5`, `gpt-5-mini`, `text-embedding-3-small`, and `claude-sonnet-4-5` identifiers. The inherited red build exposed 32 compile errors; the first corrective run then failed 8 of 126 tests for default turn identity, invalid owner canonicalization, and public marker visibility. The first fresh review produced three reproduced failures out of 129 tests: cross-owner direct stream subscription, lossy UTF-8 identity collisions, and overlapping ambient sessions. The real Orleans pub/sub boundary was identified as `IPubSubRendezvousGrain` after the narrower `IStreamPubSub` declaring-type hypothesis failed; the incoming filter now authorizes the exact notification provider/namespace and canonical stream key. Strict UTF-8 prevents surrogate replacement collisions, the complete `IConversationNeuron` hierarchy is excluded from one-per-owner discovery, and session creation rejects overlap. Orleans analyzer evidence drove a further red method-alias contract test. A focused re-review then reproduced stale-session redisposal as 1 failed / 130 passed; disposal is now atomic, idempotent, owner-aware, and clears synchronously. The final focused review reported no critical, important, or minor findings and assessed the task ready. The final owning gate passed 131 / 131; exact root `dotnet test --logger "console;verbosity=minimal"` passed DigitalBrain.Tests 131 / 131, DigitalBrain.PackageTests 11 / 11, and Brain.FeasibilityTests 11 / 11. `aspire doctor --non-interactive` passed 5 / 5 with no warnings or failures; resource inspection correctly reported no running AppHost. CodeGraph confirmed the final owner-to-client-to-canonical-key-to-filter path and the three `DigitalBrainClient` consumers. Scope, provider-type, role-routing, comment, untracked-file, non-authoritative-progress/final-`ReadAsync` repair, and `git diff --check` guards passed.

## Task 3: Bind real OpenAI, Anthropic, and embedding clients

**Assigned paths:**

- `Directory.Packages.props`
- `kernel/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`
- `kernel/DigitalBrain.Kernel/Neuron.cs`
- `kernel/DigitalBrain.Kernel/NeuronJournalJsonContext.cs`
- `kernel/DigitalBrain.Kernel/NeuronOutboxDrainer.cs`
- `kernel/DigitalBrain.Kernel/AI/**`
- `kernel/DigitalBrain.Kernel/Conversations/**`
- `tests/DigitalBrain.Tests/AI/**`
- `tests/DigitalBrain.Tests/Conversations/**`
- `tests/DigitalBrain.Tests/Kernel/NeuronOutboxTests.cs`
- `tests/DigitalBrain.Tests/Kernel/TestNeuron.cs`
- `tests/DigitalBrain.PackageTests/PackageContentTests.cs`

**Assigned-path amendment:** `DigitalBrain.Kernel.csproj` is required because central package management pins versions but does not add the provider, adapter, options-validation, or health-check references needed by the kernel implementation. `NeuronJournalJsonContext.cs` is required because the System.Text.Json source generator rejects `JsonSerializable` attributes split across partial context declarations with duplicate generated hint names; the conversation journal types must be registered on the existing official journal context. `NeuronOutboxDrainer.cs` and its existing kernel test seam are required because red review exposed a hard-crash window between the committed conversation result/outbox and the drainer's first failure-based reminder registration. Conversation execution must pre-arm the inherited recovery reminder, and the generic empty-drain path must remove that reminder after crashes before durable work exists. `Neuron.cs` is required because crash recovery commits during activation, before the conversation coordinator's redaction boundary; the base durable commit mapper must not expose journal backend details. `PackageContentTests.cs` is required because the root package gate must explicitly permit the two approved provider SDKs in `DigitalBrain.Kernel` while continuing to reject them in the application-facing Abstractions and Client packages. The universal neuron state and reminder identity remain unchanged.

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

- [x] Implement internal provider factories.
- [x] Register kernel-only role-specific wrappers without keyed DI.
- [x] Implement the conversation neuron using the durable operation ledger, journaled result, reminder recovery, and durable final notification delivery.
- [x] Add startup options validation.
- [x] Add health checks and OpenTelemetry instrumentation.
- [x] Keep test transports inside tests.

**Gate:**

```powershell
dotnet test .\tests\DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release
```

**Commit:** `feat: bind real DigitalBrain AI providers`

**Execution record (2026-07-18):** Baseline HEAD was `d61b8b665d6d4a212bd3aab9d01e41556a000af9` on `master`. Exact official API inspection confirmed Microsoft.Extensions.AI and its OpenAI adapter at `10.8.0`, OpenAI at `2.12.0`, Anthropic at the deliberately hard-pinned beta `12.36.0`, official client adapters, error types, streaming, cancellation, custom HTTP transport, and System.ClientModel `1.14.0`; the Anthropic beta remains an explicit upgrade-review risk because minor and patch releases may break compatibility. The initial red suite failed only on the absent Task 3 provider and conversation types. Real official clients now back fast, balanced, reasoning, and embedding descriptors; controlled HTTP transports remain test-only and prove endpoint, model, authorization, payload, response, streaming, cancellation, confirmed HTTP errors, and malformed responses. Kernel-only non-keyed role wrappers route conversation work through a durable intent-before-dispatch ledger, committed results and revisions, idempotent turn identities, final notification outbox, and recovery reminder. Review reproduced and closed activation-local uncommitted-state exposure, a hard-crash window before the first reminder, cross-turn reminder removal with an older pending notification, ambiguous SSE/decode/empty-response misclassification, incomplete committed snapshots, plaintext non-loopback endpoints, provider/storage/stream/reminder/cancellation detail leakage, and public serializer-context compatibility. The activation guard now invalidates and deactivates after failed writes; recovery is armed before mutation or provider dispatch; empty drains self-clean; pending notifications retain recovery; only explicit HTTP rejections become `Failed`; all ambiguous outcomes become `Unknown`; and base neuron, coordinator, and outbox boundaries emit stable errors. Three independent final reviews reported no Critical, Important, or Minor findings. Release kernel build passed with zero warnings and errors. The exact owning gate passed 192 / 192. The first exact root gate exposed the stale package dependency allowlist; the amended test now permits only `OpenAI` and `Anthropic` in `DigitalBrain.Kernel` while retaining the application-facing SDK ban. The final exact root gate passed DigitalBrain.Tests 192 / 192, DigitalBrain.PackageTests 11 / 11, and Brain.FeasibilityTests 11 / 11. `aspire doctor` passed 5 / 5 with no warnings or failures; AppHost and resource inspection correctly reported no running AppHost. Exact package graph, zero-comment, provider-keyed-DI, production-test-double, public-SDK-leak, assigned-path, CodeGraph blast-radius, and `git diff --check` guards passed.

## Task 4: Implement `DigitalBrainResource` and secure reference projections

**Assigned paths:**

- `Directory.Packages.props`
- `integrations/DigitalBrain.Aspire.Hosting/**`
- `hosts/DigitalBrain.AppHost/**`
- `tests/DigitalBrain.Tests/Aspire/**`
- `tests/DigitalBrain.Tests/Security/**`

**Assigned-path amendment:** `Brain.slnx` and `tests/DigitalBrain.Tests/Architecture/ProjectTopologyTests.cs` are required because the new public hosting integration must participate in the active solution immediately rather than remaining an orphan until the later demolition task. `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj` is required to reference the hosting integration, the active AppHost, and the official Aspire testing package so the assigned resource-model tests can inspect the exact publish-mode model used by the real host. `tests/DigitalBrain.PackageTests/PackageContentTests.cs` is required because adding the fourth packable public package changes the exact package family and permits Aspire hosting dependencies only in `DigitalBrain.Aspire.Hosting`; the existing application-facing dependency guards remain unchanged. `NuGet.config` is required because its broad `Aspire*` source mapping otherwise excludes the approved stable `Aspire.Hosting.OpenAI` `13.4.6` package from NuGet.org; the amendment maps only that exact package ID to NuGet.org. The AppHost uses a synthetic restricted client container because no approved active client project exists before the package-only quickstart task; this resource exists only to exercise the restricted projection in the required publish gate. Discovery uses a dedicated Azure Storage account so the client-visible clustering credential cannot authorize access to privileged grain state, journals, reminders, streams, or outbox data.

**Red tests:**

- `AddDigitalBrain("brain")` creates a `DigitalBrainResource`.
- typed `WithLLM<T>()`, role assignment, and `WithEmbedding<T>()` build the approved model.
- `WithReference(brain)` contains kernel-only Orleans, storage, journal, reminder, and provider configuration.
- `WithReference(brain.AsClient())` contains only Orleans client discovery and safe metadata.
- generated environment variables prove no secret or storage leakage to a client.
- publish manifest represents the composite resources.

**Implementation:**

- [x] Compose official Orleans and Azure Storage resources.
- [x] Create separate Azurite tables/blobs/queues for clustering, reminders, grain storage, journal storage, streams, and durable outbox needs; do not reuse the journal blob as ordinary grain storage.
- [x] Configure official `WithClustering`, `WithReminders`, and `WithStreaming` relationships.
- [x] Use stable official Aspire OpenAI hosting resources.
- [x] Add a minimal Anthropic connection resource with endpoint, secret parameter, and model property.
- [x] Implement privileged and client projections.
- [x] Add health dependencies and wait relationships.
- [x] Add ATS-compatible exported APIs where supported.
- [x] Replace the active AppHost's journal-only composition with `AddDigitalBrain`, typed model/embedding declarations, the privileged kernel reference, and at least one restricted test-client reference so the publish gate exercises the real resource.

**Gates:**

```powershell
dotnet test .\tests\DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release
aspire doctor --non-interactive
aspire publish --apphost .\hosts\DigitalBrain.AppHost\DigitalBrain.AppHost.csproj --output-path .\artifacts\aspire-host --non-interactive
```

**Review focus:** secret projection, composite resource ownership, provider-resource correctness, and forbidden architecture.

**Commit:** `feat: add DigitalBrain Aspire hosting resource`

**Execution record (2026-07-18):** Baseline HEAD was `42bc11cf37ae20bdf5be1746fa923b8803450f53` on `master`. Exact official API inspection covered Aspire `13.4.6` Orleans, Azure Storage, OpenAI, resource annotations, connection properties, waits, and publish-mode testing. TDD began with the approved missing-type failures; the first restore also exposed the repository's over-broad Aspire package-source mapping, which was narrowed for the exact stable OpenAI hosting package. The public hosting integration now composes separate discovery and privileged durability accounts, distinct reminder, grain-state, journal, stream, and outbox resources, official Orleans clustering/reminder/stream relationships, stable OpenAI resources, an explicit Anthropic connection resource, typed model declarations, and ATS exports. Its privileged reference projects the complete kernel configuration, while its restricted client projection carries only clustering discovery and safe metadata; generated-environment and transitive-reference tests prove that no privileged storage or provider secret crosses that boundary. The active AppHost now uses the composite resource for its kernel and an explicit-start restricted client, and an official testing-builder test inspects that exact publish-mode graph because the Azure publisher correctly emits storage infrastructure but reports that this AppHost has no configured compute environment. Review reproduced and closed shared-account credential exposure, shared OpenAI endpoint mutation, incomplete transitive waits, missing Anthropic connection properties, insufficient ATS metadata, and dependency-allowlist drift. Two independent final follow-up reviews reported no findings. The exact owning gate passed 202 / 202, package tests passed 12 / 12, and the AppHost Release build passed with zero warnings and errors. `aspire doctor --non-interactive` passed 5 / 5 with no warnings or failures. The required publish command succeeded with only expected unset-secret parameter warnings and the no-compute-environment warning, producing distinct discovery and durability storage modules. The final exact root gate passed DigitalBrain.Tests 202 / 202, DigitalBrain.PackageTests 12 / 12, and Brain.FeasibilityTests 11 / 11; one earlier root run had a known transient two-second cancellation-test timeout and its immediate exact rerun passed. AppHost resource inspection correctly reported no running host. CodeGraph blast-radius inspection, exact package graph, zero-comment, assigned-path, `git diff --check`, and generated-artifact cleanup guards passed.

## Task 5: Add the Aspire client integration

**Assigned paths:**

- `Brain.slnx`
- `integrations/DigitalBrain.Aspire/**`
- `kernel/DigitalBrain.Client/**`
- `tests/DigitalBrain.PackageTests/DotnetCli.cs`
- `tests/DigitalBrain.PackageTests/PackageContentTests.cs`
- `tests/DigitalBrain.Tests/Architecture/ProjectTopologyTests.cs`
- `tests/DigitalBrain.Tests/Client/**`
- `tests/DigitalBrain.Tests/Aspire/**`
- `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`

**Red tests:**

- `builder.AddDigitalBrainClient("brain")` connects through the restricted connection.
- `DigitalBrainSessionFactory` resolves from DI and creates an owner-bound scoped `DigitalBrainClient`.
- typed role and conversation clients are Orleans proxies and resolve without provider SDKs.
- missing or malformed connection data fails startup validation.
- health checks and telemetry are registered.
- multiple hosts remain testable without global mutable state.
- OpenAI, Anthropic, embedding, journal, and reminder services are absent from the client DI graph.

**Implementation:**

- [x] Implement the conventional `IHostApplicationBuilder` integration.
- [x] Consume `ConnectionStrings:brain` or the official Orleans client configuration emitted by Aspire.
- [x] Register typed public Orleans-proxied clients, the owner-session factory, and provider-neutral telemetry.
- [x] Implement fast, balanced, and reasoning client types only as non-grain helpers that set `ConversationRole` and call `IConversationNeuron.SubmitTurnAsync`; do not add a second chat/provider path.
- [x] Avoid keyed provider DI.
- [x] Do not reference OpenAI or Anthropic SDK packages from `DigitalBrain.Aspire` or `DigitalBrain.Client`.

**Gate:**

```powershell
dotnet test .\tests\DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release
```

**Commit:** `feat: add DigitalBrain Aspire client integration`

**Execution record (2026-07-18):** Baseline HEAD was `86343ca528576cd28a4a233208068b3be1a4c120` on `master`. Context7 was attempted first but its monthly quota was exhausted, so exact API work used current Microsoft documentation, Aspire `13.4.6` and Orleans `10.2.2-rc.2` source and XML metadata, and the restored package assemblies. TDD began with missing host integration, options, and role-facade failures. The new runtime package consumes the exact restricted `Orleans` and `DigitalBrain:Client` projection from Task 4, or synthesizes deterministic cluster metadata for a direct `ConnectionStrings:<name>` registration, while using the same official keyed Azure Tables and Orleans provider path for both storage connection strings and HTTP(S) service URIs. Startup validation fails closed for missing, malformed, partial, mismatched, or unsupported configuration without exposing credentials. The client registers owner-bound sessions, provider-neutral role helpers, activity propagation, traces, metrics, Azure Tables health, and a per-host Orleans connection observer without global mutable host state or a public health-control surface. Package and DI tests prove the client graph excludes provider, embedding, kernel, journaling, and reminder services; application-facing packages now have exact direct-dependency allowlists, and `DigitalBrain.Aspire` has only `DigitalBrain.Client` inside the public graph. The package gate exposed an MSBuild node-reuse worker retaining redirected output; disabling node reuse in its local CLI harness removed the leak and reduced the exact gate to a deterministic completion. Two independent follow-up reviews reported no findings after closing service-URI validation, DI-purity coverage, dependency-allowlist breadth, and health-surface findings. The exact owning gate passed 215 / 215, package tests passed 13 / 13 including isolated empty-cache consumer restore, and the final exact root gate passed DigitalBrain.Tests 215 / 215, DigitalBrain.PackageTests 13 / 13, and Brain.FeasibilityTests 11 / 11. `aspire doctor --non-interactive` passed 5 / 5 with no warnings or failures, and AppHost resource inspection correctly reported no running host. CodeGraph blast-radius inspection, zero-comment, assigned-path, `git diff --check`, and generated-artifact cleanup guards passed.

## Task 6: Integrate the public kernel host with official durability

**Assigned paths:**

- `Directory.Packages.props`
- `nuget.config`
- `kernel/DigitalBrain.Kernel/**`
- `hosts/Brain.Kernel.Host/**`
- `tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj`
- `tests/DigitalBrain.Tests/Kernel/**`
- `tests/Brain.FeasibilityTests/Journaling/**`
- `tests/DigitalBrain.PackageTests/PackageContentTests.cs`
- `tests/DigitalBrain.Tests/AI/AnthropicProviderClientTests.cs`

**Red tests:**

- `AddDigitalBrainKernel("brain")` consumes privileged configuration.
- missing durable storage prevents production startup.
- official journal recovery survives silo restart.
- external-operation and notification recovery still pass.
- reminders recover pending work.
- streams deliver committed notifications but cannot mutate authority.

**Implementation:**

- [x] Wire the renamed public kernel package.
- [x] Preserve official Orleans journaling and Azure Storage.
- [x] Remove `UseLocalhostClustering`, in-memory reminder registration, and any volatile production journal path from production hosts.
- [x] Consume the AppHost-projected Orleans clustering, reminder, stream, grain-storage, and distinct journal-storage configuration.
- [x] Bind typed model roles to kernel services.
- [x] Preserve owner filters, operation ledger, outbox, reminders, and stream semantics.

**Gates:**

```powershell
dotnet test .\tests\Brain.FeasibilityTests\Brain.FeasibilityTests.csproj -c Release
dotnet test .\tests\DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release
dotnet build .\Brain.slnx -c Release
```

**Review focus:** durability authority, recovery, idempotency, stream semantics, and production storage.

**Commit:** `feat: expose the durable DigitalBrain kernel`

**Execution record (2026-07-18):** Baseline HEAD was `ea909c9311bdebd726da50305e99113f1a62069f` on `master`. The assigned paths were amended before commit to include central package/source mapping, the feasibility project, the package dependency allowlist, and the synchronized Anthropic cancellation regression required by the root gate. Current Orleans `10.2.2-rc.2`, Aspire `13.4.6`, Azure SDK metadata, restored assemblies, and official source confirmed that `UseOrleans` applies the projected clustering, reminder, default grain-storage, and named stream providers; the kernel therefore adds only the distinct Azure Blob journal and `PubSubStore` composition explicitly. TDD covered the missing public registration, absent/malformed/ambiently redirected storage, exact and case-insensitive service-key collisions, missing `PubSubStore`, production journal selection, real stream delivery without stream authority, and reminder restart recovery. The public host now consumes only the privileged AppHost projection, registers official keyed Table/Blob/Queue clients, validates SDK-compatible connection strings and secure service URIs without exposing credentials, rejects stale ambient redirects and storage aliases, preserves a journal container/client distinct from grain state, binds typed AI roles, and contains no localhost, in-memory reminder, or volatile journal fallback. Azurite-backed feasibility tests start and stop the complete production kernel with the official Orleans providers, recover journaled values, maps, queues, lists, reminders, and execution identity across silo restarts, prove reminder removal, and prove a post-commit scheduling failure cannot strand durable work; the official cross-process port allocator removes endpoint bind races. Three independent final reviews reported no actionable boundary, durability, startup, or stream-authority findings. The exact gates passed Brain.FeasibilityTests 13 / 13, DigitalBrain.Tests 226 / 226, and the Release solution build with zero warnings or errors. The final exact root checkpoint passed DigitalBrain.Tests 226 / 226, DigitalBrain.PackageTests 13 / 13, and Brain.FeasibilityTests 13 / 13. `aspire doctor` passed 5 / 5 with no warnings or failures, and resource inspection correctly reported that no AppHost was running. Final CodeGraph blast-radius inspection, forbidden-fallback and added-comment scans, `git diff --check`, package-boundary checks, and generated-artifact cleanup checks passed.

## Task 7: Add optional development tools

**Assigned paths:**

- `Directory.Packages.props`
- `integrations/DigitalBrain.DevTools/**`
- `tests/DigitalBrain.Tests/DevTools/**`
- `Brain.slnx`
- `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- `tests/DigitalBrain.Tests/Architecture/ProjectTopologyTests.cs`
- `tests/DigitalBrain.PackageTests/PackageContentTests.cs`

**Red tests:**

- a standalone Orleans Dashboard host joins through `brain.AsClient()` and maps the official dashboard.
- a DevUI host discovers fast, balanced, and reasoning agents backed by owner-bound DigitalBrain conversation proxies.
- neither adapter resolves provider credentials or kernel storage.
- both default to loopback/development-safe access.
- production environment requires explicit opt-in.
- DevUI agent discovery and turns require an explicit owner session and fail closed when the owner parameter is absent.

**Implementation:**

- [x] Pin `Microsoft.Orleans.Dashboard` `10.2.2-rc.2` and prove compatibility with the `10.2.2-rc.2` Orleans client.
- [x] Pin `Microsoft.Agents.AI.DevUI` preview only in `DigitalBrain.DevTools`.
- [x] Add minimal host registration and endpoint helpers.
- [x] Adapt owner-bound DigitalBrain conversation proxies to Agent Framework agents.
- [x] Bind the same Development-only `digitalbrain-owner` parameter used by the console and create an owner session before registering DevUI agents.
- [x] Add access-control and environment guards.

**Gate:**

```powershell
dotnet test .\tests\DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release
```

**Review focus:** preview dependency isolation, credential isolation, local access controls, and accidental production enablement.

**Commit:** `feat: add DigitalBrain development tools`

**Execution record (2026-07-18):** Baseline HEAD was `a7390c8574c2557397014d7e99417a918473a1f2` on `master`. The assigned paths were amended before commit to include the solution entry, test project reference, topology assertion, and package dependency contract required by the new public package. Official NuGet metadata, restored XML documentation and assemblies, and official package source verified the Orleans Dashboard client/silo registration and route APIs, the Agent Framework DevUI, Responses, Conversations, keyed-agent hosting APIs, and the `IChatClient` contract. `DigitalBrain.DevTools` is a net8-only optional package with the exact direct graph `DigitalBrain.Aspire`, `Microsoft.Agents.AI`, `Microsoft.Agents.AI.DevUI`, `Microsoft.Extensions.AI`, and `Microsoft.Orleans.Dashboard`; package tests pin DevUI `1.13.0-preview.260703.1`, align Dashboard and Orleans Client at `10.2.2-rc.2`, and forbid every dev-tool marker from the other public packages. The standalone dashboard uses the restricted DigitalBrain Aspire client and the official dashboard routes; the silo helper adds the official cluster half. DevUI exposes exactly the fast, balanced, and reasoning owner-bound agents, validates and disposes an explicit owner session before discovery, and opens a fresh owner session for every durable turn without retaining a privileged client. Dashboard, UI, discovery, Responses, and Conversations share fail-closed endpoint guards: direct loopback is the Development default, forwarded callers never count as loopback, remote access always requires a bearer token, and production additionally requires explicit opt-in plus a token even when loopback-only. Three read-only reviews covered package isolation, security/owner authority, and exact package API/runtime use; all actionable findings were fixed, and the final API review reported no P0-P2 findings. The exact Task 7 gate passed DigitalBrain.Tests 245 / 245, the additional package gate passed 15 / 15, and the Release solution build completed with zero warnings or errors. The first exact root checkpoint encountered one unchanged timing-sensitive OpenAI cancellation-test timeout while the other 272 tests passed; an unchanged exact rerun passed DigitalBrain.Tests 245 / 245, DigitalBrain.PackageTests 15 / 15, and Brain.FeasibilityTests 13 / 13. `aspire doctor` passed 5 / 5 with no warnings or failures, Aspire inspection confirmed no running AppHost, and comment, dependency-isolation, generated-artifact, and `git diff --check` scans passed.

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

- [x] Create the five small consumer projects.
- [x] Use only `PackageReference` for DigitalBrain.
- [x] Add the interactive console loop.
- [x] Add the explicit `digitalbrain-owner` Development parameter and owner-session creation; never bypass the owner call filters.
- [x] Add development-only dashboard and DevUI projects.
- [x] Add a two-command README and troubleshooting.
- [x] Add local package feed orchestration.
- [x] Ensure the sample never calls provider SDKs directly.

**Gates:**

```powershell
.\eng\pack.ps1
.\eng\test-quickstart.ps1
```

**Operator scope amendment (2026-07-19):** Publishing is deferred. The quickstart remains Development-only, and the `aspire publish` gate was not run.

**Review focus:** true package consumption, setup simplicity, secret flow, and direct provider bypasses.

**Commit:** `samples: add DigitalBrain package quickstart`

**Execution record (2026-07-19):** Baseline HEAD was `5559dd1ffc306b428830af01534a5512058344b1` on `master`. The operator explicitly deferred publishing, so the task was completed as a Development-only package-consumer quickstart and the `aspire publish` gate was removed without publishing or deployment. Five small consumer projects restore only the six locally packed `DigitalBrain.*` packages, keep every framework reference behind `PackageReference`, expose the full typed DigitalBrain AppHost model, create the explicit owner session before restricted client resolution, and provide deterministic interactive console commands plus Development-only Dashboard and DevUI hosts. The two-command runner streams provider prompts, starts the AppHost detached, probes and stops the explicit console resource before launching the real console in the foreground, restores the caller environment, and tears down partial starts by normalized AppHost path; an executable shim matrix verifies command order, environment forwarding and restoration, visible prompts, start failures, cleanup-probe failures, stop failures, combined error propagation, and orphan prevention. Restore and build are proven from a copied sample, an isolated NuGet cache, and the local package feed with package-source provenance checks. Three independent final reviews covered Aspire lifecycle behavior, package isolation, and security/secret boundaries; all actionable P0-P2 findings were fixed, and all final reviews reported no remaining P0-P2 findings. `eng/pack.ps1` produced six validated packages and six symbol packages, `eng/test-quickstart.ps1` passed 30 / 30, the documented real flow reached the kernel, exercised the explicit console probe, connected the foreground console through Orleans, accepted `/exit`, and stopped cleanly with synthetic credentials, the Release solution build completed with zero warnings or errors, and the exact root checkpoint passed DigitalBrain.Tests 245 / 245, DigitalBrain.PackageTests 30 / 30, and Brain.FeasibilityTests 13 / 13. `aspire doctor` passed 5 / 5, Aspire inspection confirmed no running AppHost, and comment, generated-artifact, and `git diff --check` scans passed.

## Task 9: Prove live framework behavior and restart recovery

**Assigned paths:**

- `tests/DigitalBrain.PackageTests/**`
- `tests/DigitalBrain.Tests/Conversations/ConversationNeuronArchitectureTests.cs`
- `eng/test-quickstart.ps1`
- `kernel/DigitalBrain.Abstractions/Conversations/ConversationContracts.cs`
- `samples/DigitalBrain.Quickstart/**`

**Automated proof:**

- [x] Start the quickstart AppHost with a test-only HTTP provider resource, explicit OpenAI/Anthropic endpoint overrides, and synthetic secret parameters.
- [x] Prove that the normal privileged kernel provider factories and official SDK adapters call that HTTP resource; do not replace `IChatClient` or use ambient credentials.
- [x] Wait for Azurite, Orleans, kernel, console test driver, dashboard, and DevUI to become healthy.
- [x] Send a durable turn through `DigitalBrain.Client`.
- [x] Verify the selected role reached the correct provider adapter.
- [x] Restart the kernel.
- [x] Resume the same conversation and verify durable state.
- [x] Verify Orleans Dashboard endpoint.
- [x] Verify DevUI entity discovery and one role-backed turn.
- [x] Capture Aspire resource states, traces, and logs.
- [x] Stop all resources and prove no orphaned processes.

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

**Execution record (2026-07-19):** Baseline HEAD was `2579665890201fa88c12acf61147e4c293354e70` on `master`. The assigned paths were amended before commit to include the public turn-identity contract and its journal-context regression test after the real restart proof exposed a framework recovery defect. Static TDD began with three failing package tests for the absent live AppHost model, controlled provider/driver path, and supported live entry gate. The Development-only controlled provider accepts exact synthetic OpenAI and Anthropic credentials, records only hashes and non-secret routing facts, and is reached through the normal privileged kernel factories and official SDK adapters. The fixed-port console test driver uses the restricted Aspire client, an explicit owner session, typed balanced role facade, durable turn identity, health checks, and OTLP export; Dashboard, DevUI, and kernel telemetry remain on their normal package paths. The live gate restores through a fresh isolated NuGet cache, forces a non-incremental package-consumer build, waits for all dependencies, commits one balanced turn, restarts only the kernel, reads the same conversation, proves replay idempotence, continues with a second turn, invokes the fast DevUI agent, captures redacted resource/log/trace evidence, and verifies process, port, container, AppHost, environment, cache, and `aspire.config.json` cleanup. Systematic debugging fixed the harness's PowerShell automatic-variable hash collision, process-stop timing, non-enumerated `Invoke-RestMethod` array shape, optional Aspire resource fields, and same-version local package-cache reuse. The restart itself reproduced `ArgumentException: A non-empty turn id is required` during official JSON journal replay; Microsoft Learn confirmed that immutable structs require `[JsonConstructor]` to select a parameterized constructor, and the minimum validated-constructor annotation made the new round-trip regression and real restart pass without weakening the public boundary. The exact owning gate passed DigitalBrain.Tests 246 / 246; package tests passed 33 / 33. `.\eng\test-quickstart.ps1 -Live` passed all 33 package tests plus controlled-provider, restart, Dashboard, DevUI, log, trace, secret-redaction, and teardown proofs; optional real OpenAI and Anthropic turns were explicitly skipped because credentials were absent. The final exact root checkpoint passed DigitalBrain.Tests 246 / 246, DigitalBrain.PackageTests 33 / 33, and Brain.FeasibilityTests 13 / 13 after one unchanged timing-sensitive OpenAI cancellation-test timeout passed both in isolation and on the exact rerun. The Release solution build completed with zero warnings and errors; `aspire doctor` passed 5 / 5; `aspire ps` reported no running AppHost; evidence contained 31 resources with 15 token-free URLs, three authorized provider requests, one `chat claude-sonnet-4-5` kernel trace carrying `gen_ai.operation.name=chat`, and three `digitalbrain.conversation.submit` driver traces; no isolated cache or added source comment remained. CodeGraph inspection bounded the journal fix to the turn identity, official source-generated JSON context, durable intent dictionary, coordinator, and architecture tests. The initial C# review reported no actionable P0-P2 findings. The complete-diff review found three P2 gaps in URL redaction, restart-exit proof, and telemetry relevance; all were fixed under new failing static assertions, and the focused re-review reported no actionable P0-P2 findings. `git diff --check` passed. No package was published, deployed, or pushed.

## Task 10: Remove superseded active architecture

**Assigned paths:**

- `src/**`
- `edge/**`
- superseded active projects identified by the approved durable-neuron demolition tasks
- any reintroduced `modules/AI/**` throw-stub project
- `Brain.slnx`
- architecture tests

**Assigned-path amendment (2026-07-19):** The controlling durable-neuron demolition inventory also assigns `behaviors/**`, `modules/Brain.Modules.Behaviors/**`, `modules/Brain.Modules.Web/**`, `tests/Brain.Tests/**`, `tests/Brain.FeasibilityTests/AgentFramework/**`, `tests/Brain.FeasibilityTests/TypedReferences/**`, `tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj`, and `Directory.Packages.props`. These paths are required to remove the rejected trees and their now-unused dependencies completely.

**Red tests:**

- the active solution contains only the approved DigitalBrain framework, integrations, hosts, modules, tests, and samples.
- forbidden old generic invocation and MCP surfaces are absent.
- no active project references `sources/**`.
- no duplicate `Brain.*` foundation package competes with `DigitalBrain.*`.
- no throw-only AI hosting project or `ConfigurationBoundChatClient` remains.

**Implementation:**

- [x] Follow the deletion inventory from the approved durable-neuron plan.
- [x] Delete superseded code rather than adapting it.
- [x] Confirm the failed `modules/AI` experiment was deleted or its approved contracts were absorbed into the public DigitalBrain packages; never retain both.
- [x] Preserve only explicitly approved provider modules and behaviors.
- [x] Update topology tests.

**Gates:**

```powershell
dotnet test .\Brain.slnx -c Release
rg -n "DispatchProxy|InvokeMcpTool|\\bAsk\\b|Kind routing|JsonElement" kernel integrations hosts modules samples tests
git diff --check
```

**Review focus:** deletion completeness, hidden compatibility paths, duplicate package identity, generic routing, and MCP duplication.

**Commit:** `refactor: remove superseded DigitalBrain architecture`

**Execution record (2026-07-19):** Baseline HEAD was `aad78b2f0b28271cef0a2b52a359aa9e0d618582` on `master`. Two topology tests first failed against the orphaned project set and source trees. The approved demolition removed exactly 149 expected files and 12,510 lines, with no missing or extra deletion: the duplicate nine-project `src/**` architecture, both generic MCP edges, rejected behavior/Web modules and scripts, the excluded `Brain.Tests` project, and the Agent Framework and DispatchProxy-era feasibility remnants. No tracked `modules/AI/**` implementation remained; the public kernel AI contracts and provider factories already absorb the approved typed model behavior. The exact 15-project `Brain.slnx` graph and six package-only quickstart projects were preserved, as were the approved Google/Salesforce modules and four durable journaling feasibility tests. New guards enforce the exact repository project set, cross-platform approved project-reference closure, absence of superseded source roots, and no `Compile` or `Import` path into `sources/**` from active project, props, or targets files, including unresolved MSBuild-property paths. They also enforce exact root central-package usage, rejected routing/MCP symbols, provider and client reflection surfaces, aliased/cast/indexed/multiline string-contract selectors without rejecting typed contract enums, and keyed chat/embedding provider services across every active root plus explicit or type-inferred generic helper call sites while allowing only the controlled test-provider `JsonElement` wire boundary and legitimate keyed Azure/Orleans state infrastructure. Removing the obsolete workflow test allowed 37 unused root package versions to be pruned; the remaining 44 central versions match the 44 effective active solution references exactly. `dotnet restore Brain.slnx` passed. The focused architecture gate passed 27 / 27, the full owning suite passed 271 / 271, and the final Release solution gate and exact root checkpoint both passed DigitalBrain.Tests 271 / 271, DigitalBrain.PackageTests 33 / 33, and Brain.FeasibilityTests 4 / 4 with zero skips. The exact documented broad forbidden-surface scan reported seven negative assertions in retained tests plus the two approved `JsonElement` parsers in the Development-only controlled HTTP provider; the active-product-root variant reported only those two approved boundary parsers. The older durable-plan literal scan likewise reported only ten retained test uses or negative assertions for Orleans' volatile journal test provider plus two negative `DispatchProxy` assertions, so those test-only exceptions are recorded explicitly rather than claimed as a no-match. `git diff --check` passed. Read-only audits found the two residual feasibility subtrees and progressively hardened reflection, portability, path-import, keyed-provider, and contract-routing guards; all findings were fixed, and the independent package/topology review plus final focused C# re-review reported no remaining actionable P0-P2 findings. No package was published, deployed, pushed, or otherwise released.

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
