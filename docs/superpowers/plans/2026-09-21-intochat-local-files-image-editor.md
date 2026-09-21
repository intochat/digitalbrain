# IntoChat Local Files and Image Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship neuron-composed Files and Image Editor apps, with a verified local Downloads → edit → Save copy workflow.

**Architecture:** Flutter supplies reusable Surface/Layout/Collection/ImageCanvas neurons and their renderers. IntoChat supplies local file access, app controllers, image documents, scoped endpoints and bundled app definitions. The existing workspace shell hosts app surfaces and preserves table compatibility.

**Tech Stack:** Existing C#/Orleans/ASP.NET Core application and module infrastructure, Flutter/Dart Canvas, existing xUnit v3/Microsoft Testing Platform and Playwright/Aspire E2E harness. No new image-processing server dependency or AI service.

**Spec:** `docs/intochat-local-apps-design.md` (read first). Earlier broader product documents are superseded for this milestone's scope.

## Global Constraints

- Ship two working bundled apps: **Files** and **Image Editor**.
- Show **Automations / Dev Studio** in Applications with a disabled action and the explicit status **Not implemented**.
- Never overwrite the original.
- No file deletion, moving, renaming, Google Drive, or arbitrary filesystem browsing in this release.
- No LLM or live provider is required for this milestone.
- Local browsing and editing do not deduct Compute.
- Preserve existing table functionality and saved workspaces.
- Source bytes at most 32 MiB, decoded source at most 40 million pixels, export at most 40 million pixels and 128 MiB encoded bytes.

## Review Focus

1. A file disappears or changes between listing and opening: report it clearly and keep existing documents intact (Tasks 2, 3, 7).
2. Source rotation/orientation, zoom and pan alter pointer coordinates: crop and pen must export the same image region the user saw (Tasks 4, 7).
3. Retry, concurrent edits or process interruption during save: one complete output per operation, correct saved revision, original untouched (Tasks 3, 7).
4. Old table windows and missing/invalid child references: restore existing data and isolate one broken child instead of losing the workspace (Tasks 1, 5, 6).
5. Large folders, oversized images and restricted paths: bounded resource use, useful errors and no escape from configured roots (Tasks 2, 4, 7).

## Repository evidence and boundaries

The current active app entry point is `src/Applications/IntoChat/IntoChat/Program.cs`; workspace routes live under `IntoChat/Workspace/WorkspaceEndpoints.cs`. `IntoChat.csproj` excludes legacy `Http/SurfaceEndpoints.cs`, `Http/WorkspaceEndpoints.cs`, `Http/UiEndpoints.cs` and several related files. Do not reactivate those files or assume their Surface types exist.

`Contracts/Card/ICard.cs` already stores `UiChildRef` children, but `app/ui/lib/src/components/card/ui_card.dart` and `src/models/ui_part.dart` do not render them. Close this composition gap rather than introducing an unrelated surface renderer. Existing workspace windows use `TableViewReference`; preserve its serialization and existing Open calls.

The image widget in `app/shell/lib/workspace/artifact_editors.dart` has file import, visual rotation/brightness and zoom. It does not supply durable image recipes, crop, drawing or actual edited-image export. Extract reusable new editor code instead of growing this file. Preserve legacy artifact rendering until explicitly migrated.

All paths below are repository-relative. Proposed filenames/types are new unless marked Modify. Before execution, read repository instructions, inspect the working tree, and use an isolated checkout if implementation needs one. Do not overwrite the existing untracked design documents or prototype.

## Contract decisions shared by tasks

Use the existing `UiChildRef(Kind, Name)` and vocabulary convention (`card`, `image`, etc.). New kinds are `surface`, `layout`, `collection`, `imagecanvas`. Stable child identities are scoped under the owner/workspace/app instance.

Proposed contracts (put each public contract in its feature's Contracts folder, with repository-standard Orleans aliases and stable field IDs):

```csharp
// Flutter module. Each state includes Name and Revision as well as the fields below.
ISurface.Set(SurfaceDefinition definition, long expectedRevision);
ISurface.Read(); // SurfaceState: Title, Children
ILayout.Set(LayoutDefinition definition, long expectedRevision);
ILayout.Read(); // LayoutState: Mode, Children, Gap, pane constraints
ICollectionView.Set(CollectionDefinition definition, long expectedRevision);
ICollectionView.Read(); // CollectionState: Items, selection, cursor, loading/error
IImageCanvas.Set(ImageCanvasDefinition definition, long expectedRevision);
IImageCanvas.Read(); // ImageCanvasState: AssetId, Width, Height, Recipe, DocumentRevision

// IntoChat application. Async Task<T> throughout; cancellation at service/HTTP boundaries.
IFileExplorer.Read(); // FileExplorerState
IFileExplorer.Navigate(string folderId); // FileExplorerState
IFileExplorer.Refresh(); // FileExplorerState
IFileExplorer.OpenImage(string entryId, string operationId); // ImageDocumentState
IImageDocument.Read(); // ImageDocumentState
IImageDocument.Apply(ImageEditCommand command, long expectedRevision, string operationId);
IImageDocument.PrepareSave(long expectedRevision, string operationId); // SaveTicket
IImageDocument.CompleteSave(string operationId, SavedFile result); // ImageDocumentState
```

Definitions: SurfaceDefinition contains title and ordered UiChildRef children. LayoutDefinition contains mode (`row`, `column`, `split`, `stack`), ordered child references, gap and bounded pane sizes. CollectionDefinition contains ordered CollectionItem(Id, Label, Kind, SecondaryText, SizeBytes, ModifiedAt, CanActivate), selection and paging state. ImageCanvasDefinition contains an authenticated asset reference, dimensions, immutable ImageRecipe and document revision. ImageRecipe contains optional integer CropRect(X,Y,Width,Height) and ordered PenStroke(ColorArgb,Width,Points); Point uses floating source-pixel coordinates. ImageEditCommand is a typed union: AddStroke, SetCrop, ResetRecipe, ReplaceRecipe (undo/redo). Reject unknown variants.

FileExplorerState contains root/folder IDs, breadcrumbs, collection revision and Surface reference. ImageDocumentState contains ID, source asset ID/fingerprint, source entry ID, dimensions, recipe, revision, last saved revision and Surface reference. SaveTicket binds operation ID, document revision, immutable recipe digest and destination folder to an expiring upload; SavedFile contains entry ID, filename, byte count and checksum. API DTOs expose opaque IDs, never filesystem authority. Implementation must finalize serializer IDs and DTO schema together before parallel work on callers.

## Task 1: Make reusable UI composition real

**Files**
- Create `src/Modules/Flutter/Contracts/Surface/ISurface.cs`, `Contracts/Layout/ILayout.cs`, `Contracts/Collection/ICollectionView.cs`, `Contracts/ImageCanvas/IImageCanvas.cs`, and feature-local state/definition/event files.
- Create `src/Modules/Flutter/Flutter/Surface/SurfaceNeuron.cs`, `Flutter/Layout/LayoutNeuron.cs`, `Flutter/Collection/CollectionNeuron.cs`, `Flutter/ImageCanvas/ImageCanvasNeuron.cs`.
- Modify `src/Modules/Flutter/Contracts/UIVocabulary.cs` and `Flutter/Shared/UiKitEndpoints.cs`.
- Create `src/Modules/Flutter/Tests/Unit/Composition/CompositionFacts.cs`.

**Interfaces:** Produces the four generic UI contracts above; consumes existing UiChildRef, state persistence and UI event conventions. CollectionActivated carries child identity, item ID and displayed revision; ImageEditRequested carries document revision and typed command. Neither signal performs filesystem work.

- [ ] Add failing persistence and revision tests using the existing module test harness. Assert ordered child references survive grain deactivation; stale writes fail without replacing current state; item activation identifies the item and revision.

```csharp
// Within the existing UnitTest.WithModule<FlutterModule>() fixture:
var surface = brain.Get<ISurface>("owner/a/apps/files/surface");
var before = await surface.Read();
await surface.Set(new("Files", [new("layout", "owner/a/apps/files/root")]), before.Revision);
var after = await surface.Read();
Assert.Equal("layout", Assert.Single(after.Children).Kind);
Assert.True(after.Revision > before.Revision);
```

- [ ] Run the Flutter module unit test project and observe failure for the missing contracts. Implement the four small stateful neurons using existing Card/Workspace patterns; allocate stable serialized field IDs and explicit revision-conflict behavior.
- [ ] Add limits: maximum 128 children per node, nesting depth 16 in resolved rendering, allowed layout modes and nonnegative finite sizes. Cross-workspace references fail scope validation at composition dispatch. Cycles and missing children produce a recoverable error child, not recursive rendering.
- [ ] Rerun unit tests, including deactivation and malformed state cases. Checkpoint the contract implementation separately from app behavior.

## Task 2: Add IntoChat's local filesystem boundary

**Files**
- Create `src/Applications/IntoChat/IntoChat/LocalFiles/LocalFilesOptions.cs`, `ILocalFileStore.cs`, `LocalFileStore.cs`, `LocalFileDtos.cs`, `LocalFileEndpoints.cs`.
- Modify `src/Applications/IntoChat/IntoChat/Program.cs`, `Configuration/IntoChatConfiguration.cs` and `src/Applications/IntoChat/AppHost/AppHost.cs`.
- Create `src/Applications/IntoChat/Tests/E2E/LocalApps/LocalFileBoundaryFacts.cs` and `LocalFilesFixture.cs`.
- Modify `src/Applications/IntoChat/Tests/E2E/IntoChatE2ETest.cs` to merge optional private configuration into existing provider overrides.

**Interfaces:** ILocalFileStore exposes ListAsync(scope, folderId, cursor, sort, filter, ct), SnapshotImageAsync(scope, entryId, ct), OpenAssetAsync(scope, assetId, ct), and SaveCopyAsync(scope, ticket, pngStream, ct). Results are paged DirectoryPage, immutable ImageAsset, readable Stream and SavedFile respectively. Define records in LocalFileDtos.cs. File handles bind root-relative paths and workspace authorization; raw paths never enter public requests.

- [ ] Add temporary-root tests first: list Unicode and spaced filenames, navigate a folder, page results, reject `..`, absolute/UNC paths, alternate-data-stream syntax, forged handles and another workspace's asset ID. Assert a junction/reparse child cannot escape the root. Skip a link-creation test only when the OS denies creating its fixture, with an explicit reason; keep path rejection tests unconditional.
- [ ] Test rename/delete between list and snapshot returns a specific stale/not-found result. Test source exceeding byte/dimension limits is rejected before copying/decoding; inaccessible roots return an actionable unavailable state.
- [ ] Configure `IntoChat:LocalFiles:Roots:downloads` and separate `IntoChat:LocalFiles:AssetDirectory`. Pass the actual development Downloads path through host configuration, never a checked-in username. Tests use a unique temporary Downloads and asset directory.

```csharp
var overrides = new Dictionary<string, string?> {
    ["IntoChat:LocalFiles:Roots:downloads"] = fixture.Downloads,
    ["IntoChat:LocalFiles:AssetDirectory"] = fixture.Assets
};
// New optional Create argument merges this with existing provider configuration.
var test = IntoChatE2ETest.Create(privateConfiguration: overrides);
```

- [ ] Implement root-constrained traversal and immutable asset snapshots with streaming and bounded reads. Reject reparse segments below configured roots; canonicalize configured roots and verify opened handles' resolved targets before reading/writing. Use a source fingerprint and copy consistency check; retry/report a changed source rather than combining bytes from two versions.
- [ ] Map authenticated, scoped listing/asset routes through the active Program registration. Avoid logging file contents or returning absolute paths in errors. Add paged results (default 100, maximum 500), metadata-only browsing and explicit refresh.
- [ ] Run boundary tests against the actual IntoChat host and checkpoint. No Image Editor UI is needed for this task's pass criteria.

## Task 3: Implement Files and image-document neurons in IntoChat

**Files**
- Create `src/Applications/IntoChat/IntoChat/Apps/Files/IFileExplorer.cs`, `FileExplorerNeuron.cs`, `FileExplorerState.cs`.
- Create `src/Applications/IntoChat/IntoChat/Apps/Images/IImageDocument.cs`, `ImageDocumentNeuron.cs`, `ImageDocumentState.cs`, `ImageSaveCoordinator.cs`.
- Create `src/Applications/IntoChat/IntoChat/Apps/BundledAppCatalog.cs`, `AppSurfaceComposer.cs`, `AppActionDispatcher.cs`, `AppEndpoints.cs`.
- Modify active `IntoChat/Program.cs` registration.
- Create `src/Applications/IntoChat/Tests/E2E/LocalApps/LocalAppNeuronFacts.cs`.

**Interfaces:** Consumes Task 1 UI contracts and Task 2 file service. Produces IFileExplorer/IImageDocument, SaveTicket and app actions from the shared contract section. AppActionDispatcher explicitly maps scoped child events to app operations; binding definitions are application-owned, not arbitrary client code. BundledAppCatalog has two enabled apps and the disabled studio entry.

- [ ] Write failing host tests: opening the same file twice focuses/reuses its document; different images create document tabs; grains recover recipe/source references after deactivation; two workspaces cannot operate on each other's children/documents. Opening a changed file offers reload as a new snapshot without replacing an edited document silently.
- [ ] Implement app composition. Files creates stable toolbar, breadcrumb, collection and status children under its Surface. Image Editor creates stable Tabs, toolbar Layout, ImageCanvas and inspector children. Files OpenImage snapshots bytes and opens a document; button and canvas events dispatch typed commands to its neuron.
- [ ] Implement edit revision checks and operation receipts. Debounce transient pointer motion locally, persist one complete AddStroke per gesture and one SetCrop per Apply. A failed commit remains visibly unsaved and retryable; closing/reloading must not silently claim it was persisted.
- [ ] Implement save preparation, upload and completion: capture recipe/revision, raster export in Task 4, stream PNG to a private temporary file, validate PNG signature/dimensions/size, then atomically publish a new filename without overwriting. Persist the chosen destination, digest and operation status before publication so recovery can reconcile a completed rename. Repeated operation IDs with different payloads fail; matching retries return the original SavedFile. Upload failure allows an explicit retry with the same operation; expired tickets can be renewed for the same captured revision.
- [ ] Add tests for duplicate save/restart recovery, collision naming, malformed PNG, cancelled upload, and edits arriving during save. Assert LastSavedRevision equals the captured revision while newer edits stay dirty. Assert original bytes unchanged. Verify saved-file refresh notification only occurs after publication.
- [ ] Run LocalAppNeuronFacts and boundary regression tests; checkpoint. No live model or Compute deduction in any operation.

## Task 4: Build the reusable Flutter image canvas and raster exporter

**Files**
- Create `src/Modules/Flutter/app/ui/lib/src/components/image_canvas/image_recipe.dart`, `image_canvas_controller.dart`, `ui_image_canvas.dart`, `image_scene_painter.dart`, `image_exporter.dart`.
- Export through `src/Modules/Flutter/app/ui/lib/digitalbrain_ui.dart`.
- Create `src/Modules/Flutter/app/ui/test/image_canvas/image_geometry_test.dart`, `image_export_test.dart`, `image_canvas_test.dart`.

**Interfaces:** ImageCanvasController consumes immutable source dimensions/recipe/document revision; emits typed ImageEditCommand. ImageExporter.renderPng(source, recipe) returns encoded PNG bytes. Painter and exporter share scene drawing; view transform is presentation-only. Export result is uploaded with Task 3's SaveTicket.

- [ ] Add geometry tests before rendering: inverse viewport transform maps a pointer under zoom/pan to source coordinates; crop bounds clamp inside the source; invalid/zero dimensions fail; changing zoom never changes the recipe.

```dart
test('viewport pan and scale do not change image coordinates', () {
  final transform = Matrix4.identity()..translate(40.0, 20.0)..scale(2.0);
  final controller = TransformationController(transform);
  expect(controller.toScene(const Offset(100, 60)), const Offset(30, 20));
});
// Production tests also exercise ImageCanvasController's gesture adapter,
// including the canvas origin and any Fit padding, not only this framework call.
```

- [ ] Build pan/select/pen tool separation so drawing cannot simultaneously pan. Add Fit, 100%, keyboard zoom, color/width, accessible crop fields, Apply/Cancel, bounded undo/redo and Reset. Crop handles and overlays must remain legible across zoom. Persist source coordinates and image orientation consistently.
- [ ] Share a Canvas painting routine between screen and export; export crops by clipping and translating the output canvas. Use PictureRecorder → Picture.toImage → Image.toByteData(PNG). Dispose decoded images and intermediate pictures/images when no longer used; cancel stale source loads and disable Save while a source or edit commit is incomplete.
- [ ] Add a generated 120×80 colored fixture test: crop `(10,10,60,40)` yields a 60×40 PNG, pen pixels appear at expected interior coordinates, pixels outside the crop disappear, and zoom 1 versus zoom 2 yields identical export bytes/pixels. Test undo/redo/Reset and an orientation-tagged JPEG fixture. Check pixels away from antialiased edges.
- [ ] Test invalid and oversized headers before full decode, export error leaves edits intact, and switching tabs during an export does not mix documents. Run `flutter test test/image_canvas` from the ui package and checkpoint.

Official API references checked while planning: [InteractiveViewer](https://api.flutter.dev/flutter/widgets/InteractiveViewer-class.html), [PictureRecorder](https://api.flutter.dev/flutter/dart-ui/PictureRecorder-class.html), [Image.toByteData](https://api.flutter.dev/flutter/dart-ui/Image/toByteData.html). Context7 resolution was attempted but its monthly quota was exhausted. Recheck through Context7 at implementation if available.

## Task 5: Render app surfaces and route real child interactions

**Files**
- Create `src/Modules/Flutter/app/ui/lib/src/composition/neuron_view.dart`, `neuron_view_registry.dart`, `ui_surface.dart`, `ui_layout.dart`, `ui_collection.dart`.
- Modify `app/ui/lib/src/models/ui_part.dart`, `src/components/card/ui_card.dart`, `digitalbrain_ui.dart` under the same module package.
- Create `src/Modules/Flutter/app/core/lib/src/models/app_models.dart`, `src/app_client.dart`; modify `src/ui_client.dart` for consistent authenticated transport reuse.
- Create `src/Modules/Flutter/app/ui/test/composition/neuron_view_test.dart` and `src/Modules/Flutter/app/shell/lib/workspace/app_surface_host.dart`.

**Interfaces:** NeuronView receives UiChildRef plus a state/interaction adapter; registry resolves allowed kinds. AppClient consumes Task 3 DTOs and exposes read/subscribe/action/asset/upload methods. Child subscriptions unsubscribe on unmount; minimized app documents remain durable without unnecessary canvas painting.

- [ ] Write renderer tests for nested Surface → Layout → Collection/ImageCanvas, live state revision updates, ordered Card children, loading/empty/error states and controlled unknown-kind/cycle errors. Assert activating a collection row emits its ID/revision, not a filesystem path.
- [ ] Implement generic host rendering and revision-aware subscriptions; refetch on reconnect and ignore older snapshots. Feed authenticated image bytes to the canvas instead of unguarded URL fetches. Do not put file operations in ui_collection.dart.
- [ ] Wire crop/pen commits and Save copy through AppClient, with disabled duplicate-click state, visible errors, transient progress and saved-revision feedback. Surface host handles document tabs and scoped app dispatch without embedding a second Files/Image Editor business model.
- [ ] Add widget tests for keyboard activation, tooltips, crop field labels, tab switching and failed edit/upload retry. Run composition/image tests and checkpoint.

## Task 6: Install apps in the workspace-first floating shell

**Files**
- Modify `src/Modules/Flutter/Contracts/Workspace/IWorkspace.cs`, `WorkspaceWindow.cs`, `Flutter/Workspace/WorkspaceNeuron.cs` and add `Contracts/Workspace/OpenSurfaceWindow.cs`.
- Modify `src/Modules/Flutter/app/core/lib/src/models/workspace_models.dart`.
- Modify `src/Modules/Flutter/app/shell/lib/workspace/workspace_app.dart`, `workspace_store.dart`, `workspace_desktop.dart`, `artifact_editors.dart`.
- Create adjacent `workspace_islands.dart`, `application_launcher.dart` and `app_window_controller.dart`.
- Extend `src/Modules/Flutter/Tests/Unit/Workspace/WorkspaceFacts.cs` and add `app/shell/test/workspace/local_app_shell_test.dart`.
- Update `src/Applications/IntoChat/Tests/E2E/Workspace/WorkspaceBrowser.cs` and affected navigation tests, including `WorkspaceRestoreFacts.cs`.

**Interfaces:** Add `IWorkspace.OpenSurface(OpenSurfaceWindow request)` without changing existing OpenWindow/TableViewReference serializer field types. Add an optional Surface reference to WorkspaceWindow at a new field ID; retain legacy table view representation and define exactly-one-active-view validation. Old snapshots must deserialize unchanged. AppWindowController uses existing WorkspacePresentation bounds/minimized/restore state.

- [ ] Test old serialized table state first, plus opening surface windows with idempotent operation IDs and revision conflicts. Implement the additive workspace contract and Dart parser fallback to the old table reference.
- [ ] Remove directory/homepage startup: resolve last valid workspace, create Personal only when none exists, route old `/projects` entry to workspace. Workspace island exposes switch/create and existing saved-item reopening. Update test helpers to use that path while preserving isolation/reload assertions.
- [ ] Build the three floating islands matching `design-prototype/three-islands.css` and the approved mockup. Use Flutter theme spacing/radii, subtle borders, backdrop blur with opaque fallback, visible focus and reduced motion. Collapse islands sensibly on narrow screens without covering editor actions. Add top/bottom and Assistant left/right preferences to WorkspacePreferences.
- [ ] Install Files/Image Editor; studio is visibly disabled. App launch fills the available work area, minimizes other apps, preserves Assistant and remembers explicit Restore choice. Reopening an app focuses it; documents use tabs. Replace stale screenshot-specific shell labels in tests rather than relaxing their assertions.
- [ ] Show a real Compute value only if the existing source supplies it; otherwise show `Compute —`. Search handles apps and workspace items without introducing recursive filesystem search. Keep settings/profile in the right island.
- [ ] Run shell widget tests and existing workspace restore tests; checkpoint. Verify old table and legacy artifact editors still open. Do not remove their underlying code merely because the homepage is gone.

## Task 7: Prove the two-app workflow in IntoChat E2E

**Files**
- Create `src/Applications/IntoChat/Tests/E2E/LocalApps/LocalAppsJourneyFacts.cs`, `LocalAppsBrowser.cs`.
- Extend `LocalFilesFixture.cs`, `LocalFileBoundaryFacts.cs`, `LocalAppNeuronFacts.cs` from Tasks 2–3.
- Update `docs/intochat-local-apps-design.md` with verified behavior/limitations after execution.

**Interfaces:** Uses real IntoChatE2ETest, temporary root configuration, Flutter.RunWebApp, semantic UI controls, backend grains and exported filesystem bytes. No in-memory fake filesystem or mock editor replaces the main journey.

- [ ] Seed a deterministic small PNG, a nested folder, a text file and collision filename in a disposable root. Store source hash before launch. Start the actual application graph using the existing harness and disabled external-provider endpoints.

```csharp
await using var brain = await IntoChatE2ETest.Create(privateConfiguration: overrides)
    .ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp()).StartAsync(ct);
var page = brain.Page;
await page.SetViewportSizeAsync(1600, 1000);
await page.GetByRole(AriaRole.Button, new() { Name = "Applications", Exact = true }).ClickAsync();
await page.GetByRole(AriaRole.Button, new() { Name = "Files", Exact = true }).ClickAsync();
await page.GetByRole(AriaRole.Button, new() { Name = "Open Untitled.png", Exact = true }).ClickAsync();
await Assertions.Expect(page.GetByRole(AriaRole.Region,
    new() { Name = "Image Editor", Exact = true })).ToBeVisibleAsync();
```

- [ ] Through the UI, zoom/pan, draw a stroke, undo/redo, apply crop via numeric controls, Save copy, return to Files and open the result. Use deterministic canvas bounds and pointer positions; do not mutate the recipe directly to stand in for drawing. Assert only one Image Editor app window and that Files minimized on launch.
- [ ] Read the output from the temporary filesystem, assert PNG dimensions and decode it in a browser offscreen canvas for selected pixel assertions. Compare the original SHA-256 before/after. Confirm the output filename avoided the seeded collision and that reopening/reloading restores the committed recipe.
- [ ] Exercise missing source, unsupported file, stale document revision and save failure; edits must remain recoverable. Exercise a second save retry with the same operation ID against the real endpoint and assert one file. Add a concurrent edit during save assertion at the neuron layer where it can be controlled deterministically.
- [ ] Verify no homepage at startup, workspace switching preserves old table tests, studio cannot launch, Assistant/dock side preferences restore, and Compute does not decrement after local operations.
- [ ] Run the IntoChat E2E project with the repository's MTP runner configuration. Use `dotnet test --project src/Applications/IntoChat/Tests/E2E/IntoChat.Tests.E2E.csproj` if the repository SDK uses MTP's project flag; otherwise use its existing documented runner invocation. Do not assume VSTest `--filter` works with xUnit v3 MTP. Read the Aspire skill before manually starting any runtime; let the test harness own its instances and cleanup.
- [ ] Run the affected Flutter unit/widget tests and existing workspace/table E2E regressions once the journey passes. Record commands and actual results, not just screenshots.
- [ ] Manual local acceptance: configure this user's Downloads, open `C:\Users\vhorb\Downloads\Untitled.png` through Files, pan/zoom, draw/crop and save a clearly named new copy. Verify original hash unchanged and output opens. This supplied file exists and is 11,875,509 bytes; its decode dimensions and performance have not yet been measured. Never use it as an automated test fixture.

## Completion evidence

Deliver a short recording/screenshots of the Files → Image Editor → saved-copy path, the exact test commands/results, and the new output path from manual acceptance. Report actual limitations (for example local-host-only access and session-only undo). Do not describe app generation, AI editing, cloud files or Compute charging as shipped.

## Plan self-review

Scope mapped: reusable Flutter neurons (1, 4, 5); IntoChat neurons and filesystem (2, 3); workspace-first visual design and compatibility (6); real cross-app E2E and supplied-file acceptance (7). Review-focus cases have explicit owning tests. Proposed contracts are distinguished from existing APIs; no excluded legacy endpoint is used as a foundation. This document preserves the approved plan. Execution results, consolidated file organization and verified limitations are recorded in `docs/intochat-local-apps-implementation.md`.
