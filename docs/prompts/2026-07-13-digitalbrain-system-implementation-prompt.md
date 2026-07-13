# DigitalBrain System Implementation Prompt

Use this prompt in a fresh Codex task rooted at `E:\brain`.

---

You are the lead implementation agent for DigitalBrain. Implement the approved programmable Features architecture completely and production-quality at the proof scope.

## Source of truth

Read these files fully before proposing or changing anything:

1. `E:\brain\AGENTS.md`
2. `E:\brain\CLAUDE.md`
3. `E:\brain\docs\superpowers\specs\2026-07-13-digitalbrain-programmable-features-design-v3.md`
4. `E:\brain\docs\architecture\digitalbrain-architecture-overview.html`

The v3 architecture specification is authoritative. The visual is explanatory. If they appear to differ, follow v3.

Inspect the current repository, solution graph, tests, deployment, AppHost model, working tree, and recent commits before planning. Preserve unrelated user changes. Use CodeGraph first where required by `CLAUDE.md`, then `rg` for textual confirmation.

## Goal

Deliver the v3 target architecture, including:

- shipped Integration packages for Google and Salesforce with separate Orleans-free Contracts packages;
- source-first Features using the same C#, Gherkin, BDD, build, release, approval, grant, and installation path whether shipped with the repository or authored at runtime;
- transient isolated FeatureBuilder;
- hot-loading FeatureHost using collectible `AssemblyLoadContext` instances;
- `FeatureHubGrain` and `FeatureInstallationGrain` as the only new grain types;
- durable inbox, lease, fence, retry, park, schedule, commit, intent, and idempotency behavior exactly as specified;
- one RuntimeHost capability dispatcher shared by FeatureHost calls and retained INO code;
- preservation of the signed external-effect authority, approval evidence, worker correctness, connector verification, conversation rail, Flutter chat, and RFW surface rail;
- lexical bounded Memory backed by the `memoryfacts` Azure Table;
- the target 22 platform projects plus the first shipped Email Summarizer implementation and BDD test projects;
- all deletion, production-code, public-API, test, build-time, and runtime acceptance gates from v3.

Do not substitute a generic plug-in platform, dynamic provider loading, arbitrary NuGet restore, a custom package format, vector Memory, or Feature state migrations.

## Non-negotiable engineering rules

### No comments

Do not add comments to source or configuration code. This includes line comments, block comments, XML documentation comments, HTML comments, JavaScript comments, CSS comments, and YAML comments. Make names, types, boundaries, and tests express the intent.

When materially editing a file that already contains obsolete commentary, remove it if doing so is safe. Do not create a noisy repository-wide comment rewrite inside an unrelated functional slice. The planned comment-deletion pass remains separate and must have its own verification.

### Deletion is authorized

Delete proven-dead code aggressively when it is within v3's deletion ledger. Remove implementation, tests, registrations, package references, configuration, storage resources, project references, and superseded documentation together. Do not leave compatibility wrappers, forwarding abstractions, feature flags, deprecated aliases, dead branches, or commented-out code.

Before deleting, prove the final live caller has moved or does not exist. Do not delete the live Flutter/RFW rail, conversation workflow, effect authority, worker lease/fence/reminder/outbox correctness, connector verification, or Pulumi deployment.

Do not ask for repeated approval to perform deletions explicitly authorized by v3 after final-caller proof. Stop and ask only if evidence contradicts the specification or a deletion would remove a live capability not accounted for by v3.

### Simplicity and boundaries

- Prefer the smallest design that satisfies the approved behavior.
- Use constructor injection and explicit interfaces. No service locator, ambient container access, reflection-based capability discovery, or static mutable service state.
- Keep contracts narrow and dependency-light. Provider Contracts must contain no Orleans, Aspire, ASP.NET, provider SDK, credential, filesystem, environment, process, or networking dependency.
- Stable capability and event IDs are explicit data, never derived from CLR names.
- Avoid generic buses, registries, factories, or abstraction layers unless v3 requires them and at least two concrete callers justify them now.
- Keep files and types focused. Split code by authority and reason to change, not by arbitrary technical layer.
- Use nullable reference types, analyzers, warnings-as-errors, deterministic builds, and central package management already configured by the repository.

## Current framework baseline and compatibility gate

At prompt creation time the repository uses:

- `.NET SDK 11.0.100-preview.5` and `net11.0` targets;
- Aspire AppHost and CLI `13.4.6` stable;
- Orleans packages split between `10.2.0` stable and `10.2.1-preview.1`, with journaling alpha packages scheduled for deletion.

Treat these as observations, not eternal truth. At the start of implementation:

1. run `dotnet --info`, `aspire --version`, and `aspire doctor --non-interactive`;
2. use Context7 for current .NET, Orleans, Aspire, Reqnroll, and Azure SDK documentation as required by `AGENTS.md`;
3. if Context7 is unavailable, use only official Microsoft, Aspire, Reqnroll, and package-source documentation;
4. inspect the actual dependency graph and published compatibility before changing versions;
5. choose one coherent Orleans package family compatible with the selected SDK and Aspire version;
6. prefer the latest compatible stable packages, but do not downgrade the repository's deliberate SDK target or adopt previews merely for novelty;
7. if a preview is genuinely required, pin one coherent preview set and record the compatibility evidence in the implementation plan;
8. never mix Orleans core, client, server, serialization, persistence, reminder, clustering, and testing versions accidentally.

Use `dotnet-inspect` or official API documentation before relying on an unfamiliar .NET or NuGet API. Use `aspire docs search` and `aspire docs api search --language csharp` before editing unfamiliar AppHost APIs.

## Current .NET practices

- Use the generic host, built-in dependency injection, options pattern, and `ValidateOnStart` for required configuration.
- Prefer immutable configuration and request records where they improve correctness, without turning every internal type into public API.
- Pass `CancellationToken` through asynchronous boundaries and respect deadlines. Do not use sync-over-async, fire-and-forget tasks, blocking waits, or unowned background tasks.
- Use `TimeProvider` for code that depends on time and deterministic tests.
- Use `System.Text.Json` with source generation or explicit bounded DTOs where serialization is under application control.
- Use typed or named clients and the standard resilience pipeline only at real remote-I/O boundaries. Retries must respect operation semantics and idempotency.
- Validate all untrusted or owner-authored input at the boundary. Enforce size, count, deadline, path, package, and capability allowlists server-side.
- Keep secrets in Integration Runtime and host configuration. Never log credentials, tokens, message bodies, Memory text, tags, or Feature payloads.
- Emit structured logs, `ActivitySource` traces, metrics, health checks, correlation IDs, causation IDs, installation IDs, input IDs, and logical operation keys without leaking payloads.
- Make disposal and ownership explicit for processes, streams, cancellation sources, `AssemblyLoadContext`, and any temporary filesystem content.

## Orleans practices

- Use Aspire's Orleans resource model and the parameterless host integration where it supplies cluster configuration correctly.
- Keep grain interfaces and serializable contracts explicit. Use Orleans source-generated serialization with stable field IDs. Do not use reflection serializers for core durable contracts.
- Keep grain state private to its owning grain. Persist intentionally with awaited writes. A grain method must not report success before the required state write completes.
- Treat Orleans messages as at-most-once by default and potentially duplicated whenever application or runtime retries are introduced. Build application-level durable inboxes and idempotency exactly as v3 specifies.
- Do not claim exactly-once handler execution. Test duplicates, timeouts, ambiguous failures, stale leases, and replay.
- Preserve single-threaded grain reasoning. Do not add reentrancy or interleaving unless a proved requirement and tests justify it.
- Use persistent reminders only as durable wake-up hints. Persist the next logical schedule occurrence yourself because individual missed reminder ticks are not stored.
- Use `RegisterGrainTimer`, not obsolete timer APIs, for activation-local periodic work.
- Keep reminder periods appropriate for durable low-frequency work. Do not use reminders as a high-frequency queue.
- Make every lease and claim carry a fencing token that is validated at commit.
- Keep one atomic `FeatureRunCommit` write inside `FeatureInstallationGrain` for Feature state, completed input, acknowledgment, intents, and the completion ledger.
- Apply committed intents idempotently using `FeatureInstallationId + InputId + LogicalOperationKey`.
- Keep grain calls bounded, asynchronous, cancellation-aware, observable, and free of blocking provider I/O where an Integration handler or worker owns that I/O better.
- Use the in-process or testing-host facilities appropriate to the installed Orleans version for deterministic grain tests. Do not mock away Orleans behavior that the test is intended to verify.
- Register application parts and serializers at host startup. Do not dynamically load new grain interfaces or grain classes from Feature releases.

## Aspire practices

- Keep AppHost declarative: resources, references, dependencies, replicas, health, and lifecycle only. Business logic belongs in normal projects.
- Model Orleans once with `AddOrleans`, configure Azure Table clustering, named Blob grain storage, and reminders through the Aspire Orleans resource, then reference it as a silo from RuntimeHost and `AsClient()` from MCP/UI Edge and FeatureHost.
- Use resource references and keyed clients instead of manually copying connection strings.
- Use `.WaitFor()` only for real readiness dependencies and expose meaningful health checks.
- Keep common OpenTelemetry, health, service discovery, and HTTP resilience wiring in ServiceDefaults without hiding business registration there.
- Use Azurite through the single approved storage account and seven logical resources. A Docker volume is sufficient for normal local/dev persistence; data loss remains acceptable.
- Keep the local steady topology at three RuntimeHost replicas, one MCP/UI Edge, and one FeatureHost. FeatureBuilder is transient.
- Start AppHost with `aspire start`, using `--isolated` when shared local state is risky. Never use `dotnet run` for AppHost.
- Use `aspire wait <resource>` instead of manual polling. Use `aspire describe`, `aspire logs`, `aspire otel logs`, and `aspire otel traces` to diagnose the modeled application before changing code.
- Rebuild or restart only affected resources when possible. Integration composition changes may restart hosts; Feature release changes must not.
- Add AppHost tests with `Aspire.Hosting.Testing` and share the expensive distributed application lifecycle across tests where safe.

## Feature loading and build safety

- FeatureBuilder consumes a bounded source snapshot and an offline allowlisted feed, produces the implementation assembly, derived manifest, BDD result, source reference, and digest, then exits.
- It has no credentials, no Orleans membership, no platform-storage connection, no unrestricted network, and no arbitrary package restore.
- Enforce the v3 build and execution budgets as real timeouts and test them.
- FeatureHost loads only the release implementation into a collectible `AssemblyLoadContext`.
- Load `DigitalBrain.Features.Sdk` and Integration Contracts in the default context so type identity is shared.
- Use deterministic dependency resolution, avoid recursive load callbacks, handle concurrent loading, retain no static or event-handler roots into old contexts, and verify unload with weak references and forced collection in tests.
- Stage and validate a new release before switching new work. Drain the old release, unload it, and recycle the one-replica proof host if unload fails.
- Runtime-authored code is trusted owner-reviewed code with constrained capability authority. Do not describe `AssemblyLoadContext` or the builder process as a hostile-code sandbox.

## Security and authority

- Replace internal `TenantId` and `WorkspaceId` with `BrainOwnerId`, `ActorId`, `ProviderConnectionId`, and `SessionId` as specified. Local/dev data can be wiped; do not build migration machinery.
- Bind every grant to owner, installation, exact release digest, capability ID/version, provider connection, constraints, and grant revision.
- Validate grants inside RuntimeHost for every capability operation. FeatureHost caches cannot grant authority.
- Revocation and pause must take effect on the next operation.
- External mutation Features start propose-only. Observable external changes always pass through the retained signed-plan authority and connector verifier.
- Audit decisions and identifiers, never sensitive payloads.

## Testing and execution method

Use test-driven development for every functional slice:

1. write the smallest owning-project failing test;
2. run that project and observe the expected failure;
3. make the smallest implementation change;
4. rerun the same project to green;
5. run affected suites;
6. run the exact root suite before declaring the slice complete when the repository is in an integrable state.

Do not use `--filter`. Revise the root-only wording in `CLAUDE.md` as authorized by v3 so project-level red-green commands are allowed while the exact root command remains mandatory before completion.

The baseline root command is:

```powershell
dotnet test --logger "console;verbosity=minimal"
```

The recorded baseline is 408 passed, 0 failed, 0 skipped in approximately 34.3 seconds. Re-establish the baseline fresh before the first code slice and report any difference instead of assuming it.

Also run the relevant Flutter, AppHost, package-boundary, architecture, build-time, unload, restart, duplicate-delivery, authorization, backpressure, and end-to-end scenarios from v3. Use exact evidence before claiming a test, build, behavior, deletion, budget, or acceptance gate passes.

## Required implementation workflow

1. Create an isolated `codex/` worktree or branch without disturbing the owner's dirty generated Flutter files.
2. Inspect and record the fresh repository, package, API-surface, line-count, project-count, storage-resource, process-topology, and test baselines.
3. Produce a detailed implementation plan derived from v3, organized into small vertical slices with explicit deletion dependencies and review checkpoints.
4. Self-review the plan for big-bang steps, hidden migrations, compatibility shims, speculative abstractions, and missing failure tests.
5. Execute the plan rather than stopping after planning unless a genuine authority or architecture contradiction blocks progress.
6. Keep at most one slice in progress. Finish its tests and cleanup before opening another cross-cutting slice.
7. Re-check live callers immediately before every deletion.
8. Keep the solution buildable and the current product path usable at each checkpoint.
9. Measure production C# and public API after every structural phase. Do not defer the reduction gates to the end.
10. Use independent review after meaningful milestones and before final integration.
11. At completion, run all acceptance scenarios, exact root tests, Flutter tests, Aspire/AppHost tests, architecture tests, size gates, API gates, and a clean diff review.
12. Commit intentionally scoped changes. Do not stage or overwrite unrelated user modifications.

## Stop conditions

Stop and ask the owner only when:

- current repository evidence contradicts an approved v3 invariant;
- a required dependency or API has no compatible supported version;
- a deletion would remove a live product capability not represented in v3;
- credentials, external approval, or new authority are required;
- the work would need to expand into production durability, hostile-code sandboxing, arbitrary package restore, vector Memory, dynamic provider loading, or state migration.

Ordinary implementation choices within v3, including deletion of listed trash after final-caller proof, do not require another approval round.

## Completion report

Lead with the delivered outcome. Include:

- architecture slices implemented;
- major deletions and preserved rails;
- final project, production C#, and public API counts against gates;
- exact test commands and pass/fail counts;
- build and runtime budget evidence;
- Aspire topology and health evidence;
- Feature hot-install, update, rollback, restart, duplicate, revocation, backpressure, Memory, and external-effect evidence;
- any intentionally deferred non-goals;
- commit or PR details.

Do not claim completion while any v3 acceptance gate remains unverified.

## Official references to verify against

- Orleans overview: https://learn.microsoft.com/en-us/dotnet/orleans/overview
- Orleans delivery guarantees: https://learn.microsoft.com/en-us/dotnet/orleans/implementation/messaging-delivery-guarantees
- Orleans persistence: https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-persistence/
- Orleans timers and reminders: https://learn.microsoft.com/en-us/dotnet/orleans/grains/timers-and-reminders
- Orleans code generation: https://learn.microsoft.com/en-us/dotnet/orleans/grains/code-generation
- Orleans server configuration: https://learn.microsoft.com/en-us/dotnet/orleans/host/configuration-guide/server-configuration
- Orleans and Aspire: https://learn.microsoft.com/en-us/dotnet/orleans/host/aspire-integration
- Aspire Orleans integration: https://learn.microsoft.com/en-us/dotnet/aspire/frameworks/orleans
- Aspire integrations: https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/integrations-overview
- Aspire custom integrations: https://learn.microsoft.com/en-us/dotnet/aspire/extensibility/custom-integration
- Aspire AppHost testing: https://learn.microsoft.com/en-us/dotnet/aspire/testing/manage-app-host
- .NET dependency injection: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/overview
- .NET options: https://learn.microsoft.com/en-us/dotnet/core/extensions/options
- .NET OpenTelemetry: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel
- `AssemblyLoadContext`: https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext
- Assembly unloadability: https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability

Begin now by reading the source-of-truth files, checking the dirty worktree and toolchain, invoking the required planning workflow, and then implementing the first approved vertical slice.
