# Refactor Plan: Program.cs + Orleans Journaling (DigitalBrain.Kernel)

**Date**: 2026-07-08  
**Context**: Follow-up to PR#9 review (Sanitize hack, journal mutation concerns) + explicit request to nuke trash in Program.cs (441 LOC today) and question custom JournalJson.  
**Mandate**: <=50 LOC in `src/DigitalBrain.Kernel/Program.cs`. Follow CLAUDE.md: Elon's 5 steps in order, Context7 + Aspire MCP for all APIs, aspire doctor + list_resources before/after, relative paths only, no vacuous summaries, latest packages via central props, post-change build+test+doctor+inspections. Delete first (>10% net, aim 80%+ here). Self-explanatory names. No C:\ paths.

## Pre-Change Ritual (Completed)
- `aspire doctor`: all 5 checks passed (CLI 13.4.6, AppHost 13.4.6, .NET 11 preview, dev-certs, Docker).
- `aspire__list_resources`: captured (dashboard, storage/clustering, ollama models, flutter-ui, parameters, multiple kernel replicas referenced).
- Context7: resolved `/dotnet/orleans` + `/websites/learn_microsoft_en-us_dotnet_orleans`. Queried journaling examples, AddAzureBlobJournalStorage + UseJsonJournalFormat, DurableGrain/IDurableList patterns, polymorphism for JSON format, streams vs journaling.
- MS Learn + Context7 snippets cross-checked (no "Azure queues for journaling"; blobs for journal log, separate from streams).
- Full reads: Program.cs (441 lines), PrototypeJournals.cs, JournalJson.cs, DigitalBrain.Kernel.csproj (journaling alpha packages), Aspire builder (JournalBlobs only referenced, no high-level WithJournaling), KernelServices.cs, etc.
- Grep + measurements confirmed duplication and inline bloat.

## Elon's 5 Steps (Applied in Order)

### 1. Make Requirements Less Dumb (Question + Trace)
- "Program.cs must be tiny" is good hygiene, but root req is: **one obvious place to see the three startup paths** (pure `dotnet run` local, Aspire Azurite, real ACA managed-identity), without god-file mixing web hosting + Orleans silo + business endpoints + 3 auth modes.
- "Custom JournalJson is wrong, Orleans provides Azure message queues?" — **False premise**. 
  - Journaling (Microsoft.Orleans.Journaling + .AzureStorage) = durable append-only log for `IDurableList<T>` / `IDurableDictionary` / `IDurableValue` inside `DurableGrain`. Replayed on activate. Used here for dual in/out synapse journals → `GetTimeline*` etc. Matches official shopping-cart + version examples exactly.
  - Not queues. Azure Storage Queues would be for external messaging (Orleans has separate streaming providers).
  - Streams (already used: `AddMemoryStreams("DigitalBrainTimeline")`) = pub/sub broadcast. Different from durable replay journals.
  - `UseJsonJournalFormat` + custom resolver is **the documented path** when you need STJ + polymorphism (large Synapse hierarchy + `Dictionary<string,object?>` props). Source-gen `JsonSerializerContext` is preferred for trimming, but runtime `AddTypeInfoResolver` + discovery is pragmatic here because subtypes come from Core + integrations + future packs.
- Trace: bloat from incremental addition of auth, Ino, connectors, uploads, sync, security, without ruthless extraction. Detection of `isAspireHosted` via `ConnectionStrings__*` env + `DigitalBrain:Storage:AccountName` for MI split is duplicated logic.
- Challenge from prior review: journal payloads containing open `object?` (Signal.Props etc.) + STJ roundtrip → JsonElement → Orleans copier failure on grain returns (`IReadOnlyList<Synapse>`). This is why Sanitize existed. Program.cs / JournalJson config contributes to the shape; fix belongs in contracts + journal boundary, not just startup.
- Dumb req to kill: "support every historical path in one 400-line file with novel comments."

Resulting real req: thin Program.cs that composes clean extensions. Journals stay on blobs (correct), improve registration + polymorphism setup. Delete coupling to bad open-dict shape where possible (cross-ref prior review).

### 2. Delete First (Target >10% Net Reduction — Achieved Massively)
- Delete from `Program.cs`: 441 → target <50 LOC (delete 85-90%+).
  - All long explanatory comments (history, Task numbers, "why we do direct client", RBAC lag notes, etc.).
  - Inline `/upload` handler (~80 lines of business logic + temp file handling).
  - OAuth callback handler.
  - Local `static NoTracingTableOptions` / `NoTracingBlobOptions`.
  - Repeated `isAspireHosted` + `useManagedIdentity` + credential/Uri building blocks (centralize or delete duplication).
  - Hard-coded cors origins + port logic.
  - Verbose service registration blocks that belong in feature extensions (move the rest).
  - Dupe `ConfigureServices` for NeuronJournals / handlers inside UseOrleans.
- Delete / move trash in kernel overall:
  - Duplicated BlobServiceClient construction for pack-config vs journal/grainstate.
  - Scattered env detection (extract once).
  - Over-commenting in PrototypeJournals.cs and JournalJson.cs.
  - (Cross-ref review) The Sanitize mutation in Neuron is *symptom*; we don't touch it here but plan calls it out for follow-up.
- Net reduction in startup surface: huge. No functionality loss — just relocation to self-explanatory named extensions.
- If something is only for one path, make the extension take a clear "mode" or rely on DI presence (Aspire clients).

### 3. Simplify or Optimize (What Remains)
- **Program.cs skeleton (aim 30-45 lines)**:
  ```csharp
  // src/DigitalBrain.Kernel/Program.cs
  using DigitalBrain.Kernel; // + minimal

  var builder = WebApplication.CreateBuilder(args);

  builder.AddServiceDefaults();
  builder.AddDigitalBrainKernel();           // services + UseOrleans + journals (decides paths internally)
  // No more 200 lines of ifs here.

  var app = builder.Build();
  app.MapDigitalBrain();                     // cors, grpc, static (if any), endpoints
  app.Run();
  ```
- Extract:
  - Kestrel + static files + cors + grpc web → `WebHostExtensions.cs` or `DigitalBrainWebExtensions.MapDigitalBrain(app)`.
  - All the "AddSingleton buses, health, Ino, connectors, factories, security, pack config, seeders" → `DigitalBrainKernelExtensions.AddDigitalBrainKernelServices(...)` (or split Add* per concern, already partial in KernelServices.cs).
  - The giant `UseOrleans` lambda → `OrleansExtensions.ConfigureDigitalBrainOrleans(this ISiloBuilder, IConfiguration, ...)` (or `siloBuilder.AddDigitalBrainJournals(...)` + clustering + storage + streams).
  - Journal wiring: prefer Aspire-registered keyed clients (AddKeyedAzureBlobServiceClient("journal", disableTracing)) then resolve inside silo config. Fall back for pure local / MI path. One place for `AddAzureBlobJournalStorage` + `UseJsonJournalFormat(JournalJson.Configure)`.
- Journaling proper (per Context7):
  - Keep `DurableGrain` + keyed `IDurableList<Synapse>` ("in-journal"/"out-journal") + `WriteStateAsync` after mutations. Matches official patterns.
  - `PrototypeJournals.cs` already good centralization for local fast-path (InMemoryJournalForPrototype + no-op manager). Keep and call from the new extension.
  - `JournalJson.cs`: not wrong. It is the way to supply polymorphism for `UseJsonJournalFormat` when your journaled payloads are a large inheritance tree of records (Synapse + 100+ derived across assemblies). Discovery + `DefaultJsonTypeInfoResolver` + modifier is acceptable. 
    - Simplify: cache the discovered list. Make `DiscoverSynapseTypes` take an optional filter or registry. Add source-generated context for the stable core Synapses + resolver augmentation for dynamic. Keep runtime for packs.
    - Do **not** switch to queues or streams for the timelines — journals give the durable replay + `Get*Timeline` semantics needed.
  - Separate concerns: journals (durable causal log) vs. the memory streams (live broadcast). Code already does both.
- Aspire integration: AppHost only does `storage.AddBlobs("journal")` + `WithReference`. No `WithJournaling` in current Aspire.Hosting.Orleans (confirmed via search). Direct config in kernel remains correct. Simplify by registering the journal client the same way as clustering/grainstate (keyed) so UseOrleans can pull from DI instead of raw conn strings + manual clients.
- Remove the three-way branching smell: one `ConfigureDigitalBrainStorageAndJournals` that inspects what is registered in DI / config.
- Self-explanatory: `NeuronJournals`, `ConfigurePrototypeJournals`, `AddDigitalBrainJournals` etc. already decent — keep names.

### 4. Accelerate Cycle Time
- After this refactor, changes to startup are isolated in small extension files → faster compile + targeted restart.
- Use `aspire__execute_resource_command` restart on specific kernel replica (instead of full `aspire run`).
- Pre-build: `dotnet build src/DigitalBrain.Kernel.Abstractions/DigitalBrain.Kernel.Abstractions.csproj --no-restore` + same for Kernel before `dotnet test` or runs (per existing WoW).
- Background tests + poll with aspire logs/traces.
- Local dev path (no aspire) remains fast via prototype journals (already the intent of PrototypeJournals).

### 5. Automate (Last — Only After Above)
- Future: source generator or MSBuild task that emits a `SynapseJournalContext` partial with [JsonSerializable] for all known subtypes (kill most of runtime discovery).
- Dev-only target or MCP command to nuke Azurite clustering/journal tables (avoids GUID cluster-id hacks from prior review).
- Do **not** automate the current mess.

## Detailed Refactoring Plan (Specific Moves, Relative Paths)

1. **Thin `src/DigitalBrain.Kernel/Program.cs`** (final <50 lines, mostly declarative).
   - Remove all using for Azure, Orleans internals, specific features.
   - Keep only the 5-step skeleton above.
   - Delete the huge upload + oauth methods (see 5).

2. **New/updated extensions (src/DigitalBrain.Kernel/Hosting/ or flat under Kernel for simplicity)**:
   - `DigitalBrainKernelExtensions.cs` (expand the existing small one):
     - `public static IHostApplicationBuilder AddDigitalBrainKernel(this IHostApplicationBuilder builder)`
     - Inside: detect mode once (clean helper `DigitalBrainHostingMode.Detect(...)`), call `AddDigitalBrainKernelServices`, `builder.UseOrleans(s => s.ConfigureDigitalBrainOrleans(...))`.
   - `OrleansConfigurationExtensions.cs` (or `DigitalBrainOrleansExtensions.cs`):
     - `ConfigureDigitalBrainOrleans(this ISiloBuilder, ...)` 
     - Handles:
       - `ConfigureServices` for NeuronJournals + handlers.
       - if (!aspire) { UseLocalhost... AddMemory... ConfigurePrototypeJournals(); }
       - else { cluster/service id, UseAzureStorageClustering / AddAzureBlobGrainStorage / AddAzureBlobJournalStorage (resolve client from keyed DI or build), UseJsonJournalFormat(JournalJson.Configure), memory streams, signal subscriber }
     - Extract the NoTracing options + client creation to a small `StorageClientFactory` (one place).
   - `WebAndEndpointsExtensions.cs`:
     - `ConfigureDigitalBrainKestrel(...)`
     - `MapDigitalBrain(this WebApplication)` — cors, grpcweb, static files (conditional), MapGrpcService<...>, and the minimal upload/oauth if they must live in host (or better...).
   - Keep `PrototypeJournals.cs` (already extracted, good).

3. **Journal-specific cleanup**:
   - Move `JournalJson.cs` (or keep) but make `Configure` take the resolver cleanly.
   - In the orleans extension, always call `siloBuilder.AddAzureBlobJournalStorage(...) .UseJsonJournalFormat(...)` in the durable path. Use Aspire's registered `BlobServiceClient` via keyed lookup inside the options lambda when available (avoids duplicating "new BlobServiceClient(conn)").
   - Register journal client in the isAspireHosted block the same way as grainstate:
     `builder.AddKeyedAzureBlobServiceClient("journal", s => s.DisableTracing = true);`
   - Update `PrototypeJournals.ConfigurePrototypeJournals` call site only in the extension.
   - Document once (small comment): "Journals provide durable replay for synapse timelines (distinct from memory streams for broadcast)."
   - Do not delete JournalJson — it is required for the JSON format + our Synapse shape. Improve discovery (cache the type list, load only once).

4. **Move business endpoints out of Program**:
   - Extract upload logic to e.g. `src/DigitalBrain.Kernel/Endpoints/UploadEndpoints.cs` (static class with `MapUpload(this IEndpointRouteBuilder)`).
   - Same for OAuth callback (or leave thin if truly gateway).
   - This alone deletes 100+ lines from Program.

5. **Credential / mode detection**:
   - One small static `DigitalBrainEnvironment` or `HostingContext` class (or just a record) that computes `IsAspireHosted`, `UseManagedIdentity`, `StorageCredential`, URIs, conn strings once.
   - Pass it down. Delete the repeated comments.

6. **Aspire side (minor, for consistency)**:
   - In `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs`: when adding journal blobs, also ensure the service key is predictable so kernel can `AddKeyed...("journal")`.
   - No big change — Aspire still doesn't have first-class journaling wire-up.

7. **Kernel trash sweep (while here)**:
   - In `KernelServices.cs`, `DigitalBrainKernelExtensions.cs` etc.: remove any remaining novel comments.
   - Ensure all Add* methods are self-explanatory.
   - If `KernelStartupWarmupService` or similar have duplication with warmup, note for later.
   - Verify no new duplication created.

8. **Packages**: No version bumps needed unless Context7 shows newer stable journaling (current pins are the alpha needed for the feature). Use central `Directory.Packages.props`.

9. **Cross-review items**:
   - This refactor makes the journal boundary more visible (good for fixing the STJ/JsonElement + copier issue later via surrogate on journaled types or typed props).
   - Do not re-introduce broad mutation in journal paths.

## File Moves / Structure After
```
src/DigitalBrain.Kernel/
  Program.cs                          (thin, <50 LOC)
  DigitalBrainKernelExtensions.cs     (expanded)
  DigitalBrainOrleansExtensions.cs    (new, contains journal config)
  DigitalBrainWebExtensions.cs        (new)
  PrototypeJournals.cs                (keep, minor clean)
  Hosting/                            (new - startup related)
    ...
  Grains/                             (new - all neuron impls)
    ...
  Kernel/
    JournalJson.cs                    (keep or minor rename for clarity)
    KernelServices.cs                 (keep)
  Endpoints/                          (new dir, optional)
    UploadEndpoints.cs
  ...

src/DigitalBrain.Core/
  DigitalBrain.Core.csproj
  Contracts/                          (new - all I* interfaces, IHandle)
    INeuron.cs
    ...
  Models/                             (expand - ids, scopes)
    ...
  Synapses/                           (new - split from monster Synapse.cs)
    Synapse.cs (base only)
    Signals.cs (existing moved if needed)
    UserSynapses.cs (extracted)
    InoSynapses.cs
    DbSynapses.cs
    TaskSynapses.cs
    ...
  Sdk/ (keep)
  Config/ (keep)
  ...
```

## Expanded Core + Kernel Reorg for Production (Added per request)
While splitting kernel (Program thin + hosting), reorganize Core and Kernel into logical subfolders, delete trash, for production readiness (navigability, maintainability, less god files).

**5 Steps recap applied to reorg:**
- 1. Req less dumb: Flat root with 400+ line monster files (Synapse.cs, Program.cs) + mixed concerns (interfaces + 100 records in 1 file, neurons + hosting + endpoints mixed) is not prod-ready. Trace: incremental growth. For prod, need discoverable structure like Orleans pragmatic grouping + domain/layer split.
- 2. Delete: Remove all vacuous /// <summary>, long explanatory comments (per CLAUDE "small inline only"), TODOs, legacy compat aliases if safe, duplicated logic, dead files. Target >10% LOC reduction in touched files (aim 20%+ by slimming).
- 3. Simplify: Subfolders by concern (Contracts/, Models/, Synapses/ for Core; Grains/, Hosting/ for Kernel). Keep namespaces identical so [Alias], [GenerateSerializer], Orleans wire, project refs unchanged. Self-explanatory file names.
- 4. Accelerate: Better structure speeds onboarding + targeted changes + MCP restarts for verification.
- 5. Automate: Later (e.g. script for future splits or analyzers).

**Core split (biggest trash: Synapse.cs 462 LOC monster mixing everything):**
- Extract base Synapse + common small records to Synapses/Synapse.cs (slim).
- Group and extract domain records to dedicated files in Synapses/ (e.g. InoSynapses.cs, DbSynapses.cs, TaskSynapses.cs, UserAuthSynapses.cs, VisualizationSynapses.cs, etc.). Move existing split files like Automations.cs, SelfEvolution.cs into Synapses/ or appropriate.
- Move all I* contracts, IHandle to Contracts/.
- Expand Models/ for value types (NeuronId, TaskId, UserId, etc.).
- Delete trash: all /// summaries with no meaning, walls of // comments explaining "per item 13", "MVP for...", "Product goal...". Keep only exceptional small // if truly needed.
- Result: Core becomes browsable, no single file >150 LOC ideally.

**Kernel split (ties to Program split):**
- Grains/: move all *Neuron.cs, *TriggerNeuron.cs, SystemNeurons.cs, GeneratedNeuron.cs, KernelTaskNeuron.cs, ScheduleTriggerNeuron.cs etc.
- Hosting/: Program.cs (post thin), KernelStartupWarmupService.cs, DigitalBrainKernelExtensions.cs, PrototypeJournals.cs, OtlpProxyEndpoints.cs, any startup.
- Keep/enhance existing good subdirs (Foundry/, Gateway/, Ui/, Llm/, Config/, Sync/, SelfEvolution/, Auth/, Db/, TabularData/, Uploads/, Voice/, Sandbox/).
- Endpoints/ for extracted HTTP handlers (upload, oauth callbacks).
- Delete trash: legacy comments, "Backward compat alias for old code", TODO Task 10, falling back to legacy keys, overly long method comments. Clean vacuous summaries.
- While here, apply previous Program plan.

**Solution level cleanup:**
- Relative paths only.
- Ensure no breakage to [Alias] (they use string full names - keep ns).
- Update any internal usings if folder moves affect (usually not).
- Delete historical plans/docs if noise (keep this living REFACTOR plan + README + CLAUDE.md).
- Verify integrations/tests still build (they ref via projects).
- Latest packages already via central props.

**Risks for prod:** Aliases and grain types are identity - test cross-silo calls after. Use full rebuild + tests. No namespace changes.

## Validation Plan (Non-Negotiable, After Any Edits)
1. `dotnet build src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj --no-restore` (and abstractions).
2. `dotnet test --logger "console;verbosity=minimal"` (full root, no --filter).
3. `aspire doctor`.
4. `aspire__list_resources` + targeted `aspire__execute_resource_command` restart on kernel(s) + `aspire__list_console_logs` / traces for journal activation and timeline calls.
5. Manual smoke: pure `dotnet run --project src/DigitalBrain.Kernel` (prototype path) + full `aspire run` (durable path). Exercise `GetTimelineAsync`, Fire of Signal with nested props, ScheduleTrigger.
6. Confirm Program.cs LOC <=50, no novel comments, all paths still work.
7. Check no regression on journal replay (warmup service, checkpoints, causal lineage).
8. Retro: which of the 5 steps was skipped? Fix in next cycle.

## Risks / Open Items
- Aspire may add `WithJournaling` in future — when it does, route through it and delete direct Add* (delete step).
- If packs add Synapse subtypes after silo start, journal format must handle (current discovery at configure time is fine).
- The open `Dictionary<string,object?>` shape (previous review) is still the deeper problem for copier + journal STJ interop. This plan surfaces the journal config so the next change can address contracts/surrogates.

This plan is the living artifact. Implement slice-by-slice with checkpoints, using targeted restarts + tests after each. Delete more than you add.

Follow the 5 steps. Use the tools. Ship clean startup.

## Next Steps (Post-Commit on 1a62cf9; Apply 5 Steps)
**1. Make reqs less dumb**: Reorg done for prod (navigable structure vs flat trash). Next: question if full Program thin is enough or need more (e.g. move endpoints fully). Trace: prod launch requires fast iteration + clear ownership.

**2. Delete first (>10% net)**: Continue delete from Synapse.cs (more comments, extract remaining: Db*, Task*, Chart*, ClosedLoop to Synapses/ subfiles). Delete more legacy in moved files. Target another 10%+ LOC reduction. Remove any duplicate interfaces left.

**3. Simplify**: 
- Thin Program.cs to <50 LOC: extract to Hosting/DigitalBrainOrleansExtensions.cs (UseOrleans logic + journals), Hosting/DigitalBrainWebExtensions.cs (kestrel/cors/grpc/map), Hosting/DigitalBrainKernelServices.cs.
- Finish Core: split remaining from Synapse.cs into Synapses/DbSynapses.cs, Synapses/TaskSynapses.cs, Synapses/VisualizationSynapses.cs etc. Move interfaces to Contracts/.
- Kernel: ensure all loose root files (if any) in Grains/ or Hosting/. Self-explanatory names only.

**4. Accelerate**: After each slice: targeted `aspire__execute_resource_command` "restart" on kernel + poll logs/traces (no full aspire run). Pre-build abstractions/kernel. Background tests + min-verb.

**5. Automate last**: Only after: e.g. script to auto-extract more or enforce folder rules via analyzer. No early automation of bad structure.

**Immediate slices** (executed):
- Created Hosting/DigitalBrainOrleansExtensions.cs with UseDigitalBrainOrleans, AddDigitalBrainClients, ConfigureDigitalBrainKestrel, MapDigitalBrainSetup (encapsulates per Context7 best practices for extensions).
- Created DigitalBrainAppEndpoints.cs with MapDigitalBrainHandlers (upload + oauth extracted).
- Removed all config, detection, clients, setup, handlers trash from Program.cs (now 49 lines <50 goal; only builder creation + calls + Run + minimal).
- Extracted Db to Synapses/DbSynapses.cs (plus prior Ino).
- Deleted comments/summaries per rules.
- Build: clean (no CS).
- Test: 0 fails.
- Doctor: clean.
- Full execution of kernel thin + subfolder reorg + Core split in progress.
- Next: continue Core extracts (Charts, etc), remove experimental pragma if possible, prod review.

**Next immediate**:
- Slice: extract upload/oauth handlers to Hosting or Endpoints class.
- Continue Core: extract Db group etc.
- Always ritual.

Commit done. Branch: codex/fix-local-orleans-startup-errors. Retro: deleted config trash, structure improved.
