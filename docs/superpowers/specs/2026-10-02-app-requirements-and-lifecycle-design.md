# App requirements, storage lifecycle, and content outside deployment

Status: approved by the user on 2026-10-02 (all phases).

Issue: https://github.com/intochat/digitalbrain/issues/119

## Goal and constraints

A package describes what it needs through the contracts its code references. Installing it
either accepts those requirements before any effect or refuses with an actionable message.
Uninstall reclaims the install's Postgres tables, including their physical storage; reinstall
starts empty. Deployment describes capacity, never example business data.

The capacity and ring-law specs dated 2026-10-01 and CLAUDE.md apply. Capacity remains an
existing Platform facet. This change introduces no generic lifecycle service, resource manager,
manifest, step vocabulary, or runtime model interpretation. A Postgres collection is a neuron;
its state lists table neurons. Apps asks it to retire those neurons through Postgres contracts.
No module references another module's implementation or a Platform implementation. Kernel gains
no policy. Customer Researcher uses precisely the same paths as every other package.

The user approved all phases before implementation. The sections below describe the implemented
design and the verification required before opening the PR.

## Requirements are parsed package data

For the exact immutable revision being installed, Apps takes `PackageContent.Programs()`
(legacy `Source` as `app.cs`, plus every `behaviors/*.cs`) and `Files["tests.cs"]`, if present.
It parses file-based C# `#:project` directives, unions them and removes duplicates. The spec's
prose, model output, imports and settings never determine requirements. Test-only references
are requirements too, even when the installation is not itself running verification.

Use C# lexical/directive parsing, not a search for text anywhere in a file: comments and string
literals containing example directives are ignored. Support leading whitespace, quoted paths,
LF/CRLF and slash/backslash separators. Reject malformed project directives with file and line
information. Do not execute MSBuild or access a package-supplied path. Normalize the referenced
project's final filename, remove `.csproj`, and match that exact assembly simple name against
`ModuleInventory.ContractAssemblies()`. This is the repository's project/assembly naming
convention; a renamed project or custom assembly name that cannot match is refused, not guessed.
Relative and `/brain/...` paths with the same filename have the same identity.

`ModuleInventory.ContractAssemblies()` uses `ContractAssembliesOf(module.Assembly)` to discover
each composed module's own companion contracts. The inventory, not assemblies that
happen to be loaded in the process, defines availability. Kernel contracts are always present.
Non-neuron SDK/client support references must match an explicit existing script support
assembly set; they do not invent optional Platform modules. Arbitrary implementation projects,
unknown projects fail closed. A load failure in inventory discovery
becomes an unresolved-contract refusal, never an empty inventory or an unhandled load error.

For absent conventional `DigitalBrain.Modules.<name>.Contracts` references, report `<name>`
as the missing module. For an unknown or
renamed assembly, report its full simple name as unresolved. Sort and deduplicate names ordinally
so the same revision and composition give the same result. A reference present only transitively
through another module's implementation does not count as a composed module.

Apps.Contracts owns an `AppRequirementsException` and constant message templates:

- `Cannot install this app. Missing modules: {0}.`
- `Cannot install this app. Unresolvable contract references: {0}.`
- `Cannot install this app. Invalid project directive: {0}.`

If both missing and unresolved sets exist, concatenate their formatted sentences with one space.
The successful path retains the existing installed snapshot and presentation, without a new
success message. Requirements remain derived data; no serialized requirements list is added.

`IApp.Install` reads the revision and checks requirements before settings/account resolution,
state writes, receipts, file writes, script starts or storage registration. Thus a requirement
refusal is atomic, including when the previous state is Uninstalled. Retry of a successful
operation still uses its receipt. `Upgrade` checks the new revision before retiring old scripts;
`Configure` checks before replacing scripts if composition has changed. Requirement atomicity
does not claim a distributed transaction for unrelated later infrastructure failures.

Scratch `IApp.Install` calls from `tests.cs` execute that same method with no bypass. Verification
also performs the same revision check before launching `tests.cs`, so a missing test reference
gets the useful refusal instead of a compiler error. The publish gate remains exit zero, at
least one reported scenario, all scenarios passing, and no live model.

## Storage ownership and enumeration

The current ownership detail matters: ScriptEdge stamps `CallerContext.AppId = claims.File`,
not the `brain.Setting("App")` value. Postgres `Owner` serializes `[brainId, caller.AppId]`.
One installation therefore owns multiple file scopes; configure/upgrade creates more of them.
Do not silently reinterpret existing Owner values or trust a script-supplied App setting as
authority. Preserve this stamping and the existing physical-name algorithm.

Append concrete-array state to `AppState` recording all file IDs deployed during the current
installation, including retired generations. Persist planned IDs before starting any file;
retry deploys the same generation and IDs. Configure/upgrade retain earlier IDs. Record durable
pending-operation data with the existing operation ID and request hash so a crash between
deployment and final save cannot choose a different generation or forget an owner. Reserve
the next free IDs starting after 11; never reuse the missing historical Id(2). The additions are
StorageFiles (12), PendingDeployment (13), PendingUninstall (14), StorageHistoryKnown (15),
LifecycleReceipts (16) and PostgresScopes (17). The last flag remembers whether any behavior
generation required Postgres, so removing that module cannot falsely complete teardown.

Postgres.Contracts adds `IPostgresTables`, a neuron keyed by the encoded brain/file owner scope,
with module-authorized registration and retirement calls. Postgres implements it with a durable
array of table neuron IDs and a permanent retired flag. This neuron enumerates tables; Apps
passes only its recorded owner scopes and awaits retirement. Apps references Postgres.Contracts
only and calls this path only when Postgres is composed. Removing Postgres from a deployment
with owned storage must refuse cleanup rather than declare success; restore the module first.

Registration is performed by Postgres before physical DDL. Append pending-definition information
to PostgresTableState: register the table ID durably, then pin Owner, Origin and normalized
definition durably, then create the physical table and persist Accepted. Retry uses that pin.
This closes the current create-before-save orphan window. A pending definition is teardown
input too: its hashed physical name is deterministically derived inside Postgres.

Registration and retirement's state writes serialize on the collection neuron. Registration can
interleave with retirement's table calls, so a table finishing Define promptly observes the closed
scope rather than deadlocking against its own teardown. Retirement first persists its
closed flag and table list, then calls table teardown. New registration against a closed scope
fails. Table operations verify that their scope is open before effects. Avoid a grain cycle:
registration never calls a table; table teardown called by the collection does not call back
to that collection. An in-flight Define either registers before retirement and is in the list,
or cannot create anything. Table-grain serialization orders an in-flight mutation before its
drop. Revoking script runs is additional protection, not the sole race prevention mechanism.

The collection/teardown methods are module-only contracts excluded from script discovery
using the existing per-contract protection mechanism. Authenticate the Apps neuron and its
brain before accepting its scope list, and authenticate the Postgres collection before table
teardown; reject arbitrary client, script and cross-brain calls. No public drop-by-name or
caller-chosen origin API is exposed. Table teardown rechecks the exact expected Owner.

## Uninstall, replay and reinstall

Apps persists an uninstall-in-progress operation before effects and refuses new configure,
upgrade, invoke or install work until it completes. It retires every recorded file, asks
Postgres to retire every recorded owner scope, settles pending invocations, then saves
Uninstalled and the operation receipt. Failure leaves durable pending work, never a successful
uninstall. A retry or reactivation resumes the same operation before accepting a new install.

For each table, Postgres drops the pinned physical table using `DROP TABLE IF EXISTS` under
the same advisory lock used for definition. The provider alone holds SQL, quoting, connections
and source selection. Use `Accepted.Table` when available and `OriginOrPlatform(Origin)`;
do not call capacity resolution or provision fresh capacity during cleanup. Drop no database,
shared schema, integration account or table belonging to another owner. Do not use CASCADE.

Only after drop succeeds does the table clear Owner, Accepted, Origin and pending definition.
Retain closed-owner protection in collection state. If the process dies after drop but before
the state save, replay drops the already absent table and finishes. If the collection retries
after the table has been reused by a new owner, the expected-owner comparison makes the old
request a no-op; it cannot erase the new definition. Keep retired collections as durable
tombstones, rather than evictable recent-operation receipts.

An uninstall of NotInstalled or Uninstalled is a successful no-op even with a fresh operation
ID. Retain a receipt even for a no-op uninstall. Replaying a completed uninstall after reinstall returns the current snapshot without
effects. Replaying an old successful install after restart likewise does not deploy again.
Reinstall with a new operation ID uses higher file generations, never reopens retired scopes,
and can Define the same logical table key with a different schema against empty storage.
Sharing or forking a package shares code only: different brain/install IDs yield independent
files, owners and physical tables. Uninstall of one cannot remove another's data.

## Legacy persisted state

Keep existing serialized names, aliases and IDs. PostgresTableState retains Owner at 0,
Accepted at 1 and Origin at 2; additions begin at 3. Null Origin on an existing accepted table
means the platform source for reads, repeated Define and teardown. It must never be resolved
to a newly registered source. New state collections are concrete arrays.

Lazy registration alone is insufficient: an untouched old table must also be reclaimed.
Ship a Postgres-owned, resumable maintenance connector to backfill collection neurons from
existing `postgres.table` state before enabling lifecycle completion for legacy installs.
For the deployed Azure Blob store it enumerates table-state blobs in `digitalbrain-v2-state`,
decodes them with the existing Orleans serializer, and registers each grain ID under its stored
Owner. It does not infer ownership from SQL names or load other modules' implementation types.
Run the scan as a silo startup task before serving app execution; Apps also awaits Ensure before
retirement. Persist its completion in a Postgres neuron and
fail closed on unreadable records. Restarting an incomplete scan repeats registration safely.
For the first upgrade, stop old-version silos before starting the new version: legacy writers
must be quiesced while their ownership is backfilled.
Fresh stores record an empty completed scan. Test storage supplies equivalent legacy records.
This is migration code, not a new Kernel enumeration API or script-visible storage service.

Legacy Apps state also needs historical file scopes: the Apps-owned migration reads the
installed revision ancestry and derives FileKey for each recorded deployment generation and
the union of its behavior paths, including legacy app.cs. Extra empty scopes are harmless.
If required history is unavailable, migration refuses completion with diagnostics; it does
not silently abandon tables. Preserve current ScriptPaths as an additional source. Backfill
before uninstall, and retain the result thereafter. Neither migration rewrites old numeric IDs.

## Content belongs with its consumer

Remove `WithSeed("leads")` from IntoChat AppHost and its E2E composition. ClickHouse is composed
with capacity options only. The existing lead data is illustrative fixture content; Customer
Researcher stores its own research and does not need it. Do not add automatic demo data to that
package or grant write SQL to the currently read-only `IClickHouse` contract.

Move the lead SQL out of Aspire.Hosting into the owning test/package content directory for
the consumers that use it. Test consumers invoke their connector's setup explicitly, with
deterministic keys and per-key missing-row insertion under serialized setup, rather than the
current whole-table `count() == 0` guard. Verify two setup runs and recovery from partial setup.
No remaining consumer means delete the unused fixture. A future product demo must be explicit
package content installed through the ordinary pipeline, never a deployment seed.

IntoChat unit owns a parsed composition fact: inspect AppHost's module options and resource
configuration, requiring the allowed capacity settings, zero seeds/init scripts/content paths,
and neutral database names. Assert the allowed option shape, not just absence of the word
`leads`, so renaming the sample or hiding it behind an options helper cannot evade the guard.

## Refusals reach the person unchanged

Keep the capacity text defined once in
`CapacityUnavailableException.RefusalMessage`: "Sorry, runtime provisioning is not accessible
at the moment." Requirement templates live once in Apps.Contracts. Callers use these constants
or the resulting typed error message, never duplicate literals or format a replacement apology.

Map requirement refusals to the existing ProblemDetails response with status 409 and exact
message in detail; capacity refusals use status 503 and exact detail. Preserve a stable typed
error code/message across the CSharp edge, whose client reconstructs `ScriptRefusalException`
with the message and original exception type name as Code, rather
than handing scripts an opaque transport exception. No stack trace is a user-facing response.

The normal package install UI renders the returned detail verbatim. Assistant tool failures
carry the same text and the deterministic presentation path emits it directly; do not rely
on a model to quote it correctly. For behavior execution, setup and invocation failure paths
preserve the recognized capacity error in AppResponse.Error and the visible status. In
Customer Researcher this includes Define/setup failures and bypasses the generic research
catch and trailing-period trimming. Other failures keep their existing sanitization.

## Ordering and verification

After approval, each behavior change starts with a failing fact; implement the smallest change
that passes it. Reviewable commit groups, each building and passing affected project suites:

1. Requirement parsing, composed-module resolution and atomic Apps checks, including scratch
   verification and upgrade preflight; typed contract refusals.
2. Postgres collection neurons, durable definition registration, pinned drop and legacy
   migration. Add provider and real-database coverage of physical deletion.
3. Apps durable file-scope history and resumable uninstall/reinstall, including migration and
   crash recovery across effects and saves.
4. End-to-end refusal transport and deterministic app/assistant presentation.
5. Consumer-owned fixtures and the IntoChat composition guard; full shipped-package gate and
   local Aspire journey.

Apps facts cover all present, one missing, and renamed/unresolvable contract assembly. Each
asserts the actual user-visible success/refusal, with missing requirements leaving no script,
receipt or installation mutation. Include tests-only requirements, duplicate/path variants,
comment/string false positives, and upgrade preserving the
running revision. Prove sandbox scratch installation uses the same check.

Postgres facts cover define -> upsert -> teardown -> redefine -> empty, teardown twice,
reactivation between drop and state save, pinned origin after capacity changes, null-origin
legacy state, pending DDL recovery, concurrent registration/retirement, old teardown after
reuse, and another install/fork remaining intact. Apps facts exercise uninstall twice with
different IDs, install replay after restart, failed final saves and every behavior generation.
Any changed serialized type gets a frozen LegacyState-style reactivation fact, following
Supabase's precedent; test both preserved fields and the new operation after reactivation.

Facts have sentence-shaped names, use TestContext.Current.CancellationToken and real neurons;
fakes belong at connector boundaries. No new InternalsVisibleTo, no model in verification.
Requirement tests live in Apps; table lifecycle in Postgres; seeding in ClickHouse/its consumer;
composition in IntoChat unit. Transport/presentation tests live with CSharp/Assistant/Flutter
where changed, including matching Dart wire changes if required. Run the existing project-reference
facts and audit production project references to confirm no module-to-module implementation
reference was added.

Run `dotnet test` separately for affected Apps, Postgres, ClickHouse and IntoChat unit projects,
plus affected owner projects and module E2E suites; never run the `.slnx`. Compile live-gated
scenarios when credentials are unavailable. Then `aspire run` from
`src/Applications/IntoChat/AppHost`: all resources Healthy, install Customer Researcher,
research into the locally provisioned database, uninstall, verify physical removal, reinstall,
verify empty/redefinable storage and research again. Verify every shipped package's sandbox
gate. Record actual commands, results and any unavailable verification in the PR body.

Every commit ends with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
The implementation PR includes `Closes #119`, per-project verification evidence and smoke
evidence. Update this document to approved only after the user approves it, and keep it aligned
with implementation; material design departures return for approval.

## Acceptance

- Missing or unresolvable contracts refuse before installation effects, naming the missing
  module or unresolved assembly; Customer Researcher has no special-case requirement list.
- User and sandbox scratch installs enforce the same derived requirements, and every shipped
  package still passes its deterministic publish gate.
- Uninstall reclaims all pinned tables and table definitions, including historical generations
  and legacy state. Double-uninstall is a no-op; restart/replay cannot duplicate effects or
  delete a new installation's data; reinstall is empty and redefinable.
- Apps asks through contracts; Postgres enumerates and drops. SQL and connection knowledge
  stay in Postgres. No new module-to-module implementation reference or fifth concept exists.
- AppHost contains zero content-shaped configuration, guarded by an IntoChat composition fact;
  remaining fixture consumers seed their own content idempotently.
- Requirement and capacity refusals reach normal app and assistant surfaces verbatim, with
  their messages defined once.
- The approved spec matches implementation; affected per-project tests pass and the local
  provisioned-database install/research/uninstall/reinstall smoke has all resources Healthy.
