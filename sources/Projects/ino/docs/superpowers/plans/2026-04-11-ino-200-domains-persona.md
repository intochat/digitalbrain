# ino — 200 Domain Cards, Dynamic Persona, and Living System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the five core ino features — Chat with domain cards, Timeline, Branch, Brain View, and Skills — with dynamic Rive persona, Genesis model, and self-aware telemetry.

**Architecture:** Seven phases, each producing a demoable artifact. Phase 1 (rename) unblocks consistent terminology. Phase 2 (brain view) is a quick visual win. Phase 3 (persona) is the wow demo. Phase 4 (genesis) is the philosophical core. Phase 5 (telemetry) adds self-awareness. Phase 6 (Stitch) adds breadth. Phase 7 (website) is marketing.

**Tech Stack:** Flutter (rive, flutter_bloc, go_router), C# / Orleans (grains, persistent state), gRPC + protobuf, Rive Editor MCP, Stitch MCP, Aspire, OpenTelemetry

**Spec:** `docs/superpowers/specs/2026-04-11-ino-200-domains-persona-design.md`

---

## Phase 1: Terminology Rename (Flutter)

Rename user-facing labels: Time Travel → Timeline, Universe → Branch. Merge the two timeline BLoCs into one with Live/Scrub modes. No backend changes — gRPC stays as-is.

### Task 1.1: Rename UniverseBloc → BranchBloc

**Files:**
- Modify: `ino.flutter/lib/state/universe_bloc.dart` (all lines)
- Modify: `ino.flutter/lib/main.dart:76`
- Modify: `ino.flutter/lib/screens/universes/universes_screen.dart` (all lines)

- [ ] **Step 1: Rename the file**

```bash
cd ino.flutter
mv lib/state/universe_bloc.dart lib/state/branch_bloc.dart
mv lib/screens/universes/universes_screen.dart lib/screens/branch/branch_screen.dart
```

- [ ] **Step 2: Find-replace class names in `branch_bloc.dart`**

Replace all occurrences:
- `UniverseListLoaded` → `BranchListLoaded`
- `UniverseForked` → `BranchForked`
- `UniverseReplayed` → `BranchReplayed`
- `UniverseCompared` → `BranchCompared`
- `UniverseSelected` → `BranchSelected`
- `UniverseDiffCleared` → `BranchDiffCleared`
- `UniverseItem` → `BranchItem`
- `UniverseDiffResult` → `BranchDiffResult`
- `UniverseBlocState` → `BranchBlocState`
- `UniverseBloc` → `BranchBloc`
- `UniverseEvent` → `BranchEvent`

Keep gRPC client calls unchanged — they still call `forkUniverse`, `replayUniverse`, etc. on the wire.

- [ ] **Step 3: Update imports in `main.dart`**

At `main.dart:76`, change:
```dart
// old
BlocProvider(create: (_) => UniverseBloc(client)),
// new
BlocProvider(create: (_) => BranchBloc(client)),
```

Update import at top of file:
```dart
// old
import 'state/universe_bloc.dart';
// new
import 'state/branch_bloc.dart';
```

- [ ] **Step 4: Rename screen in `branch_screen.dart`**

Replace all occurrences:
- `UniversesScreen` → `BranchScreen`
- `_UniversesScreenState` → `_BranchScreenState`
- `_UniverseCard` → `_BranchCard`
- All UI strings: "Universe" → "Branch", "universe" → "branch", "Fork" → "Branch"
- "Fork Universe" dialog title → "Create Branch"

- [ ] **Step 5: Update router in `app.dart`**

At `app.dart:53-59`, change the universes tab:
```dart
// old
(path: '/universes', icon: Icons.call_split, label: 'Universes'),
// new
(path: '/branch', icon: Icons.call_split, label: 'Branch'),
```

At `app.dart:21-46`, update the route:
```dart
// old
GoRoute(path: '/universes', builder: (_, __) => const UniversesScreen()),
// new
GoRoute(path: '/branch', builder: (_, __) => const BranchScreen()),
```

Update import:
```dart
// old
import 'screens/universes/universes_screen.dart';
// new
import 'screens/branch/branch_screen.dart';
```

- [ ] **Step 6: Update `universe_diff_view.dart` references**

In `lib/ui/components/universe_diff_view.dart`, rename:
- `UniverseDiffView` → `BranchDiffView`
- UI strings: "Universe A" → "Reality", "Universe B" → "Branch"

- [ ] **Step 7: Verify build**

Run: `cd ino.flutter && flutter analyze`
Expected: No errors related to universe/branch naming

- [ ] **Step 8: Commit**

```bash
git add -A ino.flutter/lib/state/branch_bloc.dart ino.flutter/lib/screens/branch/ ino.flutter/lib/ui/components/ ino.flutter/lib/app.dart ino.flutter/lib/main.dart
git commit -m "refactor(flutter): rename Universe → Branch in all user-facing UI"
```

---

### Task 1.2: Merge TimeTravelBloc into TimelineBloc (Live + Scrub modes)

**Files:**
- Modify: `ino.flutter/lib/state/timeline_bloc.dart:7-213`
- Delete: `ino.flutter/lib/state/time_travel_bloc.dart`
- Modify: `ino.flutter/lib/main.dart:48,75`

- [ ] **Step 1: Add Scrub events and state to `timeline_bloc.dart`**

At `timeline_bloc.dart:7-24`, add new events after existing ones:

```dart
sealed class TimelineEvent {}

// existing Live mode events
class TimelineStarted extends TimelineEvent {}
class TimelinePaused extends TimelineEvent {}
class TimelineResumed extends TimelineEvent {}
class TimelineFilterChanged extends TimelineEvent {
  final int minDecay;
  final Set<String>? kinds;
  TimelineFilterChanged({required this.minDecay, this.kinds});
}
class _EventReceived extends TimelineEvent {
  final TimelineEntry entry;
  _EventReceived(this.entry);
}

// NEW: Scrub mode events
class TimelineScrubbed extends TimelineEvent {
  final int sequence;
  TimelineScrubbed(this.sequence);
}
class TimelineModeToggled extends TimelineEvent {}
```

At `timeline_bloc.dart:50-80`, extend the state:

```dart
enum TimelineMode { live, scrub }

class TimelineBlocState {
  final List<TimelineEntry> events;
  final bool isLive;
  final bool isLoading;
  final int minDecay;
  final Set<String>? activeKinds;
  // NEW scrub fields
  final TimelineMode mode;
  final int currentSequence;
  final int maxSequence;
  final StateSnapshot? snapshot;

  const TimelineBlocState({
    this.events = const [],
    this.isLive = false,
    this.isLoading = false,
    this.minDecay = 30,
    this.activeKinds,
    this.mode = TimelineMode.live,
    this.currentSequence = 0,
    this.maxSequence = 0,
    this.snapshot,
  });

  // copyWith includes all fields
}
```

Add `StateSnapshot` class (from `time_travel_bloc.dart:13-27`):

```dart
class StateSnapshot {
  final int asOfSequence;
  final String asOfTimestamp;
  final List<String> activeNeurons;
  final List<String> openCorrelations;
  final Map<String, int> countsByKind;

  const StateSnapshot({
    required this.asOfSequence,
    required this.asOfTimestamp,
    required this.activeNeurons,
    required this.openCorrelations,
    required this.countsByKind,
  });
}
```

- [ ] **Step 2: Add scrub handlers to TimelineBloc**

In the BLoC class, register new handlers and add scrub logic:

```dart
// in constructor, add:
on<TimelineScrubbed>(_onScrubbed);
on<TimelineModeToggled>(_onModeToggled);

// snapshot cache (same pattern as old TimeTravelBloc)
final Map<int, StateSnapshot> _snapshotCache = {};

Future<void> _onModeToggled(TimelineModeToggled event, Emitter<TimelineBlocState> emit) async {
  final newMode = state.mode == TimelineMode.live ? TimelineMode.scrub : TimelineMode.live;
  emit(state.copyWith(mode: newMode));
}

Future<void> _onScrubbed(TimelineScrubbed event, Emitter<TimelineBlocState> emit) async {
  if (_snapshotCache.containsKey(event.sequence)) {
    emit(state.copyWith(
      currentSequence: event.sequence,
      snapshot: _snapshotCache[event.sequence],
    ));
    return;
  }
  emit(state.copyWith(currentSequence: event.sequence, isLoading: true));
  final resp = await _client.getStateAt(event.sequence);
  final snap = StateSnapshot(
    asOfSequence: resp.asOfSequence,
    asOfTimestamp: resp.asOfTimestamp,
    activeNeurons: resp.activeNeurons.toList(),
    openCorrelations: resp.openCorrelations.toList(),
    countsByKind: Map<String, int>.from(resp.countsByKind),
  );
  _snapshotCache[event.sequence] = snap;
  emit(state.copyWith(
    currentSequence: event.sequence,
    snapshot: snap,
    isLoading: false,
  ));
}
```

- [ ] **Step 3: Delete `time_travel_bloc.dart`**

```bash
rm ino.flutter/lib/state/time_travel_bloc.dart
```

- [ ] **Step 4: Remove TimeTravelBloc from `main.dart`**

At `main.dart:75`, remove:
```dart
// DELETE this line
BlocProvider(create: (_) => TimeTravelBloc(client)),
```

Remove the import:
```dart
// DELETE
import 'state/time_travel_bloc.dart';
```

- [ ] **Step 5: Merge screens into unified `timeline_screen.dart`**

Rewrite `lib/screens/timeline/timeline_screen.dart` to have a mode toggle (Live / Scrub) in the AppBar. When in Live mode, show the existing event list. When in Scrub mode, show the scrubber + snapshot panels (from old `time_travel_screen.dart`).

The mode toggle is a `SegmentedButton` or `ToggleButtons` in the AppBar:

```dart
actions: [
  BlocBuilder<TimelineBloc, TimelineBlocState>(
    builder: (context, state) => ToggleButtons(
      isSelected: [state.mode == TimelineMode.live, state.mode == TimelineMode.scrub],
      onPressed: (_) => context.read<TimelineBloc>().add(TimelineModeToggled()),
      children: const [Text('Live'), Text('Scrub')],
    ),
  ),
],
```

Body switches on `state.mode`:
- `TimelineMode.live` → existing ListView of event cards + control bar
- `TimelineMode.scrub` → TimelineScrubber + snapshot chips + NeuralMap (from old time_travel_screen)

- [ ] **Step 6: Delete old time travel screen**

```bash
rm ino.flutter/lib/screens/time_travel/time_travel_screen.dart
rmdir ino.flutter/lib/screens/time_travel
```

- [ ] **Step 7: Update router — remove /timetravel route**

In `app.dart:21-46`, remove the `/timetravel` route. In `app.dart:53-59`, remove the Time Travel tab. The bottom nav now has 4 tabs: Chat, Timeline, Branch, Brain View, Skills — wait, that's still 5. Replace `/timetravel` with Brain View:

```dart
// Updated tabs (5 total):
(path: '/home', icon: Icons.chat, label: 'Chat'),
(path: '/timeline', icon: Icons.timeline, label: 'Timeline'),
(path: '/branch', icon: Icons.call_split, label: 'Branch'),
(path: '/brain', icon: Icons.hub, label: 'Brain'),
(path: '/skills', icon: Icons.extension, label: 'Skills'),
```

The `/brain` route goes to a new `BrainViewScreen` (created in Phase 2). For now, point it at the existing Skills screen as placeholder.

- [ ] **Step 8: Verify build + commit**

Run: `cd ino.flutter && flutter analyze`

```bash
git add -A ino.flutter/
git commit -m "refactor(flutter): merge TimeTravelBloc into TimelineBloc with Live/Scrub modes, rename Time Travel → Timeline"
```

---

## Phase 2: Brain View with Zoom and Reset

### Task 2.1: Create BrainViewScreen with InteractiveViewer

**Files:**
- Create: `ino.flutter/lib/screens/brain/brain_view_screen.dart`
- Modify: `ino.flutter/lib/ui/components/neural_map.dart:6-118`
- Modify: `ino.flutter/lib/app.dart` (route for `/brain`)

- [ ] **Step 1: Create BrainViewScreen**

```dart
// ino.flutter/lib/screens/brain/brain_view_screen.dart
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import '../../state/skills_bloc.dart';
import '../../ui/components/neural_map.dart';

class BrainViewScreen extends StatefulWidget {
  const BrainViewScreen({super.key});
  @override
  State<BrainViewScreen> createState() => _BrainViewScreenState();
}

class _BrainViewScreenState extends State<BrainViewScreen> {
  final _transformController = TransformationController();
  String _filter = 'all'; // all, active, domain

  void _resetZoom() {
    _transformController.value = Matrix4.identity();
  }

  @override
  void dispose() {
    _transformController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Brain View'),
        actions: [
          SegmentedButton<String>(
            segments: const [
              ButtonSegment(value: 'all', label: Text('All')),
              ButtonSegment(value: 'active', label: Text('Active')),
            ],
            selected: {_filter},
            onSelectionChanged: (v) => setState(() => _filter = v.first),
          ),
        ],
      ),
      body: Stack(
        children: [
          InteractiveViewer(
            transformationController: _transformController,
            minScale: 0.3,
            maxScale: 5.0,
            boundaryMargin: const EdgeInsets.all(200),
            child: SizedBox(
              width: 1200,
              height: 1200,
              child: BlocBuilder<SkillsBloc, SkillsBlocState>(
                builder: (context, state) => NeuralMap(
                  neurons: state.skills.map((s) => s.name).toList(),
                  filter: _filter,
                ),
              ),
            ),
          ),
          Positioned(
            right: 16,
            bottom: 16,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                FloatingActionButton.small(
                  heroTag: 'zoom_in',
                  onPressed: () {
                    final current = _transformController.value.clone();
                    current.scale(1.3);
                    _transformController.value = current;
                  },
                  child: const Icon(Icons.add),
                ),
                const SizedBox(height: 8),
                FloatingActionButton.small(
                  heroTag: 'zoom_out',
                  onPressed: () {
                    final current = _transformController.value.clone();
                    current.scale(0.7);
                    _transformController.value = current;
                  },
                  child: const Icon(Icons.remove),
                ),
                const SizedBox(height: 8),
                FloatingActionButton.small(
                  heroTag: 'reset',
                  onPressed: _resetZoom,
                  child: const Icon(Icons.fit_screen),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
```

- [ ] **Step 2: Update NeuralMap to accept filter param and use larger canvas**

At `neural_map.dart:6`, add `filter` parameter:

```dart
class NeuralMap extends StatelessWidget {
  final List<String> neurons;
  final String filter;
  const NeuralMap({super.key, required this.neurons, this.filter = 'all'});
```

The painter at `neural_map.dart:36-118` already draws nodes in a circle. No change needed to the painter itself — the `InteractiveViewer` wrapping in `BrainViewScreen` handles zoom.

- [ ] **Step 3: Wire route in `app.dart`**

Add import and route:
```dart
import 'screens/brain/brain_view_screen.dart';

// in routes:
GoRoute(path: '/brain', builder: (_, __) => const BrainViewScreen()),
```

- [ ] **Step 4: Verify build + commit**

Run: `cd ino.flutter && flutter analyze`

```bash
git add ino.flutter/lib/screens/brain/ ino.flutter/lib/ui/components/neural_map.dart ino.flutter/lib/app.dart
git commit -m "feat(flutter): Brain View screen with zoom, pan, and reset button"
```

---

## Phase 3: Dynamic Persona with Rive

### Task 3.1: Extend PersonaState with persona identity

**Files:**
- Modify: `ino.flutter/lib/persona/persona_state.dart:16-50`

- [ ] **Step 1: Add persona identity fields to PersonaStateModel**

```dart
class PersonaStateModel {
  final PersonaEmotion emotion;
  final double energy;
  final double confidence;
  final int neuronCount;
  final double synapseRate;
  final Map<String, double> domainAffinity;
  // NEW persona identity
  final String personaName;       // "jarvis", "luna", "cortex", etc.
  final String personaSlug;       // lowercase key for cache: "jarvis"
  final Map<String, String> traits; // {"tone": "formal", "style": "analytical", ...}
  final String? riveAssetUrl;     // URL to .riv file in blob storage (null = use template)

  const PersonaStateModel({
    this.emotion = PersonaEmotion.idle,
    this.energy = 0.5,
    this.confidence = 0.5,
    this.neuronCount = 0,
    this.synapseRate = 0.0,
    this.domainAffinity = const {},
    this.personaName = 'ino',
    this.personaSlug = 'ino',
    this.traits = const {},
    this.riveAssetUrl,
  });
  // extend copyWith with new fields
}
```

- [ ] **Step 2: Commit**

```bash
git add ino.flutter/lib/persona/persona_state.dart
git commit -m "feat(persona): extend PersonaStateModel with identity fields and riveAssetUrl"
```

---

### Task 3.2: Add Rive animation to PersonaWidget (replacing CustomPaint)

**Files:**
- Modify: `ino.flutter/lib/persona/persona_widget.dart:8-218`
- Modify: `ino.flutter/pubspec.yaml` (add rive dependency)

- [ ] **Step 1: Add rive dependency**

```bash
cd ino.flutter && flutter pub add rive
```

- [ ] **Step 2: Create preset .riv template placeholder**

For v1, keep the existing `CustomPaint` as the default renderer. Add a Rive renderer that activates when `riveAssetUrl` is set. The widget switches between them:

```dart
// persona_widget.dart — updated build method
@override
Widget build(BuildContext context) {
  return BlocBuilder<PersonaBloc, PersonaStateModel>(
    builder: (context, persona) {
      // if we have a Rive asset, use it
      if (persona.riveAssetUrl != null) {
        return SizedBox(
          width: widget.size,
          height: widget.size,
          child: _RivePersona(
            assetUrl: persona.riveAssetUrl!,
            emotion: persona.emotion,
            energy: persona.energy,
            size: widget.size,
          ),
        );
      }
      // fallback: existing CustomPaint persona
      return AnimatedBuilder(
        animation: _controller,
        builder: (_, __) => CustomPaint(
          size: Size(widget.size, widget.size),
          painter: _PersonaPainter(/* existing params */),
        ),
      );
    },
  );
}
```

- [ ] **Step 3: Create `_RivePersona` widget**

```dart
class _RivePersona extends StatefulWidget {
  final String assetUrl;
  final PersonaEmotion emotion;
  final double energy;
  final double size;
  const _RivePersona({required this.assetUrl, required this.emotion, required this.energy, required this.size});
  @override
  State<_RivePersona> createState() => _RivePersonaState();
}

class _RivePersonaState extends State<_RivePersona> {
  Artboard? _artboard;
  StateMachineController? _smController;
  SMINumber? _emotionInput;
  SMINumber? _energyInput;

  @override
  void initState() {
    super.initState();
    _loadRive();
  }

  Future<void> _loadRive() async {
    final data = await HttpClient().getUrl(Uri.parse(widget.assetUrl))
        .then((req) => req.close())
        .then((resp) => resp.fold<BytesBuilder>(BytesBuilder(), (b, d) => b..add(d)))
        .then((b) => b.takeBytes());
    final file = RiveFile.import(ByteData.sublistView(Uint8List.fromList(data)));
    final artboard = file.mainArtboard.instance();
    final controller = StateMachineController.fromArtboard(artboard);
    if (controller != null) {
      artboard.addController(controller);
      _emotionInput = controller.findInput<double>('emotion') as SMINumber?;
      _energyInput = controller.findInput<double>('energy') as SMINumber?;
    }
    setState(() {
      _artboard = artboard;
      _smController = controller;
    });
    _syncInputs();
  }

  void _syncInputs() {
    _emotionInput?.value = widget.emotion.index.toDouble();
    _energyInput?.value = widget.energy;
  }

  @override
  void didUpdateWidget(covariant _RivePersona old) {
    super.didUpdateWidget(old);
    if (old.emotion != widget.emotion || old.energy != widget.energy) {
      _syncInputs();
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_artboard == null) {
      return SizedBox(width: widget.size, height: widget.size);
    }
    return SizedBox(
      width: widget.size,
      height: widget.size,
      child: Rive(artboard: _artboard!, fit: BoxFit.contain),
    );
  }
}
```

- [ ] **Step 4: Verify build + commit**

Run: `cd ino.flutter && flutter analyze`

```bash
git add ino.flutter/lib/persona/ ino.flutter/pubspec.yaml ino.flutter/pubspec.lock
git commit -m "feat(persona): Rive animation support with fallback to CustomPaint"
```

---

### Task 3.3: Add persona switch gRPC endpoint + backend

**Files:**
- Modify: `iaw/Telegram/Protos/ino.proto`
- Modify: `iaw/Telegram/Services/InoService.cs`

- [ ] **Step 1: Add proto messages**

In `ino.proto`, add after the PersonaState message:

```protobuf
message SwitchPersonaRequest {
  string persona_name = 1;     // "Jarvis", "my dog Rex"
}

message SwitchPersonaResponse {
  string persona_slug = 1;     // "jarvis", "my-dog-rex"
  string persona_name = 2;     // display name
  string rive_asset_url = 3;   // URL to .riv file (empty if generating)
  bool is_generating = 4;      // true if PersonaCreator is building the animation
  map<string, string> traits = 5;
}
```

Add RPC to the service:
```protobuf
rpc SwitchPersona(SwitchPersonaRequest) returns (SwitchPersonaResponse);
```

- [ ] **Step 2: Implement SwitchPersona in InoService**

In `InoService.cs`, add handler that:
1. Slugifies the persona name
2. Checks if a cached persona exists in the agent registry
3. If yes: returns the cached .riv URL and traits
4. If no: returns `is_generating = true` and fires a signal to PersonaCreator (deferred to Phase 4/Genesis)

For now, implement with 5 hardcoded presets (jarvis, luna, cortex, coach, sage) and return `is_generating = true` for anything else:

```csharp
public override Task<SwitchPersonaResponse> SwitchPersona(SwitchPersonaRequest request, ServerCallContext context)
{
    var slug = request.PersonaName.ToLowerInvariant().Replace(" ", "-");
    var presets = new Dictionary<string, (string name, Dictionary<string, string> traits)>
    {
        ["jarvis"] = ("Jarvis", new() { ["tone"] = "formal", ["style"] = "analytical", ["proactivity"] = "high" }),
        ["luna"] = ("Luna", new() { ["tone"] = "warm", ["style"] = "creative", ["proactivity"] = "medium" }),
        ["cortex"] = ("Cortex", new() { ["tone"] = "terse", ["style"] = "technical", ["proactivity"] = "medium" }),
        ["coach"] = ("Coach", new() { ["tone"] = "intense", ["style"] = "motivating", ["proactivity"] = "high" }),
        ["sage"] = ("Sage", new() { ["tone"] = "calm", ["style"] = "reflective", ["proactivity"] = "low" }),
    };

    if (presets.TryGetValue(slug, out var preset))
    {
        var resp = new SwitchPersonaResponse { PersonaSlug = slug, PersonaName = preset.name };
        foreach (var kv in preset.traits) resp.Traits.Add(kv.Key, kv.Value);
        return Task.FromResult(resp);
    }

    return Task.FromResult(new SwitchPersonaResponse
    {
        PersonaSlug = slug,
        PersonaName = request.PersonaName,
        IsGenerating = true,
    });
}
```

- [ ] **Step 3: Regenerate proto + verify build**

```bash
cd ino.flutter && flutter pub run protoc_plugin  # or however protos are generated
dotnet build ino.slnx
```

- [ ] **Step 4: Commit**

```bash
git add iaw/Telegram/Protos/ino.proto iaw/Telegram/Services/InoService.cs ino.flutter/lib/grpc/generated/
git commit -m "feat(persona): SwitchPersona gRPC endpoint with 5 preset personas"
```

---

### Task 3.4: Wire persona switch into Flutter chat

**Files:**
- Modify: `ino.flutter/lib/state/persona_bloc.dart`
- Modify: `ino.flutter/lib/state/ino_bloc.dart`
- Modify: `ino.flutter/lib/grpc/ino_client.dart`

- [ ] **Step 1: Add switchPersona to gRPC client**

In `ino_client.dart`, add:

```dart
Future<SwitchPersonaResponse> switchPersona(String personaName) async {
  return _stub.switchPersona(SwitchPersonaRequest(personaName: personaName));
}
```

- [ ] **Step 2: Add PersonaSwitchRequested event to PersonaBloc**

```dart
class PersonaSwitchRequested extends PersonaEvent {
  final String personaName;
  PersonaSwitchRequested(this.personaName);
}
```

Handler:
```dart
Future<void> _onSwitchRequested(PersonaSwitchRequested event, Emitter<PersonaStateModel> emit) async {
  final resp = await _client.switchPersona(event.personaName);
  emit(state.copyWith(
    personaName: resp.personaName,
    personaSlug: resp.personaSlug,
    riveAssetUrl: resp.riveAssetUrl.isEmpty ? null : resp.riveAssetUrl,
    traits: Map<String, String>.from(resp.traits),
  ));
}
```

- [ ] **Step 3: Detect "you are X" in chat messages**

In `InoBloc._onSendMessage`, after sending the message, check if it matches the pattern "you are ..." and fire the persona switch:

```dart
final personaMatch = RegExp(r'^you are (.+)$', caseSensitive: false).firstMatch(message);
if (personaMatch != null) {
  // fire persona switch in parallel with the chat response
  _personaBloc.add(PersonaSwitchRequested(personaMatch.group(1)!));
}
```

- [ ] **Step 4: Verify build + commit**

```bash
cd ino.flutter && flutter analyze
git add ino.flutter/lib/state/ ino.flutter/lib/grpc/
git commit -m "feat(persona): 'you are X' in chat triggers persona switch with Rive cross-fade"
```

---

## Phase 4: Genesis Model (Creator Neuron)

### Task 4.1: Create Creator agent grain

**Files:**
- Create: `iaw/Agents/Genesis/CreatorAgent.cs`
- Modify: `iaw/Core/Registry/AgentRegistryGrain.cs:23-27`

- [ ] **Step 1: Create CreatorAgent**

```csharp
// iaw/Agents/Genesis/CreatorAgent.cs
namespace IAW.Agents.Genesis;

[LLMAgent("Creator",
    "The genesis neuron. Creates new skills on demand. Never talks to users directly.",
    Model = "gpt-54-regular")]
public class CreatorAgent : Agent<ICreator>
{
    protected override IEnumerable<Tool> DefineTools() =>
    [
        Tool.Create("create_skill",
            "Create a new runtime skill with a name, system prompt, and tool list",
            async (string name, string systemPrompt, string[] tools) =>
            {
                var registry = IAW.Get<IAgentRegistry>();
                var record = new AgentRecord
                {
                    Id = $"skill-{name.ToLowerInvariant().Replace(" ", "-")}",
                    Name = name,
                    Description = systemPrompt[..Math.Min(200, systemPrompt.Length)],
                    SystemPrompt = systemPrompt,
                    Tools = tools.ToList(),
                    IsRuntime = true,
                };
                await registry.RegisterAsync(record);
                return $"Skill '{name}' created with id '{record.Id}'";
            }),
    ];
}

public interface ICreator : IAgent { }
```

- [ ] **Step 2: Add `IsRuntime` flag to AgentRecord**

In `AgentRecord` (wherever it's defined in Core/Contracts), add:
```csharp
public bool IsRuntime { get; set; }
```

This distinguishes compile-time agents from L1 runtime-created ones.

- [ ] **Step 3: Verify build + commit**

```bash
dotnet build ino.slnx
git add iaw/Agents/Genesis/ iaw/Core/
git commit -m "feat(genesis): Creator agent (neuron #0) with create_skill tool"
```

---

### Task 4.2: Log system birth events to timeline

**Files:**
- Modify: `features/timetravel/Timetravel.Core/TimelineEvent.cs`
- Modify: `iaw/Agents.Host/Program.cs` or silo startup

- [ ] **Step 1: Add `SkillCreated` event kind to TimelineEventKind**

In `TimelineEvent.cs`, add to the enum:
```csharp
SkillCreated,     // "+" — new skill registered
PersonaSwitched,  // "P" — persona changed
```

- [ ] **Step 2: Fire SkillCreated from AgentRegistryGrain.RegisterAsync**

At `AgentRegistryGrain.cs:23-27`, after `store.WriteStateAsync()`, fire a timeline event:

```csharp
var timeline = GrainFactory.GetGrain<ITimelineWriter>("global");
await timeline.AppendAsync(new TimelineEvent
{
    Kind = TimelineEventKind.SkillCreated,
    Source = "creator",
    Target = record.Id,
    Verb = "created",
});
```

- [ ] **Step 3: Verify build + commit**

```bash
dotnet build ino.slnx
git add features/timetravel/ iaw/Core/Registry/
git commit -m "feat(genesis): log SkillCreated events to timeline for system growth tracing"
```

---

## Phase 5: Self-Aware Telemetry

### Task 5.1: Add telemetry metrics to backend

**Files:**
- Modify: `iaw/Core/Registry/AgentRegistryGrain.cs`
- Create: `iaw/Core/Telemetry/InoMetrics.cs`

- [ ] **Step 1: Create metrics class**

```csharp
// iaw/Core/Telemetry/InoMetrics.cs
namespace IAW.Core.Telemetry;

using System.Diagnostics.Metrics;

public static class InoMetrics
{
    private static readonly Meter Meter = new("ino", "1.0.0");

    public static readonly Counter<long> SkillInvocations = Meter.CreateCounter<long>("ino.skills.invocations");
    public static readonly Counter<long> SignalsTotal = Meter.CreateCounter<long>("ino.signals.total");
    public static readonly Counter<long> PersonaSwitches = Meter.CreateCounter<long>("ino.persona.switches");
    public static readonly UpDownCounter<long> SkillsActive = Meter.CreateUpDownCounter<long>("ino.skills.active");
}
```

- [ ] **Step 2: Instrument AgentRegistryGrain**

In `RegisterAsync`, after write:
```csharp
InoMetrics.SkillsActive.Add(1, new KeyValuePair<string, object?>("skill", record.Name));
```

- [ ] **Step 3: Verify build + commit**

```bash
dotnet build ino.slnx
git add iaw/Core/Telemetry/ iaw/Core/Registry/
git commit -m "feat(telemetry): ino.skills.invocations, ino.signals.total, ino.persona.switches metrics"
```

---

### Task 5.2: Add telemetry query to gRPC + Flutter chart card

**Files:**
- Modify: `iaw/Telegram/Protos/ino.proto`
- Modify: `iaw/Telegram/Services/InoService.cs`
- Create: `ino.flutter/lib/ui/components/bar_chart_card.dart`

- [ ] **Step 1: Add proto messages for telemetry**

```protobuf
message TelemetryRequest {
  string query = 1;  // "most_used_skills", "response_time", "skill_count"
}

message TelemetryResponse {
  string chart_type = 1;  // "bar", "sparkline", "counter"
  repeated TelemetryEntry entries = 2;
  string summary = 3;
}

message TelemetryEntry {
  string label = 1;
  double value = 2;
}

rpc GetTelemetry(TelemetryRequest) returns (TelemetryResponse);
```

- [ ] **Step 2: Implement GetTelemetry (hardcoded for now)**

In `InoService.cs`:

```csharp
public override async Task<TelemetryResponse> GetTelemetry(TelemetryRequest request, ServerCallContext context)
{
    var registry = _clusterClient.GetGrain<IAgentRegistry>("registry");
    var agents = await registry.GetAllAsync();

    return request.Query switch
    {
        "most_used_skills" => new TelemetryResponse
        {
            ChartType = "bar",
            Summary = $"{agents.Count} skills registered",
            Entries = { agents.OrderByDescending(a => a.InvocationCount)
                .Take(10)
                .Select(a => new TelemetryEntry { Label = a.Name, Value = a.InvocationCount }) },
        },
        "skill_count" => new TelemetryResponse
        {
            ChartType = "counter",
            Entries = { new TelemetryEntry { Label = "Skills Active", Value = agents.Count } },
        },
        _ => new TelemetryResponse { ChartType = "text", Summary = "Unknown query" },
    };
}
```

- [ ] **Step 3: Create bar chart Flutter card**

```dart
// ino.flutter/lib/ui/components/bar_chart_card.dart
import 'package:flutter/material.dart';

class BarChartCard extends StatelessWidget {
  final String title;
  final List<(String label, double value)> entries;
  final double maxValue;

  const BarChartCard({super.key, required this.title, required this.entries, required this.maxValue});

  @override
  Widget build(BuildContext context) {
    return Card(
      color: const Color(0xFF161b22),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w700, color: Color(0xFFe6e6e6))),
            const SizedBox(height: 12),
            ...entries.map((e) => Padding(
              padding: const EdgeInsets.only(bottom: 6),
              child: Row(
                children: [
                  SizedBox(width: 70, child: Text(e.$1, style: const TextStyle(fontSize: 10, color: Color(0xFF8b949e)), textAlign: TextAlign.right)),
                  const SizedBox(width: 8),
                  Expanded(
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(4),
                      child: LinearProgressIndicator(
                        value: e.$2 / maxValue,
                        minHeight: 16,
                        backgroundColor: const Color(0xFF0d1117),
                        valueColor: const AlwaysStoppedAnimation(Color(0xFF6C63FF)),
                      ),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Text('${e.$2.toInt()}', style: const TextStyle(fontSize: 10, color: Color(0xFF8b949e))),
                ],
              ),
            )),
          ],
        ),
      ),
    );
  }
}
```

- [ ] **Step 4: Verify build + commit**

```bash
dotnet build ino.slnx
cd ino.flutter && flutter analyze
git add iaw/Telegram/ ino.flutter/lib/ui/components/bar_chart_card.dart
git commit -m "feat(telemetry): GetTelemetry gRPC endpoint + BarChartCard Flutter component"
```

---

## Phase 6: Stitch Card Generation

### Task 6.1: Create shared design system

**Files:**
- Create: `.stitch/DESIGN.md`

- [ ] **Step 1: Write the design system file**

Create `.stitch/DESIGN.md` with the color palette, typography, card structure, and persona variants from spec Section 13.2. This file is copied into every Stitch generation prompt for consistency.

- [ ] **Step 2: Commit**

```bash
git add .stitch/DESIGN.md
git commit -m "feat(stitch): ino card design system for 200 domain card generation"
```

---

### Task 6.2: Generate first domain (Productivity, 10 cards)

This task uses the `stitch-design` skill interactively. Not code — it's a Stitch MCP batch operation.

- [ ] **Step 1: Create Stitch project**

Use Stitch MCP: `create_project(title="ino-productivity")`

- [ ] **Step 2: Generate 10 cards**

For each app (Drive, Docs, Sheets, Office 365, Notion, Todoist, Trello, Asana, Evernote, ClickUp), call:
```
generate_screen_from_text(projectId, enhanced_prompt_with_DESIGN_MD, MOBILE)
```

Prompts follow the pattern:
```
Design a dark-themed mobile card for [App] showing [use case].
Card structure: header with icon + "[App] · [Action]" + green READY tag.
Body: [domain-specific content].
Follow the ino card design system: #6C63FF primary, #161b22 surface, 14px border-radius, 11px body text.
```

- [ ] **Step 3: Review and iterate**

Use `edit_screens` for any cards that drift from the design system.

- [ ] **Step 4: Export designs**

Download HTML + screenshots to `.stitch/designs/productivity/`

- [ ] **Step 5: Commit**

```bash
git add .stitch/designs/productivity/
git commit -m "feat(stitch): 10 Productivity domain card designs (Drive, Docs, Sheets, etc.)"
```

Repeat Task 6.2 pattern for remaining 24 domains, prioritized by the generation order in spec Section 13.3.

---

## Phase 7: Website Brain View

### Task 7.1: Add interactive brain view to VitePress

**Files:**
- Create: `website/.vitepress/theme/components/BrainView.vue`
- Modify: `website/guide/how-it-works.md`

- [ ] **Step 1: Create BrainView.vue**

SVG-based interactive component following the same patterns as existing `HowItWorksDiagram.vue`. Shows 25 domain nodes clustered by readiness, with zoom via CSS transform + mouse wheel, and a reset button.

Key structure:
```vue
<template>
  <div class="brain-container" @wheel.prevent="onWheel">
    <div class="brain-toolbar">
      <button @click="resetZoom">Reset</button>
    </div>
    <svg :style="{ transform: `scale(${zoom}) translate(${panX}px, ${panY}px)` }"
         viewBox="0 0 800 600">
      <!-- Central ino node -->
      <!-- 25 domain cluster nodes positioned radially -->
      <!-- Signal edges with pulse animation -->
    </svg>
  </div>
</template>
```

Domain data comes from a static JSON array matching the 25 domains in spec Section 4.2.

- [ ] **Step 2: Embed in how-it-works page**

In `website/guide/how-it-works.md`, add:
```md
## Brain View — 203 Skills Across 25 Domains

<BrainView />
```

- [ ] **Step 3: Test locally**

```bash
cd website && npm run dev
```

Open in browser, verify zoom/pan/reset work, nodes are clickable.

- [ ] **Step 4: Commit**

```bash
git add website/.vitepress/theme/components/BrainView.vue website/guide/how-it-works.md
git commit -m "feat(website): interactive Brain View with 25 domain clusters, zoom, and reset"
```

---

### Task 7.2: Add genesis growth animation to hero

**Files:**
- Modify: `website/.vitepress/theme/components/HomePage.vue`

- [ ] **Step 1: Add growth animation**

In the hero section of `HomePage.vue`, replace or extend the existing neural mesh animation with a growth sequence:
- Frame 0: 2 dots (Creator gold, ino purple)
- Frame 1-3: Edge forms between them
- Frame 4-10: New skill dots appear one by one, edges form
- Frame 11-20: Acceleration — multiple dots per frame
- Frame 30+: Dense neural network (existing mesh appearance)
- Loop every 10 seconds

Use existing CSS animation patterns from `HomePage.vue` (it already has animated neural mesh with signal pulses).

- [ ] **Step 2: Test + commit**

```bash
cd website && npm run dev
git add website/.vitepress/theme/components/HomePage.vue
git commit -m "feat(website): genesis growth animation in hero — system grows from 2 neurons to dense network"
```

---

## Execution Dependencies

```
Phase 1 (Rename) ──────┐
                        ├──→ Phase 3 (Persona) ──→ Phase 4 (Genesis)
Phase 2 (Brain View) ──┘                                │
                                                         ▼
                                              Phase 5 (Telemetry)

Phase 6 (Stitch) ── independent, can run in parallel with any phase
Phase 7 (Website) ── independent, can run anytime after Phase 2
```

Phase 1 and 2 are quick wins (~1 hour total). Phase 3 is the demo moment. Phase 4 completes the philosophical model. Phase 5 adds self-awareness. Phase 6 and 7 are breadth and polish.

---

## Deferred to Follow-Up Plans

Two spec sections are intentionally deferred:

1. **Section 7 — Screen Layout refactor** (persona zone top, contextual UI bottom). The current layout already has persona at top of HomeScreen. The full refactor (persona zone that shrinks/expands, contextual UI zone that switches based on signal state) depends on all other phases being in place. Separate plan once Phases 1-5 land.

2. **Section 12 — Auth Architecture** (GoogleAuth skill, OAuth cascade). Infrastructure-heavy — needs Google Cloud project setup, OAuth client IDs, secure token storage grain, consent flow webview. Separate plan with security review before implementation.
