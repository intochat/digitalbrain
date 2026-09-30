# Unified first-party apps

Status: ratified 2026-09-30 (owner). Extends the 2026-09-29 programmable-brain standard.

## Problem

Four coexisting app mechanisms contradict the one-app-model standard:

1. Compiled `IApplication` projects under `src/Apps/` (Assistant, CustomerResearcher) composing UI
   through the host-side `UiComposer` builder (`.Ui(ui => ui.Surface(...))`) — a second UI grammar
   the 2026-09-29 spec rejected, outside the spec→tests→behaviors pipeline.
2. Manifest-only folders under `src/Apps/` (`files`, `forms`, `image-editor`, `customer-tables`):
   hand-written `app.json` files embedded into `DigitalBrain.Modules.Apps` as the hardcoded
   `FirstPartyApps` list, fronting capabilities that actually live in the Files, Supabase and
   Flutter modules.
3. Host-resident built-ins: `IntoChat/Apps/BuiltIn/SettingsApp` and `BuiltInAppEndpoints`
   hardcoding `AssistantApp` — app code living in the product host, unlike modules (e.g. Google),
   which own their endpoints.
4. Shipped packages under `src/Applications/IntoChat/Apps/` (Assistant, GroupChat, WordCount) —
   the ratified shape: `app.json` + `app.spec.md` + `tests.cs` (+ behaviors) published through
   `ShippedAppPublisher` via commit→verify→publish.

## Decision

**One app mechanism: the package.** Every first-party app is a package — manifest + spec + tests +
behaviors — through the same gate users' apps pass. If the paradigm cannot express our own apps,
users cannot build with it either; growing module contracts, not compiled escape hatches, closes
expressiveness gaps.

- `app.json` stays, as a **package file**: the app's public face for the launcher (name,
  description, `uiEntry`, example prompts), the model (`descriptionForModel`, operations,
  `agentTools`), consent (permissions, meters) and the install catalog. The Builder generates it,
  verification validates it, the catalog serves it. The embedded `FirstPartyApps` list dies;
  discovery reads the manifest directory / catalog only.
- The four manifest-only pseudo-apps are deleted. Their logic already lives module-side and stays:
  Files module (`FileExplorerNeuron`, `ImageDocumentNeuron`, surfaces, endpoints), Supabase module
  (live tables + agent tools — the assistant→database→UI-table flow is unaffected), Flutter module
  (`FormNeuron` renderable). If "Files" et al. return as installable apps, they are authored as
  real packages over those module contracts.
- The compiled Assistant and CustomerResearcher are rebuilt as packages. **Full parity is required
  before the compiled Assistant is deleted** (voice input, attachments, receipts, model picker,
  agent tools, live results). Migration order: grow module contracts first (agent tool factories,
  voice/file-input/receipt/model-catalog contracts reachable from behavior scripts — the same
  `ISurface`/`ILayout`/`IButton`/`IVoiceInput` grains `UiComposer` wraps today), then author the
  package against them, prove parity in its `tests.cs` scenarios, then delete the compiled app,
  `UiComposer`/`.Ui()` and the `IApplication` runtime.
- `SettingsApp` + `BuiltInAppEndpoints` are rebuilt as a Settings package; the built-in mechanism
  is deleted.
- `IntoChat/Packages/` (the HTTP layer over `IPackage`) is legitimate host surface and stays.

## Phases

1. **Manifest unification** — delete `src/Apps/{files,forms,image-editor,customer-tables}`,
   `FirstPartyApps` and its embeddings; consent and the manifest directory resolve from
   catalogued/published manifests only; shell launcher lists module surfaces (files, images,
   csharp) as shell entries plus catalog apps, no first-party manifest special-casing.
2. **Module contract growth** — everything the compiled Assistant reaches through DI or `.Ui()`
   becomes module contracts callable from behavior scripts.
3. **Package rebuilds** — Assistant (parity-gated), CustomerResearcher, Settings as packages via
   `ShippedAppPublisher`.
4. **Deletions** — `src/Apps/` tree, `IApplication`/`ApplicationCatalog`/`UiComposer`,
   `BuiltInAppEndpoints`/`BuiltIn`, duplicate Assistant folders reconciled into the one package.
