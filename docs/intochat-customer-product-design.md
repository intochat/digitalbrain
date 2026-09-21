# IntoChat customer product design

Status: proposed product and architecture, 21 September 2026. Grounded in the current repository and the selected three-island HTML design. This document does not claim that proposed services already exist.

Architecture refinement: see [Applications composed of neurons](intochat-neuron-app-composition.md) for Surface/Layout, behavior wiring, app definitions and the recursive Flutter host. A subsequent renderer inspection found that the current `UiCardPart`/`UiCard` path does not render backend child references; end-to-end child composition must be implemented, not assumed.

## Release decision

The first customer release contains five complete core applications: **Image Studio, Files, Tables, Notes, and Automations**. They share one workspace, Assistant, application platform, operation runtime, connection system, and Compute ledger. Custom apps compose those capabilities. A broad catalog is a later expansion of this same platform, not a collection of unrelated demo windows.

The product promise is: describe an outcome, watch the work happen in the relevant application, inspect the result, and keep or reuse it. Assistant is an alternate way to operate the applications, not a separate system that produces disconnected chat attachments.

## Workspace entry and shell

Remove the projects homepage from the customer journey and production shell. Preserve all existing project data, IDs, deep links, and organization. A project becomes a workspace in customer-facing navigation.

- Returning user: restore the last accessible workspace, active conversation, open artifacts, window geometry, selected app, dock edge and Assistant side.
- New user: create a Personal workspace, show Assistant with a concise welcome, and offer Import files or Create something. No marketing heading or template marketplace before work starts.
- Old `/projects` URL: redirect to the last accessible workspace; a missing or inaccessible workspace falls back to an accessible one or offers creation without exposing another user's data.
- Keep three floating islands on one edge. Left: IntoChat/workspace. Center: Assistant, open apps, Applications, Search. Right: real Compute balance, Settings, profile. The user can move all islands to the top.
- Applications opens a searchable popover with Installed, Recent, and Create app. Built-in applications and installed custom apps follow the same launch contract.
- First app launch fills the available work area and minimizes other windows. Restore returns to floating geometry; later switches respect that choice. Assistant remains available. Closing a document window does not delete the artifact or cancel a job.
- Progress, completion, required input, and failure appear on the relevant app and in background activity. Show a small badge only when useful; do not show permanent decorative system status.
- On phones, switch between Assistant and the active app instead of stacking a giant desktop above chat. Collapse island labels and expose overflow without hiding the balance or trapping controls off-screen.

## The five applications

| App | Customer experience | Existing foundations | Work needed for release |
|---|---|---|---|
| Image Studio | Import, preview, crop/rotate, adjust, improve quality, remove background, compare versions, export/save copy | Image neuron; current artifact image editor imports, rotates and adjusts brightness | Durable image assets, editing providers, jobs, compare surface, exported edits, version history and Drive import/export |
| Files | Search, preview, organize, import/upload, open with an app, save a copy to a connection | Tree neuron; workspace artifacts; local file import | File/asset identity, storage, access control, metadata, Drive connection, paginated browsing and synchronization rules |
| Tables | Import CSV, edit rows, select/filter/sort, perform supported actions on selection, export | Table/Sheet neurons, existing table controller and CSV-related shell code | Complete consistent UI, shared Assistant selection context, conflict recovery, durable actions, large-data behavior |
| Notes | Create and edit documents, organize sections, insert artifact references, receive Assistant edits, restore versions, export | Text neuron provides markdown state; existing artifact infrastructure | Editable document model, versioned save and patch operations, editor integration, conflict handling; Text display alone is not a full editor |
| Automations | Choose trigger, input, actions and destination; test once; inspect cost and run history; enable, pause, retry | Existing WorkspacePrograms UI includes intent compilation, validation, deployment and runs; Button/Toggle/Card/Tabs neurons | Customer editor over validated behavior contracts, supported triggers, permissions, durable jobs, schedules, run limits and understandable failure states |

Every app must support loading, empty, offline/reconnecting, permission-denied, expired connection, operation failed, canceled, and read-only states where applicable. Do not list a working-looking app action that has no configured backend capability. Explain what connection or capability is missing and provide a specific next step.

### Image Studio layout

Use a compact title/toolbar with file name and save state; a large central image canvas; a contextual adjustments panel; and an optional history rail. Keep the tool list short: Crop/rotate, Adjust, Enhance, Remove background. Show advanced controls only for the selected tool. Before/after comparison is a purposeful viewer control, not an image decorated as a card. A result appears as a new version; the original remains recoverable.

Basic local edits should preview immediately. Expensive operations show scope, estimated Compute and a maximum approved amount. Do not promise an upscaling factor, output resolution or background quality the chosen provider cannot deliver. Use provider capability metadata to configure available options.

## One complete customer journey

Example: “Open the product photo from my Drive, remove the background and improve its quality.”

1. Resolve the connected Drive account and candidate files. If multiple files match, Assistant presents a compact selection card with thumbnails and names. Never silently choose an ambiguous file.
2. Files resolves a selected external reference into an authorized asset handle with source revision and provenance. Tokens stay in the connection service.
3. Assistant opens that asset in Image Studio. Its request includes the artifact ID, version and selected image; no duplicate private copy of the app state is maintained in chat.
4. Compose the requested operation sequence using available capabilities. Show one consolidated estimate for the chain, its maximum spend and destination. If the user has already authorized this operation within a standing budget, execution can proceed within that policy without repetitive confirmations.
5. Reserve Compute atomically. Reject insufficient balance before the paid operation begins. Submit a durable job with an idempotency key and expected input version.
6. The image app and Assistant subscribe to the same job. Show genuine progress or an indeterminate stage indicator; do not invent percentages. The user may continue working or minimize the app.
7. Store each successful output as a version linked to the original asset and operation. Show a before/after preview with Keep version, Retry, and Export/Save copy. If enhancement fails after background removal succeeds, preserve the completed intermediate version and charge according to the stated policy.
8. Settle actual usage, release unused reservation and update the balance everywhere. An ambiguous provider result stays pending reconciliation rather than charging again on retry.
9. “Save it back to Drive” creates a new copy by default. Replacing the original is a distinct action that checks source revision and handles a concurrent remote edit.

Google Drive is proposed here as an in-product integration. The current Google module inspected in this repository implements Gmail; it does not establish that Drive access is implemented. Installing a connector into the development assistant would not implement the customer's product integration.

## How neurons actually participate

Separate four responsibilities:

| Layer | Owns | Does not own |
|---|---|---|
| Flutter shell | Workspace selection, island placement, app launch, focus, window geometry and minimization | Provider credentials, billing truth, background execution |
| UI composition | Registered Flutter controls rendered from typed neuron references and layout definitions | Arbitrary generated Flutter code, unrestricted method invocation |
| Application/document state | Asset/document identity, revisions, selection, editing state, supported domain actions | Window position or account balance |
| Operation runtime | Authorization, cost estimate/reservation, durable work, provider execution, output and settlement | Rendering widgets |

Existing `UiChildRef(Kind, Name)` references a UI neuron. `CardState.Children` composes controls, and `TabItem` associates a title with a child. Reuse that vocabulary for component identity and subscription. Keep a stable Flutter renderer registry by kind. Unknown or unavailable kinds render a clear unsupported-component state instead of breaking the entire app.

The existing vocabulary includes Image, Text, TextField, Slider, Button, Toggle, Progress, InfoBar, Card, Tabs, Tree, Table, Sheet, Chart, Graph, Calendar and other components. Use them where their semantics match. Do not try to build a professional image editor from dozens of arbitrary Cards: introduce a reusable image-canvas/compare renderer and typed editing contract where the existing Image display contract is insufficient. New specialized components should become reusable supported neuron kinds, not exceptions embedded in one app.

Example Image Studio composition: an image-document root owns original/current asset references and history; Image displays an output; Slider represents adjustment input; Buttons invoke registered editing actions; Progress/InfoBar reflect an operation; Tabs selects tool panels; Tree presents versions. The image-document and operation contracts are proposed additions. A Button click expresses an authorized domain command; it does not carry an executable script supplied by the model.

Assistant and manual controls submit the same commands with the same actor, permissions and expected document revision. A selection context contains explicit workspace/app/artifact references and selected entities. Reading a document does not grant permission to mutate it. Revision conflicts return recoverable conflict information rather than overwriting a newer user edit. Pin each job's input revision so a late result does not replace unrelated work.

Distinguish declarative durable state from ephemeral interaction state. Text caret, hover, image pan/zoom and live slider preview stay local where appropriate. Persist meaningful document edits and view preferences intentionally. Avoid a server round-trip for every animation frame or pointer movement.

## Shared contracts to introduce or generalize

Names below describe proposed responsibilities, not existing public APIs.

- **App definition:** ID, version, display name, icon, supported document kinds, root UI definition, typed action bindings, required capabilities and permissions, lifecycle/migration metadata. Built-in and custom definitions share validation rules; trusted bundled apps may supply richer registered renderers.
- **App instance:** workspace, app/version, root neuron references, document references, instance lifecycle. Distinct from a window: reopening a window should not create an accidental duplicate instance.
- **Artifact/asset:** tenant/owner, stable ID, kind, content revision, storage handle, MIME type, size, provenance and version links. An external file reference records provider/account/file ID and remote revision; it is not a durable signed URL or a credential.
- **Window view reference:** generalize the current table-only reference to a discriminated, versioned view reference. Preserve compatibility with existing `TableViewReference` and saved windows. Keep the existing operation receipts and expected-revision protection.
- **Action/operation:** command ID, caller, workspace, app/document version, capability, validated arguments, input assets, authorization and budget scope, output contract.
- **Job:** queued/running/waiting-for-input/succeeded/failed/cancel-requested/canceled, progress stage, output versions, error class, retry lineage and metering reference. “Cancel requested” must not claim the provider has stopped.
- **Connection reference:** service/account, granted scopes, connection state and credential-service reference. Secrets never enter generic UI state or chat.
- **Compute ledger:** authoritative balance, reservation, measured usage, settlement/release/refund, currency/unit conversion policy if used, and immutable operation linkage.

## Custom apps

Build an **App Builder** over reusable capabilities rather than allowing unrestricted generated code in the first release. A customer says, for example: “Make a catalog prep app that takes a folder of photos, removes backgrounds, and puts the results and status in a table.”

The builder produces a draft app definition with input schema, document model, a neuron-based UI, registered action bindings and workflow. Preview with sample data; report unavailable capabilities explicitly. Validate component kinds, references, cycles/limits, action input/output types, permission requirements and operation budgets. Then offer Install to this workspace with the proposed permissions visible. Installation adds the app to Applications; it does not grant broader connector scopes implicitly.

Allow the user to revise the app conversationally. Changes create a new draft version and a migration plan when the data model changes. Keep the installed version working until the new version validates and is activated. Pin running jobs to their original app version. Provide rollback for definitions; data rollback needs an explicit migration strategy. Uninstalling the app should explain which artifacts and schedules are retained.

The existing `WorkspacePrograms` screen already calls compile/validate/deploy-style endpoints and displays versions and runs. Inspect and extend that runtime before introducing a competing workflow engine. Its presence does not prove that it already validates UI app manifests, enforces all connector permissions, or provides the required billing/job semantics.

First-release custom apps compose supported operations across the five core apps. Requests for unsupported capabilities become a clear limitation or a draft requiring a new capability. Later, sandboxed generated code can be a separate capability with isolation, resource limits and review—not the default escape hatch for missing features.

## Compute and trust

Always show the available Compute balance in the account island. During paid work, expose reserved and available amounts in the balance popover. Show the task estimate before execution and actual usage afterward. Simple navigation, focus, local previews and opening an existing document should not look like paid AI work; finalize and publish the actual charging policy before launch.

Reserve → execute → meter → settle is the normal lifecycle. Concurrent jobs cannot overdraw an account through competing stale balance checks. Replay/reconnect/retry cannot duplicate charges. Unused reservations are released on completion or failure. Canceled work may already have incurred provider costs: explain that before execution and apply a consistent published rule. Support spending limits for a task, automation run and workspace. Auto-recharge is opt-in and must have an explicit cap.

The current password-kind TextField neuron persists values and emits them in a change signal. Real connector credentials require a separate secret submission/authorization path. Masking text is not enough. Likewise, the browser must never calculate the bill or mutate the authoritative balance.

## Inbox and customer attention

Include an attention Inbox in background activity, even though it is not one of the five primary launch apps. Show actionable items: an operation needs input, output is ready, connection expired, budget reached, automation failed. Each item opens the exact app/document/run and has read/resolved state. Group repeated errors. Successful background runs should not interrupt current work unnecessarily.

The existing `IInbox` supports `Appear(string)` and reading strings. Preserve compatibility if it is extended, but introduce structured item identity, kind, timestamp, source references, available actions and read/resolution state. Do not encode the new product protocol as ad hoc strings.

## Implementation sequence

1. **Workspace shell:** replace homepage entry and solid chrome with the selected islands; preserve migration/deep links; add app catalog, shared focus policy, dock/Assistant preferences and responsive navigation. Use truthful unavailable balance state until a ledger is connected.
2. **App hosting and data:** generalize view references; add validated app definitions/instances; unify artifact identity, selection context and stable neuron renderers; make Files/Tables/Notes complete against these contracts.
3. **Operations and Compute:** durable jobs, idempotency, authorization, reservations, settlement and observable progress. Build failure/reconnect/retry behavior before adding multiple paid providers.
4. **Image Studio and Drive:** image-document versions, local edits/export, authorized Drive browsing/import, editing-provider adapters, background removal/enhancement and save-copy flow. Verify the complete customer journey above.
5. **Automations and App Builder:** reuse the inspected programming runtime, add customer-facing workflow UI, schedule/budget semantics, draft preview, validation, install and version migration. Reuse core apps rather than fork their logic.
6. **Release validation:** cross-app workflows, recoverable errors, permissions, cost accounting, accessibility, performance and onboarding. Populate a curated library of useful built-in app/workflow templates only when they run end-to-end.

These are implementation dependencies, not a reduction of the agreed five-app release. The first implementation slice should be the workspace shell plus a real generic app-hosting path; paid workflows then attach to it.

## Release acceptance examples

- Existing customers reach their previous workspace without losing projects, routes or artifacts. New customers can import a file and do useful work without a setup tour.
- Manual Image Studio controls and Assistant commands produce the same versioned results. Moving/minimizing a window never restarts the operation.
- A Drive import handles ambiguous names, missing permissions, expired connection and changed/deleted files without silent substitution or leaked access.
- A user can edit a note/table while Assistant works; a stale operation cannot overwrite those edits undetected.
- A repeated network request, browser reconnect or provider retry cannot duplicate a charge or create duplicate outputs unintentionally.
- An automation survives UI closure, can be paused, exposes a truthful run state, honors budget limits and routes failures to the correct Inbox item.
- A custom catalog-prep app can be described, previewed, validated, installed and reopened. Its permissions and version are inspectable, and an invalid update leaves the installed app usable.
- All five apps pass keyboard, focus, text scaling, contrast, screen-reader and narrow-screen checks. Large assets and tables have explicit limits and responsive loading.

## Repository evidence inspected

- `src/Modules/Flutter/app/shell/lib/workspace/workspace_app.dart`: `_directory` entry state, homepage button, routes, program client and artifact callbacks.
- `src/Modules/Flutter/app/shell/lib/workspace/workspace_desktop.dart` and `workspace_store.dart`: stable editors, geometry, minimization, modes and saved preferences.
- `src/Modules/Flutter/app/shell/lib/workspace/artifact_editors.dart`: current image import, rotation and brightness; table/chart/other artifact renderers.
- `src/Modules/Flutter/app/shell/lib/workspace/workspace_programs.dart`: intent draft compilation, validation, deployment, versions and run UI.
- `src/Modules/Flutter/Contracts/Shared/UiChildRef.cs`, `Card/ICard.cs`, `Tabs/ITabs.cs`, `Text/IText.cs`, `Tree/ITree.cs`, `Image/IImage.cs`, `Inbox/IInbox.cs`, `Workspace/TableViewReference.cs`.
- `src/Modules/AI/Contracts/IImageGeneration.cs` and `AI/Clients/OpenAIImageGeneration.cs`: prompt-based generation contract; this inspected API does not supply source-image editing.
- `src/Modules/Google`: inspected integration surface is Gmail. A targeted source search did not identify an existing Compute ledger; confirm the whole application/service inventory before creating new storage or services.

Provider/model choice, pricing, storage deployment and credential-service implementation require targeted technical validation before coding those integrations. No particular provider performance or price is assumed in this design.
