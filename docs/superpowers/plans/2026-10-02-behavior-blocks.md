# Behavior Blocks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking. Execution method awaits the user's choice.

**Goal:** Let people author apps as independent natural-language behavior blocks, inspect source through overflow menus, and expand exact verification scenarios with registry highlighting.

**Architecture:** Structured documents live in the existing draft neuron and travel in immutable package content. A deterministic export preserves the existing spec/build/test pipeline; a tolerant legacy adapter preserves older content. Shared Flutter widgets consume typed documents, revision-bound results, and advisory vocabulary from the composed contract catalog.

**Tech Stack:** C#, Orleans, existing ASP.NET endpoints, Flutter/Dart and current shell themes; no new editor framework or heavyweight dependency.

**Spec:** `docs/superpowers/specs/2026-10-02-behavior-blocks-design.md` (approved 2026-10-02).

## Global Constraints

- No scenario neurons, step grammar, runtime prose interpretation, or execution ordering by block position.
- Preserve exact gate names and existing publish semantics. Draft action says Build and publish; no Private badge.
- Existing specs remain byte-for-byte unchanged during viewing. Unknown structured versions are read-only with a complete fallback.
- Append serialized IDs, retain aliases, use concrete arrays, mirror contracts in Dart in the same task.
- Registry references are advisory; PlatformOnly and platform-assembly types never appear.
- Package source lookup is by exact revision/content key, never by host filesystem path.
- No permanent IDE/chatter/inspector layout. Small content-sized blocks follow the approved sketch.
- Create an isolated worktree at execution time using using-git-worktrees; inspect applicable repository instructions before editing. Do not install product dependencies during planning.

## Review Focus

1. Two open editors must not overwrite each other; retain unsaved text on conflict (Tasks 2, 7).
2. Old passing results must not become current after edits, failures, or shared-scenario expansion (Tasks 3, 5).
3. Unknown metadata and malformed/duplicate legacy headings must remain visible without fabricated green checks (Tasks 1, 5).
4. Duplicate short type names and platform-only symbols must not yield a falsely resolved reference (Tasks 4, 5).
5. Mixed old/new clients, deep links to old revisions, and narrow screens must preserve source identity and editing access (Tasks 2, 6, 8).

## Paths and checks

Paths below use these exact prefixes:

- `AC` = `src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts`
- `A` = `src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps`
- `AU` = `src/Modules/DigitalBrain/Apps/tests/DigitalBrain.Modules.Apps.Tests.Unit`
- `AE` = `src/Modules/DigitalBrain/Apps/tests/DigitalBrain.Modules.Apps.Tests.E2E`
- `CC` = `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Contracts`
- `C` = `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp`
- `CU` = `src/Modules/Microsoft/CSharp/tests/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit`
- `CE` = `src/Modules/Microsoft/CSharp/tests/DigitalBrain.Modules.Microsoft.CSharp.Tests.E2E`
- `F` = `src/Modules/Google/Flutter/app/shell`

For a named C# fact class, run `dotnet test <prefix>/<project-directory-name>.csproj --filter FullyQualifiedName~<class>` from the checkout root, expanding the prefix above. Full project checks omit the filter. Never test the solution file. Dart commands run from `F`.

Each task's test step precedes implementation: run the named tests and record the relevant failure, implement, rerun to PASS, then commit only task files. Compilation failure is acceptable for an intentionally absent new API; environmental failures are not evidence of a failing behavior test.

## Task 1: Structured documents and lossless compatibility

**Files:** create `AC/Authoring/AppAuthoringDocument.cs`, `A/Authoring/AppDocumentCodec.cs`, `AU/AppDocumentFacts.cs`, `F/lib/workspace/apps/app_document.dart`, `F/test/app_document_test.dart`; modify `AC/Packages/PackageContent.cs` only to expose the metadata path constant.

**Interfaces:** `AppAuthoringDocument(int Version, string Preamble, AppBehaviorDocument[] Behaviors, AppScenarioDocument[] Scenarios)`; `AppBehaviorDocument(string Id, string Title, string Description, string[] SourcePaths, string[] ScenarioIds)`; `AppScenarioDocument(string Id, string Name, string Body, bool IsLive)`. IDs are GUID strings, version is 1, sidecar path is `app.authoring.json`. `AppDocumentCodec.Validate(document, files, requireSources)` rejects invalid references; `ExportSpec(document)` returns Markdown; `Read(content)` returns `AppDocumentReadResult(Document?, OriginalSpec, CanEdit, Error?)`. Provide equivalent Dart decoding.

- [ ] Write facts asserting IDs survive JSON round-trip; export includes behavior descriptions in a preamble section and each scenario exactly once; duplicate gate names/IDs and dangling scenario links are refused. Assert missing source is allowed prebuild and refused when requireSources is true; only relative package keys are accepted.
- [ ] Add legacy/unknown-version fixtures: `Assert.Equal(original, result.OriginalSpec)` and `Assert.False(result.CanEdit)` for unknown metadata. Invalid metadata cannot hide original spec or raw metadata diagnostics. Add Dart assertions for the same wire fixtures.
- [ ] Run `AppDocumentFacts` and `flutter test test/app_document_test.dart` and confirm expected failures.
- [ ] Implement validation, versioned encoding and deterministic export. Preserve name matching exactly; serialize `(live)` only as the documented heading suffix. Reject structured titles/bodies that would inject extra scenario headings into the export. Do not create source associations by scanning prose.
- [ ] Rerun both checks to PASS; commit `feat: add structured app authoring documents`.

## Task 2: Revision-safe draft persistence and editing contracts

**Files:** modify `AC/Authoring/{AppDraftState.cs,IAppDraft.cs,AppDraftView.cs}`, `A/Authoring/{AppDraftNeuron.cs,MarketplaceEndpoints.cs}`, `AU/AppDraftFacts.cs`; create `AC/Authoring/AppDocumentRequests.cs`, `A/Authoring/AppDraftDocuments.cs`, `AE/AppDocumentHttpFacts.cs`; update Dart document/request adapters in `F/lib/workspace/apps/app_document.dart`.

**Interfaces:** append draft state `Document` at Id 11, `SourceRevision` at 12, `VerifiedDocumentHash` at 13. `SaveAppDocument(long ExpectedRevision, AppAuthoringDocument Document)`; `IAppDraft.SaveDocument(SaveAppDocument request) -> Task<AppDraftView>`. `IAppDraft.ImportRevision(PackageRevisionRef revision, long expectedRevision) -> Task<AppDraftView>` initializes a fresh draft using exact package revision. Routes: PUT `/packages/drafts/{id}/document`, POST `/packages/drafts/{id}/import`. Import rejects nonempty targets. HTTP conflicts return 409.

- [ ] Write facts for save/reopen persistence and `Assert.Equal(before.Document, afterRejectedWrite.Document)` on stale revision; successful save increments revision. Test ownership via two authenticated callers and no mutation of the installed app or package revision.
- [ ] Test legacy EditSpec on structured drafts is refused without losing metadata; legacy drafts retain existing EditSpec behavior. Mutation during Build is refused. Importing an older revision returns that revision's exact content even when head differs.
- [ ] Run `AppDraftFacts` and `AppDocumentHttpFacts` to expected FAIL.
- [ ] Implement ownership through existing Draft resolution, structured atomic saves, deterministic Spec refresh, conflict mapping, and source revision recording. Clear current-evidence hash on mutation but retain historical verification. Reject old overwrite paths for structured content. Derive edit capabilities server-side using existing package ownership/access checks.
- [ ] Rerun both classes to PASS; commit `feat: persist revision-safe behavior drafts`.

## Task 3: Author/Builder integration and explicit conversion

**Files:** modify `A/Authoring/{AgentPrompts.cs,AppDraftNeuron.cs,BuilderTools.cs}`, `AC/Authoring/IAppDraft.cs`, `A/Authoring/MarketplaceEndpoints.cs`, `AU/AppDraftFacts.cs`; create `A/Authoring/AppDocumentConversion.cs`, `AU/AppDocumentConversionFacts.cs`.

**Interfaces:** Author emits the Task 1 document plus existing app metadata. Builder emits existing files/settings and `BehaviorSourceBinding(string BehaviorId, string[] SourcePaths)[]`; it cannot alter IDs/prose/scenarios. `IAppDraft.ProposeConversion(long expectedRevision) -> Task<AppAuthoringDocument>` returns a proposal without saving; POST `.../conversion` exposes it. Accept through SaveDocument. `AppDocumentConversion` preserves the original legacy text and all unassigned scenarios.

- [ ] Test that a description edit appears in Builder input; Builder association of an unknown behavior or nonexistent source fails validation; sidecar round-trips through publish/fork/import. Assert source-only binding updates preserve document identities and names.
- [ ] Test proposal generation leaves stored state unchanged; accepting preserves original content and does not invent uncertain links. Test duplicate creates fresh ID/prose with empty sources and links, and deletion retains scenario records referenced elsewhere (document validation facts).
- [ ] Test failed rebuild cannot carry old success as current. Hash includes editable document content; set VerifiedDocumentHash only for the successfully built document, while result revision pins the final content/source. Source changes require a new immutable revision.
- [ ] Run `AppDraftFacts` and `AppDocumentConversionFacts` to expected FAIL.
- [ ] Implement structured Author validation/retries and Builder binding validation; write generated spec and sidecar together before publish. Keep legacy authoring path available. Whole-app tests and publishing gate remain unchanged. Refuse destructive conversion of unknown metadata versions.
- [ ] Rerun classes to PASS; commit `feat: build and convert structured behavior documents`.

## Task 4: Safe registry vocabulary and enriched app views

**Files:** create `CC/Catalog/IContractVocabulary.cs`, `C/Catalog/ContractVocabulary.cs`, `CU/ContractVocabularyFacts.cs`, `AC/Authoring/SpecVocabulary.cs`, `A/Authoring/AppSpecVocabulary.cs`, `AU/AppSpecVocabularyFacts.cs`; modify `C/{CSharpModule.cs,Edge/ScriptContracts.cs}`, `A/Authoring/{MarketplaceService.cs,AppDraftNeuron.cs}`, `AC/Authoring/{AppSpecView.cs,AppDraftView.cs}` and Dart view adapters.

**Interfaces:** `IContractVocabulary.Read() -> ContractVocabularyEntry[]`; entries contain `QualifiedName, Name, Kind, Module, Description?`, kind Neuron or Signal. Shared internal discovery supplies ScriptContracts and ContractVocabulary with the same filtered contract universe. `SpecVocabulary` joins entries with manifest Operation/Setting/Account tokens. Append `Manifest`, `DocumentReadResult`, and `Vocabulary` to app spec responses; append equivalent vocabulary and exact-revision source files to draft views as new serialized fields.

- [ ] Write `Assert.DoesNotContain(entries, x => x.QualifiedName == protectedName)` for protected types and platform assemblies, including signals. Assert two identical short names retain two qualified entries; missing descriptions remain null. Test composed inventory only, not arbitrary loaded assemblies.
- [ ] Test manifest token inclusion, optional vocabulary provider absence, exact-revision files, wire round-trip and no implementation-project references. No malformed registry response may suppress spec content.
- [ ] Run `ContractVocabularyFacts` and `AppSpecVocabularyFacts` to expected FAIL.
- [ ] Implement cached read-only discovery via composed ModuleInventory and existing dependency injection. Do not add script-callable reflection methods. Use contracts-only dependency from Apps. Supply correct revision metadata and VerifiedAt already present in AppVerification.
- [ ] Rerun classes to PASS; commit `feat: expose safe app specification vocabulary`.

## Task 5: Pure Dart parsing, matching and result projection

**Files:** create `F/lib/workspace/apps/{spec_document.dart,spec_highlighter.dart,spec_vocabulary.dart,app_verification_summary.dart}` and corresponding files under `F/test/` ending `_test.dart`; extend `app_document.dart`.

**Interfaces:** `parseSpec(String) -> SpecDocument` preserves all content; `highlightSpec(String, SpecVocabulary) -> List<SpecSpan>` emits offset ranges and candidate identities; `summarizeVerification(AppDocument, AppVerification?, {required bool isCurrent}) -> VerificationSummary`. A span stores start/end, kind, and candidate tokens; summary has app totals and per-behavior statuses.

- [ ] Write tests for empty/preamble-only/malformed specs, extra heading whitespace, fenced code containing heading text, and duplicate names. Assert text reconstruction equals input. Duplicate names cannot receive an unambiguous pass.
- [ ] Write matcher tests: research is not inside researcher; longest identifiers win; neuron/signal case is exact; manifest names match whole words; operations in quotes outrank literals; duplicate short names return candidates; empty vocabulary still preserves literals/text. No source linking from spans.
- [ ] Write result tests: shared scenario counted once, unlinked scenarios retained, nonzero exit is visible despite passing entries, missing verdict is Not run, no scenarios is No checks, only live is Live documentation, stale success is historical.
- [ ] Run `flutter test test/spec_document_test.dart test/spec_highlighter_test.dart test/app_verification_summary_test.dart` to expected FAIL, implement pure functions, then rerun to PASS.
- [ ] Commit `feat: parse and highlight behavior specifications`.

## Task 6: Shared readable behavior blocks and source sheets

**Files:** modify `F/lib/workspace/apps/{app_spec_view.dart,app_spec_screen.dart}`, `F/test/app_spec_test.dart`; create `F/lib/workspace/apps/{behavior_block.dart,scenario_list.dart,app_source_sheet.dart,registry_token_sheet.dart}`, `F/test/behavior_block_test.dart`.

**Interfaces:** `AppSpecView` consumes typed document/read result, vocabulary, revision-bound verification and optional edit callbacks; `BehaviorBlock` emits edit/duplicate/delete intent by stable ID. Source sheet consumes a revision label and exact files map; no host path/network guessing. Registry sheet receives candidate entries from Task 5.

- [ ] Write widgets tests for compact blocks and semantic overflow labels, expansion/shared scenarios, selectable failures, ambiguous registry picker, missing-source empty state, and current versus historical verification. Assert source text belongs to requested revision, not latest head.
- [ ] Run `flutter test test/app_spec_test.dart test/behavior_block_test.dart` to expected FAIL.
- [ ] Implement lazy outer list and content-sized block children, theme tokens, icon/text verdicts, read-only source sheet and optional edit menus. Legacy parser fallback shows all original content. Keep unlinked scenarios visible at app level. Add keyboard focus/menu/disclosure behavior and narrow-width sheets.
- [ ] Rerun widget tests in both themes to PASS; commit `feat: present apps as readable behavior blocks`.

## Task 7: Wire authoring interactions and app navigation

**Files:** modify `F/lib/workspace/apps/{create_app_screen.dart,apps_screen.dart,app_spec_screen.dart}`, `F/test/{apps_screen_test.dart,app_spec_test.dart}`; create `F/lib/workspace/apps/{behavior_editor.dart,app_draft_controller.dart}`, `F/test/app_draft_controller_test.dart`, `F/test/behavior_authoring_test.dart`.

**Interfaces:** controller wraps existing AppsRequest and owns `saveDocument`, `importRevision`, `proposeConversion`, `build`, plus unsaved document and expected revision. Editor returns title/description or cancellation. Conversion review permits explicit scenario-to-behavior linking before acceptance. Shared view receives these callbacks only with edit capability.

- [ ] Write tests for Add/Edit/Cancel, duplicate reset, deletion preserving shared scenarios, unsaved conflict recovery, conversion review/accept/cancel, and reopening draft after failure. Assert hidden edit controls are backed by server refusal from Task 2.
- [ ] Test published edit creates/imports a draft pinned to the displayed revision; unauthorized package edit is unavailable. Assert Build and publish copy, no Private badge, no invented per-scenario run endpoint, and Open only through existing installed-app capability.
- [ ] Run `flutter test test/app_draft_controller_test.dart test/behavior_authoring_test.dart test/apps_screen_test.dart` to expected FAIL.
- [ ] Implement focused editors and conversion review, revision-safe saves, historical verification state, local busy indicators and recoverable errors. Keep read/scroll usable while registry/source details load. Offer full-app checks for published revisions and the existing build/publish action for drafts.
- [ ] Rerun tests to PASS; commit `feat: wire independent behavior authoring`.

## Task 8: End-to-end proof and visual verification

**Files:** extend `AE/AppDocumentHttpFacts.cs`, `AE/PackageSharingHttpFacts.cs`, `CE/AuthoringCompositionFacts.cs`; add `F/test/behavior_authoring_journey_test.dart`; save evidence to `docs/superpowers/verification/2026-10-02-behavior-blocks.md`.

- [ ] Add journeys for create/edit/build/publish/read/fork/import identity preservation; stale save refusal; exact source revision; legacy package unchanged; registry absent and protected names excluded through the composed HTTP response. Add widget journey from app entry through edit and expanded verification.
- [ ] Run focused new journeys to expected FAIL where gaps remain; repair only their owning task implementation, then rerun to PASS.
- [ ] Run `flutter analyze` and `flutter test` from F. Run full AU, AE, CU and CE project suites using the expanded project paths. Record environment/credential blocks honestly; compile gated suites if runtime credentials are unavailable.
- [ ] Start the repository's current IntoChat AppHost through its documented Aspire command; inspect actual Flutter desktop and narrow views in both themes. Verify content density, scrolling, menus, source sheet, highlight contrast, failures, conversion and conflict recovery against the supplied sketch and final approved refinement. Save actual screenshots as evidence. Do not substitute the generated mock for rendered UI evidence.
- [ ] Review full diff for authorization gaps, protected vocabulary exposure, wire compatibility, lost legacy content, stale green results and changed runtime semantics. Record results and limitations; commit `test: verify behavior block authoring journeys`.

## Handoff

Tasks are ordered because contracts and revision semantics feed the renderer and authoring flow. Recommend native execution in this chat with a final independent branch review: one implementer can keep the shared document contract consistent across C# and Dart. Subagent-driven task implementation/review is an alternative with more isolated review at each boundary. No product implementation has started; review this plan and select the execution method first.
