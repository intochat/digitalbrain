# IntoChat test cleanup and Marketplace extraction

Status: approved by the user on 2026-10-01. The attached user request is the authoritative scope.

## Goal and constraints

IntoChat tests cover product composition, wiring, and full-stack journeys. Module behavior belongs in the owning module's test projects. Apps owns app authoring and publishing orchestration; IntoChat owns its embedded app content, the `intochat` publisher identity, and host auth, CORS, and configuration.

Execute two phases as separate commit series. Preserve existing uncommitted master-key work, including the modified `MasterKeyHostingFacts.cs`. Stage only changes belonging to this request. Trust the supplied audit; re-check changed files and target test idioms, rather than repeat the audit.

## Phase 1: test ownership

Delete HygieneFacts, HostedDeploymentFacts, OtlpParserFacts and its linked compile item, and the trivial Mcp tests. Move only the concurrent 64-authorize charge case to Compute; delete the allowance cases already covered there. Move ComputeUsageFacts to Assistant. Move MasterKeyHostingFacts, preserving its current edits, to a Kernel hosting test home with the necessary hosting reference.

Keep only ModuleOptionsFacts' reflection sweep, locating it with composition tests if its module assembly coverage depends on the product host. Delete the duplicate generic round trip and arbitrary product module count.

Move LocalAppNeuronFacts to Flutter or Files according to the exercised contracts. Delete thin ComputeLimits read-after-write coverage. Move ShellPersistence's HTTP durability and stale-write fact to Flutter; retain its browser journey. Move CrossWorkspace's slider isolation fact to Flutter; retain the cross-principal HTTP 403 journey.

Replace packaging string lists with one composition test. Parse `DigitalBrain__Modules__N` entries in Dockerfile and XML environment entries in Container.pubxml. Reject duplicate indices, empty lists, differing sets, developer-only modules, and unresolvable assembly-qualified types. Derive developer-only types from the existing composition profile where possible. Keep EveryProjectReferenceResolvesToAnExistingFile; delete completed Flutter and MCP migration guards and HostedSdkModuleFacts.

Merge overlapping AppDraft sandbox-unavailable tests and make ScriptedTestRunner results per instance. Share E2E trace helpers and reuse People.SignedIn for cookie clients. Move usage pagination boundary and foreign cursor behavior to Assistant/Compute. Move secret ownership behavior to Identity and authoring host composition behavior to Microsoft.CSharp E2E. Retain receipt-in-stream and browser journeys.

Every retained test must exercise behavior. Use target-module test hosts, real grains, sentence-shaped names, and TestContext.Current.CancellationToken. Remove IntoChat Unit references to Mcp, Kernel.Aspire.Hosting, and Compute once their consumers move.

## Phase 2: module boundaries

### Sandbox contract

Introduce `IScriptSandbox` in Apps.Contracts. It exposes availability, contract discovery, and source checking through provider-neutral DTOs. CSharp implements and registers it using its existing discovery and compiler services. Apps has no dependency on CSharpToolService or CSharpAgentTools. Both `/session/capabilities` and AssistantModule's discovered concrete capability check consume the contract. Missing sandbox composition remains a supported host mode.

Use one Apps-owned availability policy for draft builds and revision verification. A C# runtime or revision with tests requires the sandbox; other content keeps its existing behavior. Preserve the existing error semantics.

### Draft contracts and implementation

Move IAppDraft, IAppDrafts, AppDraftView, AppDraftState, AppDraftStatus, AppDraftAttempt, index entries/state, change signals, and AppSpecView into Apps.Contracts as appropriate for their wire/persistence roles. Preserve Orleans field IDs, grain keys, signal provenance, and JSON field shapes. Pin AppDraftStatus to Empty=0, Drafted=1, Building=2, Published=3, Failed=4. Mirror any actual wire changes in Flutter in the same commit; a namespace move alone must not change JSON.

Move neurons, BuilderTools, AgentPrompts, MarketplaceService, and draft/spec/verify/discussion routes into Apps. Register through AppsModule and map through its endpoint configuration so IntoChat does not manually map a second Marketplace feature. Preserve `/packages` routes, PackageRouteGuard, principal ownership, and brain membership enforcement.

Move GroupChatRuntime and PromptRuntime into Apps runtimes. Extend runtime metadata to describe authoring requirements: runtime name, required files, settings conventions, and invocation addressing. Build authoring prompts from registered descriptions. Put the C# description with its provider so sandbox paths are described by the component that implements them. Share group-chat key construction with discussion retrieval instead of duplicating it in prompts.

### Shipped apps

Move generic resource loading and ShippedAppPublisher into Apps. Introduce IShippedAppSource and an embedded-resource implementation registered by `AddShippedApps(assembly, "IntoChat.ShippedApps/", publisher: "intochat")`. Preserve deterministic content normalization, commit/verify/publish ordering, startup controls, and cancellation behavior.

Remove ShippedPublisher and the `intochat/settings` identity from Assistant. Resolve the built-in settings package from host-provided configuration/registration; IntoChat supplies its package identity. Missing configuration should produce a deliberate unavailable response, not construct a fabricated package identity. Avoid an Apps-to-Assistant dependency cycle when relocating routes that currently use InstalledPackages.

Normalize authoring keys to `DigitalBrain:Apps:*`; update configuration consumers and documentation together. Update CLAUDE.md's Marketplace reference and known-failure note once phase 1 verification proves those failures are gone.

## Ordering and verification

Phase 1 commits group deletion/packaging, module test moves, and shared-helper cleanup. Phase 2 commits follow the user's six ordered steps: sandbox contract, draft contracts, implementation relocation, runtime descriptions, shipped-source abstraction, and availability/configuration consolidation. Each intermediate commit must build and pass its affected project tests.

Run dotnet test per affected project, never the solution. Include IntoChat Unit and all module projects receiving tests. Do not run the 18-minute IntoChat E2E suite; compile it to validate moved/shared support code. After each phase run Aspire from IntoChat/AppHost until all resources are Healthy. Flutter changes require flutter analyze and flutter test in the shell directory. Finish with a code review covering ownership, dependency direction, persisted IDs, wire compatibility, authorization, sandbox availability, and parallel test isolation.

## Acceptance

- The three pre-existing packaging failures are replaced by one passing packaging test.
- IntoChat Unit has no direct Mcp, Kernel.Aspire.Hosting, or Compute reference.
- IntoChat contains no Marketplace implementation or concrete sandbox capability checks.
- Apps authoring works through module registration, with and without a sandbox composed.
- Draft state and Flutter wire compatibility are preserved.
- No module embeds the IntoChat publisher identity.
- Both commit series have recorded per-project verification and Healthy-resource smoke evidence.
