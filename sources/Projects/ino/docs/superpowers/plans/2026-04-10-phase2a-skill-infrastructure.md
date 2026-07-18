# Phase 2a: Skill Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make AgentRegistry persistent, extend AgentRecord with domain/skill metadata, implement the skill install gRPC flow end-to-end, and add a Flutter skill browser screen so users can discover and install domain modules.

**Architecture:** AgentRegistryGrain gets `IPersistentState<RegistryState>` so runtime-registered neurons survive silo restarts. AgentRecord gains `Domain`, `Origin`, `UISchema`, and `ScriptSource` fields. The gRPC service's `ListSkills`/`InstallSkill`/`GetSkillUI` stubs are implemented against the registry. A new `SkillsBloc` in Flutter drives a skill browser screen accessible from the home screen. Three seed travel skills (FlightSearch, HotelSearch, PlaceDiscovery) are pre-registered as metadata-only records to prove the flow — actual neuron implementations come in Phase 2b.

**Tech Stack:** Orleans 10 (`IPersistentState`, `Grain<T>`), .NET 11, gRPC, Flutter (flutter_bloc, go_router, rfw)

**Spec:** `docs/superpowers/specs/2026-04-10-flutter-grpc-persona-design.md`, `docs/product/travel-domain.md`

---

## File Map

### C# — Modified files

| File | Change |
|---|---|
| `iaw/Core/Registry/AgentRecord.cs` | Add `Domain`, `Origin`, `UISchema`, `ScriptSource` fields |
| `iaw/Core/Registry/AgentRegistryGrain.cs` | Switch from in-memory dict to `IPersistentState<RegistryState>`, add `InstallSkillAsync`, `ListByDomainAsync` |
| `iaw/Core/Registry/IAgentRegistry.cs` | Add `InstallSkillAsync`, `ListByDomainAsync`, `UninstallSkillAsync` methods |
| `iaw/Core/Registry/AgentRegistrationStartupTask.cs` | Set `Origin = CompileTime` on discovered records |
| `iaw/Grpc/Services/InoService.cs` | Implement `ListSkills`, `InstallSkill`, `GetSkillUI` against registry |

### C# — New files

| File | Responsibility |
|---|---|
| `iaw/Core/Registry/SkillOrigin.cs` | Enum: `CompileTime`, `Runtime`, `Imported` |
| `iaw/Core/Registry/RegistryState.cs` | Persistent state class for `AgentRegistryGrain` |
| `features/ino-new/InoNew.Core/Skills/TravelSkillSeeder.cs` | Seeds 3 travel skill metadata records on startup |

### Flutter — New files

| File | Responsibility |
|---|---|
| `ino.flutter/lib/state/skills_bloc.dart` | SkillsBloc — list, install, track installed skills |
| `ino.flutter/lib/screens/skills/skills_screen.dart` | Skill browser with domain filtering |
| `ino.flutter/lib/ui/components/skill_card.dart` | SkillCard rfw component |

### Flutter — Modified files

| File | Change |
|---|---|
| `ino.flutter/lib/app.dart` | Add `/skills` route |
| `ino.flutter/lib/main.dart` | Add `SkillsBloc` provider |
| `ino.flutter/lib/screens/home/home_screen.dart` | Add skill browser navigation button |

---

## Task 1: SkillOrigin enum and RegistryState

**Files:**
- Create: `iaw/Core/Registry/SkillOrigin.cs`
- Create: `iaw/Core/Registry/RegistryState.cs`

- [ ] **Step 1: Create SkillOrigin enum**

Create `iaw/Core/Registry/SkillOrigin.cs`:

```csharp
namespace Core.Registry;

public enum SkillOrigin
{
    CompileTime,
    Runtime,
    Imported
}
```

- [ ] **Step 2: Create RegistryState**

Create `iaw/Core/Registry/RegistryState.cs`:

```csharp
namespace Core.Registry;

[GenerateSerializer]
public sealed class RegistryState
{
    [Id(0)]
    public Dictionary<string, AgentRecord> Records { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 3: Build**

```bash
cd E:/ino && dotnet build ino.slnx
```

- [ ] **Step 4: Commit**

```bash
git add iaw/Core/Registry/SkillOrigin.cs iaw/Core/Registry/RegistryState.cs
git commit -m "feat(registry): add SkillOrigin enum and RegistryState for persistence"
```

---

## Task 2: Extend AgentRecord with domain/skill fields

**Files:**
- Modify: `iaw/Core/Registry/AgentRecord.cs`

- [ ] **Step 1: Read the current AgentRecord**

Read `iaw/Core/Registry/AgentRecord.cs` to see the existing fields and their `[Id(N)]` attributes. The highest existing Id is `[Id(8)]` for `DescriptionEmbedding`.

- [ ] **Step 2: Add new fields**

Add these fields after the existing ones, continuing the `[Id]` sequence:

```csharp
[VectorStoreData(IsIndexed = true)]
[Id(9)]
public string Domain { get; set; } = "";

[Id(10)]
public SkillOrigin Origin { get; set; } = SkillOrigin.CompileTime;

[Id(11)]
public string? UISchema { get; set; }

[Id(12)]
public string? ScriptSource { get; set; }

[Id(13)]
public string? SynapseSchema { get; set; }

[Id(14)]
public DateTimeOffset? InstalledAt { get; set; }
```

- [ ] **Step 3: Build and verify**

```bash
cd E:/ino && dotnet build ino.slnx
```

- [ ] **Step 4: Run tests to check for regressions**

```bash
cd E:/ino && dotnet test ino.slnx
```

All existing tests should pass — the new fields have defaults and don't break serialization.

- [ ] **Step 5: Commit**

```bash
git add iaw/Core/Registry/AgentRecord.cs
git commit -m "feat(registry): extend AgentRecord with Domain, Origin, UISchema, ScriptSource, SynapseSchema"
```

---

## Task 3: Make AgentRegistryGrain persistent

**Files:**
- Modify: `iaw/Core/Registry/AgentRegistryGrain.cs`
- Modify: `iaw/Core/Registry/IAgentRegistry.cs`

- [ ] **Step 1: Read current files**

Read both `AgentRegistryGrain.cs` and `IAgentRegistry.cs` in full.

- [ ] **Step 2: Add new methods to IAgentRegistry**

Add to the `IAgentRegistry` interface:

```csharp
Task<AgentRecord> InstallSkillAsync(AgentRecord record, CancellationToken ct = default);
Task<List<AgentRecord>> ListByDomainAsync(string domain, CancellationToken ct = default);
Task<bool> UninstallSkillAsync(string agentType, CancellationToken ct = default);
```

- [ ] **Step 3: Rewrite AgentRegistryGrain to use persistent state**

Replace the in-memory `_records` dictionary with `IPersistentState<RegistryState>`. The grain must:

1. Inject `[PersistentState("registry", "Default")] IPersistentState<RegistryState> store` via constructor
2. On `OnActivateAsync`: discover compile-time agents AND merge with persisted state (persisted state wins for records that exist in both — preserves runtime-installed skills)
3. `RegisterAsync`: write to `_store.State.Records` + `await _store.WriteStateAsync()`
4. `InstallSkillAsync`: same as Register but sets `Origin = Runtime`, `InstalledAt = DateTimeOffset.UtcNow`, then writes state
5. `ListByDomainAsync`: filter `_store.State.Records.Values` by `Domain`
6. `UninstallSkillAsync`: remove from dict if `Origin != CompileTime`, write state
7. All existing search/get methods use `_store.State.Records` instead of `_records`

Key: the `Dictionary<string, AgentRecord> _records` field (line 8) is replaced by `_store.State.Records`. Every read of `_records` becomes `_store.State.Records`. Every mutation adds `await _store.WriteStateAsync()`.

- [ ] **Step 4: Build and test**

```bash
cd E:/ino && dotnet build ino.slnx && dotnet test ino.slnx
```

- [ ] **Step 5: Commit**

```bash
git add iaw/Core/Registry/AgentRegistryGrain.cs iaw/Core/Registry/IAgentRegistry.cs
git commit -m "feat(registry): persistent AgentRegistryGrain with InstallSkill and domain listing"
```

---

## Task 4: Set Origin on compile-time discovery

**Files:**
- Modify: `iaw/Core/Registry/AgentRegistrationStartupTask.cs`

- [ ] **Step 1: Read the file**

Read `AgentRegistrationStartupTask.cs` in full.

- [ ] **Step 2: Set Origin and Domain in BuildRecord**

In the `BuildRecord` method, after building the `AgentRecord`, set:

```csharp
record.Origin = SkillOrigin.CompileTime;
record.Domain = ExtractDomain(agentType);
```

Add a helper method:

```csharp
static string ExtractDomain(Type agentType)
{
    var ns = agentType.Namespace ?? "";
    if (ns.Contains("Agents.CSharp")) return "coding";
    if (ns.Contains("Orchestration")) return "system";
    if (ns.Contains("Agents")) return "system";
    return "general";
}
```

- [ ] **Step 3: Build and test**

```bash
cd E:/ino && dotnet build ino.slnx && dotnet test ino.slnx
```

- [ ] **Step 4: Commit**

```bash
git add iaw/Core/Registry/AgentRegistrationStartupTask.cs
git commit -m "feat(registry): tag compile-time agents with Origin and Domain"
```

---

## Task 5: Seed travel skill metadata

**Files:**
- Create: `features/ino-new/InoNew.Core/Skills/TravelSkillSeeder.cs`

- [ ] **Step 1: Create the seeder**

Create `features/ino-new/InoNew.Core/Skills/TravelSkillSeeder.cs`:

```csharp
using Core.Registry;

namespace InoNew.Core.Skills;

public static class TravelSkillSeeder
{
    public static List<AgentRecord> GetTravelSkills() =>
    [
        new AgentRecord
        {
            Id = Guid.NewGuid(),
            AgentType = "FlightSearchNeuron",
            Namespace = "travel",
            DisplayName = "Flight Search",
            Description = "Search flights by origin, destination, and dates. Track prices and find deals via SerpApi.",
            Capabilities = ["flight_search", "price_tracking", "date_flexibility"],
            InterfaceName = "INeuron",
            RoutingExamples = ["find flights to Barcelona", "cheapest flight from NYC to London in June", "track flight prices"],
            Domain = "travel",
            Origin = SkillOrigin.Runtime,
            InstalledAt = DateTimeOffset.UtcNow
        },
        new AgentRecord
        {
            Id = Guid.NewGuid(),
            AgentType = "HotelSearchNeuron",
            Namespace = "travel",
            DisplayName = "Hotel Search",
            Description = "Search hotels by location, dates, and guest count. Compare prices and ratings via SerpApi.",
            Capabilities = ["hotel_search", "price_comparison", "rating_filter"],
            InterfaceName = "INeuron",
            RoutingExamples = ["find hotels in Paris", "best hotels near Times Square under $200", "hotel with pool in Miami"],
            Domain = "travel",
            Origin = SkillOrigin.Runtime,
            InstalledAt = DateTimeOffset.UtcNow
        },
        new AgentRecord
        {
            Id = Guid.NewGuid(),
            AgentType = "PlaceDiscoveryNeuron",
            Namespace = "travel",
            DisplayName = "Place Discovery",
            Description = "Discover restaurants, attractions, and local places. Reviews from Google Maps, Yelp, TripAdvisor.",
            Capabilities = ["place_search", "restaurant_reviews", "attraction_info", "local_tips"],
            InterfaceName = "INeuron",
            RoutingExamples = ["best restaurants in Rome", "things to do in Tokyo", "coffee shops near me"],
            Domain = "travel",
            Origin = SkillOrigin.Runtime,
            InstalledAt = DateTimeOffset.UtcNow
        }
    ];
}
```

- [ ] **Step 2: Build**

```bash
cd E:/ino && dotnet build ino.slnx
```

- [ ] **Step 3: Commit**

```bash
git add features/ino-new/InoNew.Core/Skills/
git commit -m "feat(travel): seed 3 travel skill metadata records (FlightSearch, HotelSearch, PlaceDiscovery)"
```

---

## Task 6: Implement gRPC skill endpoints

**Files:**
- Modify: `iaw/Grpc/Services/InoService.cs`

- [ ] **Step 1: Read current InoService.cs**

Read the file to see current stub implementations and how other methods work.

- [ ] **Step 2: Implement ListSkills**

Replace the `ListSkills` stub:

```csharp
public override async Task<ListSkillsResponse> ListSkills(ListSkillsRequest request, ServerCallContext context)
{
    var registry = clusterClient.GetGrain<IAgentRegistry>("global");

    List<AgentRecord> records;
    if (!string.IsNullOrEmpty(request.Domain))
        records = await registry.ListByDomainAsync(request.Domain, context.CancellationToken);
    else
        records = await registry.GetAllAsync(context.CancellationToken);

    var response = new ListSkillsResponse();
    foreach (var r in records)
    {
        if (!string.IsNullOrEmpty(request.Query) &&
            !r.DisplayName.Contains(request.Query, StringComparison.OrdinalIgnoreCase) &&
            !r.Description.Contains(request.Query, StringComparison.OrdinalIgnoreCase))
            continue;

        response.Skills.Add(new SkillInfo
        {
            Id = r.AgentType,
            Name = r.DisplayName,
            Domain = r.Domain,
            Description = r.Description,
            Installed = r.Origin == SkillOrigin.Runtime || r.Origin == SkillOrigin.CompileTime
        });
    }
    return response;
}
```

Add required usings: `using Core.Registry;`

- [ ] **Step 3: Implement InstallSkill**

Replace the `InstallSkill` stub. For Phase 2a, "install" means registering a travel seed record:

```csharp
public override async Task<InstallSkillResponse> InstallSkill(InstallSkillRequest request, ServerCallContext context)
{
    var registry = clusterClient.GetGrain<IAgentRegistry>("global");

    var travelSkills = InoNew.Core.Skills.TravelSkillSeeder.GetTravelSkills();
    var skill = travelSkills.FirstOrDefault(s =>
        s.AgentType.Equals(request.SkillId, StringComparison.OrdinalIgnoreCase));

    if (skill is null)
        return new InstallSkillResponse { Ok = false };

    var installed = await registry.InstallSkillAsync(skill, context.CancellationToken);
    return new InstallSkillResponse { Ok = true, NeuronId = installed.AgentType };
}
```

- [ ] **Step 4: Implement GetSkillUI**

Replace the `GetSkillUI` stub. Return a basic rfw chat template for now:

```csharp
public override async Task<SkillUIResponse> GetSkillUI(SkillUIRequest request, ServerCallContext context)
{
    var registry = clusterClient.GetGrain<IAgentRegistry>("global");
    var record = await registry.GetByAgentTypeAsync(request.SkillId, context.CancellationToken);

    if (record is null)
        return new SkillUIResponse();

    var rfwText = $"""
        import core;
        import material;
        import ino;

        widget root = Column(
          children: [
            Text(text: "{record.DisplayName}", style: {{ fontSize: 20.0 }}),
            Text(text: "{record.Description}"),
          ],
        );
        """;

    return new SkillUIResponse
    {
        RfwDescription = Google.Protobuf.ByteString.CopyFromUtf8(rfwText)
    };
}
```

- [ ] **Step 5: Add project reference if needed**

The Grpc project needs to reference InoNew.Core (for TravelSkillSeeder). Check if `iaw/Grpc/Grpc.csproj` already has this reference — it should from Phase 1.

- [ ] **Step 6: Build and test**

```bash
cd E:/ino && dotnet build ino.slnx && dotnet test ino.slnx
```

- [ ] **Step 7: Commit**

```bash
git add iaw/Grpc/Services/InoService.cs
git commit -m "feat(grpc): implement ListSkills, InstallSkill, GetSkillUI against persistent registry"
```

---

## Task 7: Flutter SkillsBloc

**Files:**
- Create: `ino.flutter/lib/state/skills_bloc.dart`
- Create: `ino.flutter/test/state/skills_bloc_test.dart`

- [ ] **Step 1: Read existing BLoC patterns**

Read `ino.flutter/lib/state/ino_bloc.dart` to follow the same naming and structure conventions.

- [ ] **Step 2: Write SkillsBloc**

Create `ino.flutter/lib/state/skills_bloc.dart`:

```dart
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart' as pb;

sealed class SkillsBlocEvent {}

class LoadSkills extends SkillsBlocEvent {
  LoadSkills({this.domain = '', this.query = ''});
  final String domain;
  final String query;
}

class InstallSkillRequested extends SkillsBlocEvent {
  InstallSkillRequested(this.skillId);
  final String skillId;
}

class SkillsBlocState {
  const SkillsBlocState({
    this.skills = const [],
    this.isLoading = false,
    this.installingId,
    this.error,
  });

  final List<SkillItem> skills;
  final bool isLoading;
  final String? installingId;
  final String? error;

  SkillsBlocState copyWith({
    List<SkillItem>? skills,
    bool? isLoading,
    String? installingId,
    String? error,
  }) {
    return SkillsBlocState(
      skills: skills ?? this.skills,
      isLoading: isLoading ?? this.isLoading,
      installingId: installingId,
      error: error,
    );
  }
}

class SkillItem {
  const SkillItem({
    required this.id,
    required this.name,
    required this.domain,
    required this.description,
    required this.installed,
  });

  factory SkillItem.fromProto(pb.SkillInfo info) {
    return SkillItem(
      id: info.id,
      name: info.name,
      domain: info.domain,
      description: info.description,
      installed: info.installed,
    );
  }

  final String id;
  final String name;
  final String domain;
  final String description;
  final bool installed;
}

class SkillsBloc extends Bloc<SkillsBlocEvent, SkillsBlocState> {
  SkillsBloc({required InoGrpcClient client})
      : _client = client,
        super(const SkillsBlocState()) {
    on<LoadSkills>(_onLoad);
    on<InstallSkillRequested>(_onInstall);
  }

  final InoGrpcClient _client;

  Future<void> _onLoad(LoadSkills event, Emitter<SkillsBlocState> emit) async {
    emit(state.copyWith(isLoading: true, error: null));
    try {
      final response = await _client.listSkills(
        domain: event.domain,
        query: event.query,
      );
      final items = response.skills.map(SkillItem.fromProto).toList();
      emit(state.copyWith(skills: items, isLoading: false));
    } catch (e) {
      emit(state.copyWith(isLoading: false, error: '$e'));
    }
  }

  Future<void> _onInstall(
    InstallSkillRequested event,
    Emitter<SkillsBlocState> emit,
  ) async {
    emit(state.copyWith(installingId: event.skillId));
    try {
      final response = await _client.installSkill(event.skillId);
      if (response.ok) {
        add(LoadSkills());
      } else {
        emit(state.copyWith(error: 'Install failed'));
      }
    } catch (e) {
      emit(state.copyWith(error: '$e'));
    }
  }
}
```

- [ ] **Step 3: Write test**

Create `ino.flutter/test/state/skills_bloc_test.dart`:

```dart
import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/state/skills_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart' as pb;
import 'package:mocktail/mocktail.dart';

class MockInoGrpcClient extends Mock implements InoGrpcClient {}

void main() {
  late MockInoGrpcClient mockClient;

  setUp(() {
    mockClient = MockInoGrpcClient();
  });

  group('SkillsBloc', () {
    test('initial state has empty skills', () {
      final bloc = SkillsBloc(client: mockClient);
      expect(bloc.state.skills, isEmpty);
      expect(bloc.state.isLoading, isFalse);
    });

    blocTest<SkillsBloc, SkillsBlocState>(
      'LoadSkills fetches and emits skills',
      build: () {
        when(() => mockClient.listSkills(domain: any(named: 'domain'), query: any(named: 'query')))
            .thenAnswer((_) async {
          final response = pb.ListSkillsResponse();
          response.skills.add(pb.SkillInfo()
            ..id = 'FlightSearchNeuron'
            ..name = 'Flight Search'
            ..domain = 'travel'
            ..description = 'Search flights'
            ..installed = false);
          return response;
        });
        return SkillsBloc(client: mockClient);
      },
      act: (bloc) => bloc.add(LoadSkills()),
      expect: () => [
        isA<SkillsBlocState>().having((s) => s.isLoading, 'loading', true),
        isA<SkillsBlocState>()
            .having((s) => s.skills.length, 'count', 1)
            .having((s) => s.skills.first.name, 'name', 'Flight Search')
            .having((s) => s.isLoading, 'loading', false),
      ],
    );
  });
}
```

- [ ] **Step 4: Run tests**

```bash
cd E:/ino/ino.flutter && flutter test test/state/skills_bloc_test.dart
```

- [ ] **Step 5: Commit**

```bash
cd E:/ino && git add ino.flutter/lib/state/skills_bloc.dart ino.flutter/test/state/skills_bloc_test.dart
git commit -m "feat(flutter): SkillsBloc with LoadSkills and InstallSkill events"
```

---

## Task 8: Flutter skill browser screen

**Files:**
- Create: `ino.flutter/lib/screens/skills/skills_screen.dart`
- Create: `ino.flutter/lib/ui/components/skill_card.dart`
- Modify: `ino.flutter/lib/app.dart`
- Modify: `ino.flutter/lib/main.dart`
- Modify: `ino.flutter/lib/screens/home/home_screen.dart`

- [ ] **Step 1: Read existing files**

Read `app.dart`, `main.dart`, `home_screen.dart` to understand the current structure.

- [ ] **Step 2: Create SkillCard component**

Create `ino.flutter/lib/ui/components/skill_card.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:ino_flutter/state/skills_bloc.dart';

class SkillCard extends StatelessWidget {
  const SkillCard({
    super.key,
    required this.skill,
    this.installing = false,
    this.onInstall,
  });

  final SkillItem skill;
  final bool installing;
  final VoidCallback? onInstall;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      color: theme.colorScheme.surfaceContainerHighest,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(
                  _iconForDomain(skill.domain),
                  color: theme.colorScheme.primary,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    skill.name,
                    style: theme.textTheme.titleMedium?.copyWith(
                      color: theme.colorScheme.onSurface,
                    ),
                  ),
                ),
                if (skill.installed)
                  Chip(
                    label: const Text('Installed'),
                    backgroundColor: theme.colorScheme.primaryContainer,
                    labelStyle: TextStyle(
                      color: theme.colorScheme.onPrimaryContainer,
                      fontSize: 12,
                    ),
                  )
                else if (installing)
                  const SizedBox(
                    width: 24,
                    height: 24,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                else
                  FilledButton.tonal(
                    onPressed: onInstall,
                    child: const Text('Install'),
                  ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              skill.description,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 4),
            Text(
              skill.domain.toUpperCase(),
              style: theme.textTheme.labelSmall?.copyWith(
                color: theme.colorScheme.outline,
              ),
            ),
          ],
        ),
      ),
    );
  }

  IconData _iconForDomain(String domain) {
    return switch (domain) {
      'travel' => Icons.flight,
      'coding' => Icons.code,
      'system' => Icons.settings,
      'finance' => Icons.attach_money,
      'health' => Icons.health_and_safety,
      _ => Icons.extension,
    };
  }
}
```

- [ ] **Step 3: Create SkillsScreen**

Create `ino.flutter/lib/screens/skills/skills_screen.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/state/skills_bloc.dart';
import 'package:ino_flutter/ui/components/skill_card.dart';

class SkillsScreen extends StatefulWidget {
  const SkillsScreen({super.key});

  @override
  State<SkillsScreen> createState() => _SkillsScreenState();
}

class _SkillsScreenState extends State<SkillsScreen> {
  String _selectedDomain = '';

  @override
  void initState() {
    super.initState();
    context.read<SkillsBloc>().add(LoadSkills());
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        title: const Text('Skills'),
        backgroundColor: Colors.transparent,
      ),
      body: Column(
        children: [
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: Row(
              children: [
                _DomainChip(
                  label: 'All',
                  selected: _selectedDomain.isEmpty,
                  onSelected: () => _filterDomain(''),
                ),
                _DomainChip(
                  label: 'Travel',
                  selected: _selectedDomain == 'travel',
                  onSelected: () => _filterDomain('travel'),
                ),
                _DomainChip(
                  label: 'Coding',
                  selected: _selectedDomain == 'coding',
                  onSelected: () => _filterDomain('coding'),
                ),
                _DomainChip(
                  label: 'System',
                  selected: _selectedDomain == 'system',
                  onSelected: () => _filterDomain('system'),
                ),
              ],
            ),
          ),
          Expanded(
            child: BlocBuilder<SkillsBloc, SkillsBlocState>(
              builder: (context, state) {
                if (state.isLoading) {
                  return const Center(child: CircularProgressIndicator());
                }
                if (state.skills.isEmpty) {
                  return const Center(
                    child: Text(
                      'No skills available',
                      style: TextStyle(color: Colors.white54),
                    ),
                  );
                }
                return ListView.builder(
                  padding: const EdgeInsets.all(16),
                  itemCount: state.skills.length,
                  itemBuilder: (context, index) {
                    final skill = state.skills[index];
                    return Padding(
                      padding: const EdgeInsets.only(bottom: 8),
                      child: SkillCard(
                        skill: skill,
                        installing: state.installingId == skill.id,
                        onInstall: () => context
                            .read<SkillsBloc>()
                            .add(InstallSkillRequested(skill.id)),
                      ),
                    );
                  },
                );
              },
            ),
          ),
        ],
      ),
    );
  }

  void _filterDomain(String domain) {
    setState(() => _selectedDomain = domain);
    context.read<SkillsBloc>().add(LoadSkills(domain: domain));
  }
}

class _DomainChip extends StatelessWidget {
  const _DomainChip({
    required this.label,
    required this.selected,
    required this.onSelected,
  });

  final String label;
  final bool selected;
  final VoidCallback onSelected;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(right: 8),
      child: FilterChip(
        label: Text(label),
        selected: selected,
        onSelected: (_) => onSelected(),
      ),
    );
  }
}
```

- [ ] **Step 4: Wire into app.dart**

Add the `/skills` route and import:

```dart
import 'package:ino_flutter/screens/skills/skills_screen.dart';
```

Add to `routes` list:
```dart
GoRoute(
  path: '/skills',
  builder: (context, state) => const SkillsScreen(),
),
```

- [ ] **Step 5: Add SkillsBloc to main.dart**

Add import:
```dart
import 'package:ino_flutter/state/skills_bloc.dart';
```

Add to `MultiBlocProvider.providers`:
```dart
BlocProvider(create: (_) => SkillsBloc(client: client)),
```

- [ ] **Step 6: Add navigation to home screen**

Add a skills button to the home screen. In `home_screen.dart`, add an `IconButton` in the app bar or a FAB that navigates to `/skills`:

```dart
// Add to the top of the Column, after PersonaWidget:
Row(
  mainAxisAlignment: MainAxisAlignment.end,
  children: [
    IconButton(
      icon: const Icon(Icons.extension, color: Colors.white54),
      onPressed: () => context.go('/skills'),
      tooltip: 'Skills',
    ),
  ],
),
```

Import `go_router`:
```dart
import 'package:go_router/go_router.dart';
```

- [ ] **Step 7: Run flutter analyze and tests**

```bash
cd E:/ino/ino.flutter && flutter analyze --no-fatal-infos && flutter test
```

- [ ] **Step 8: Commit**

```bash
cd E:/ino && git add ino.flutter/
git commit -m "feat(flutter): skill browser screen with domain filtering and install flow"
```

---

## Task 9: Integration verification

**Files:** None new — verification only.

- [ ] **Step 1: Build everything**

```bash
cd E:/ino && dotnet build ino.slnx
cd E:/ino/ino.flutter && flutter build web
```

- [ ] **Step 2: Run all tests**

```bash
cd E:/ino && dotnet test ino.slnx
cd E:/ino/ino.flutter && flutter test
```

- [ ] **Step 3: Start Aspire and verify**

```bash
aspire start
```

Check the dashboard — `grpc` resource should be Healthy.

- [ ] **Step 4: Test skill listing via gRPC**

From the Flutter app or grpcurl:
```bash
grpcurl -plaintext -d '{"domain": ""}' localhost:5400 ino.v1.Ino/ListSkills
```

Should return all registered agents (compile-time + any installed skills).

- [ ] **Step 5: Test skill install via gRPC**

```bash
grpcurl -plaintext -d '{"skill_id": "FlightSearchNeuron"}' localhost:5400 ino.v1.Ino/InstallSkill
```

Should return `{ "ok": true, "neuronId": "FlightSearchNeuron" }`.

- [ ] **Step 6: Stop Aspire**

```bash
aspire stop
```

---

## Summary

| Task | What it delivers |
|---|---|
| 1 | `SkillOrigin` enum + `RegistryState` persistence class |
| 2 | `AgentRecord` extended with Domain, Origin, UISchema, ScriptSource, SynapseSchema |
| 3 | `AgentRegistryGrain` persistent via `IPersistentState`, InstallSkill + ListByDomain |
| 4 | Compile-time agents tagged with Origin + Domain |
| 5 | 3 travel skill seed records (FlightSearch, HotelSearch, PlaceDiscovery) |
| 6 | gRPC ListSkills/InstallSkill/GetSkillUI implemented against registry |
| 7 | Flutter `SkillsBloc` with load + install |
| 8 | Flutter skill browser screen with domain chips + install buttons |
| 9 | Integration verification |
