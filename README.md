# NeuroOS / DigitalBrain — Framework (brain/)

**NeuroOS** is the canonical .NET Aspire + Orleans runtime ("Kernel") for **DigitalBrain**. Everything is a **Neuron** (actor grain) or **Synapse** (immutable typed message). Server-driven UI, marketplace packs, typed C# only (`.ino` is dead).

The web client lives at [digitalbrain.tech](https://digitalbrain.tech) and talks to the kernels via gRPC + RFW/ForUI surfaces.

## What lives here

- `DigitalBrain.Core` — pure protocol: `INeuron`, `Synapse`, `IHandle<T>`, `NeuronId`/`TaskId`, and shared runtime messages.
- `DigitalBrain.Ui.Contracts` — server-driven UI contracts: `UiSurface`, `UiWidgetTree`, `RfwCard`, UI vocabularies, typed surface records, chart specs, generic action descriptors, and UI-facing neuron contracts.
- `DigitalBrain.Ui.Runtime` — runtime/sample UI projection helpers: `UiSurfaceSamples` and `UiSurfaceLiveData`, kept out of the stable schema assembly.
- `DigitalBrain.Pack.Contracts` — executable pack contracts: `IPackBehavior`, `NeuroPack`, `PackManifest`, bundle manifest/config schema, pack trust helpers, and `KitExperience` authoring helpers.
- `DigitalBrain.Marketplace.Contracts` — marketplace neuron commands/events plus projection helpers for marketplace and installed-bundle UI surfaces.
- `DigitalBrain.SeedPacks` — local marketplace seed catalog and embedded built-in pack source.
- `DigitalBrain.Kernel` — the runtime (Orleans + services): base `Neuron`, embodiment (`Foundry`/`PackAlcEmbodier`), LLM, economics (Stripe + ECDSA), context/memory (hybrid + Qdrant), server-driven UI (UiSurface emission + bidirectional `UiGateway`), self-update/HA rolling.
- `DigitalBrain.Aspire` — hosting SDK (`AddDigitalBrain`, `WireKernelSilo`, `AddFlutterClient`...).
- `NeuroOSPrototype.AppHost` — the Aspire model (3× kernel replicas, Ollama, Azurite, MCP, flutter-ui).
- `DigitalBrain.Mcp(.Tools)` — internal MCP server (neuron tools).
- Tests (Reqnroll BDD over real `TestCluster` + Aspire E2E).

See `Brain.slnx`, `aspire.config.json`, `Directory.Packages.props` (Aspire 13.4.6, Orleans 10.2, net11.0).

## Core Concepts

- **Neuron** = Orleans grain (`IGrainWithStringKey` implementing `INeuron` + `IHandle<>` for the synapses it consumes).
- **Synapse** = broadcast or point-to-point message (`[GenerateSerializer] record : Synapse`). Carries `SynapseId` + causation lineage via `Stamp(...)`.
- **NeuroPack** = signed (ECDSA-nistP256) C# code. Compiled in collectible ALC, embodied as running behavior. Marketplace install reaches N+1 handlers without restart.
- **UI** = `UiSurface : Synapse` (or `RfwCard`) from `DigitalBrain.Ui.Contracts`. Neurons emit `UiWidgetTree` using the official kit (`NeuronUiKit`, `UiKitVocabulary`). Client is thin (ForUI + RFW host/renderer). Shell, nav, content, experiences all come from neurons.

## Working in this repo (AGENTS.md loop)

1. Make requirements less dumb.
2. Delete (target >10% net reduction).
3. Simplify.
4. Accelerate (fast targeted feedback).
5. Automate last.

**Fast inner loop**: `dotnet build && dotnet test --filter "..."` (protocol/unit/step/UI contracts).

**Aspire/hosting**: use MCP tools (`aspire__doctor`, `aspire__list_resources`, `aspire__execute_resource_command` to restart flutter-ui/kernel, `aspire__list_console_logs`) or `aspire` CLI. Prefer targeted commands.

**Full distributed**: `aspire run` (Ollama + 3 kernels + client) only when needed for end-to-end (pack embodiment, LLM flows, live surfaces).

**After every change**:
- `dotnet build`
- Relevant `dotnet test`
- `aspire doctor` (MCP or CLI)
- Flutter: `flutter analyze` + targeted tests on renderer/kit
- MCP resource restart + logs when UI or hosting touched

**Rules**:
- Context7 for all library/framework APIs before writing against them (ForUI, RFW, Orleans, Aspire, gRPC...).
- Relative paths only. Never reference `C:\Users\...`.
- Meaningful names over comments. No vacuous `/// <summary>`.
- Central versions in `Directory.Packages.props`. Latest deliberate.
- Self-explanatory variable names.

## UI Kit (Neurons + Synapses focus)

See `docs/SYSTEM_DESIGN.md` for the current architecture and `CONTINUITY.md` for recent history.

- Grammar lives in `DigitalBrain.Ui.Contracts/UiSurfaces.cs` (`NeuronUiKit`, `UiKitVocabulary`, `UiWidgetTree`, `UiSurface.ForWidgetTree`...).
- Runtime/sample surface builders live in `DigitalBrain.Ui.Runtime/UiSurfaceRuntime.cs` (`UiSurfaceSamples`, `UiSurfaceLiveData`).
- Experiences: `KitExperience` + fluent `UiExperience` live in `DigitalBrain.Pack.Contracts` (packs author multi-hop UIs in pure C# while Core stays out of the UI schema and marketplace contracts stay outside Core).
- Emission examples: `UserSessionNeuron` (app-shell), `SystemNeurons`, UI runtime live-data helpers, and marketplace projections in `DigitalBrain.Marketplace.Contracts`.
- Wire: `HomeFeedBus` + `UiSurfaceRfwBridge` + bidirectional `UiGatewayService`.
- Client: `rfw_host/` (host + `UiSurfaceTreeRenderer`) + `ui_kit/` (thin ForUI impl of `ui:` vocab) + ForUI shell. Thin host only.

All shell/nav/content/forms/actions upgradable by publishing + installing packs.

## Quick start (dev)

From `brain/`:
```sh
aspire doctor
# targeted work
dotnet build DigitalBrain.Core/DigitalBrain.Core.csproj
dotnet build DigitalBrain.Ui.Contracts/DigitalBrain.Ui.Contracts.csproj
dotnet build DigitalBrain.Ui.Runtime/DigitalBrain.Ui.Runtime.csproj
dotnet test --filter "UiSurface|KitExperience"
```

From `app/`:
```sh
flutter pub get
flutter run -d chrome   # or windows
```

Full stack (kernels + Ollama + MCP + flutter):
```sh
aspire run
```

See `samples/`, `DigitalBrain.Tests/`, `NeuroOSPrototype.AppHost/AppHost.cs`.

## Deploy / Ops

See `deploy/`, Pulumi files, `docs/`.

## Related

- `app/` — Flutter client (static bundle on GitHub Pages).
- `core-requirements/` — planning (Musk approach, project survey).
- `Projects/` — archive (read for patterns, do not extend).
- `marketplace/` — seeds & client.

License, continuity, and session notes live alongside the code. Follow the 5 steps.

---

*NeuroOS: typed C# neurons, synapses on a timeline, UI from the kernel.*
