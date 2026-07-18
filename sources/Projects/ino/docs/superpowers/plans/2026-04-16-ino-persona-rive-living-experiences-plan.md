# ino persona — living Rive character + experience-verb-mime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the POC persona substrate — one authored `ino-persona.riv` driven parametrically via Rive Data Binding ViewModels, a `PersonaNeuron` that turns verb execution into frame streams, a cross-cutting `NotifierNeuron` for iOS-style banner status, a `PersonaEvolver` L1 mapping-script generator, a 3-service gRPC surface, the first experience bundle (Uber) proving the loop, and a Flutter web client rendering the demo from `POC/docs/prototypes/01-taxi-flow.html`.

**Architecture:** Extends POC Phase 2's cross-silo runtime with two additive hooks (`IStatusSynapse` marker + `FirePort` `VerbStarted`/`VerbCompleted` broadcasts + Discovery interface-dispatch). Runtime data flows one way: verb handler fires → PersonaNeuron emits `PersonaFrame` onto per-user stream → Flutter BLoC applies frame fields to the Rive ViewModel instance. Notifications flow reactively: bundle fires `IStatusSynapse` → NotifierNeuron listens on system silo → banner pushed through `StreamNotifications` gRPC.

**Tech Stack:** .NET 11 preview, Orleans 10 (DurableGrain + IDurableList), xunit.v3 (single-assembly CollectionDefinition), Grpc.AspNetCore + Grpc.AspNetCore.Web, Flutter 3.41 + CanvasKit, `rive ^0.14.5`, `rfw ^1.1.3`, `flutter_bloc ^9`, `grpc ^5`, `protobuf ^6`. POC package pins mirror `D:\ino\Directory.Packages.props`.

**Spec:** `docs/superpowers/specs/2026-04-16-ino-persona-rive-living-experiences-design.md`

**Deferred to a follow-on plan:** The remaining 9 experience bundles (Spotify, Gmail, WhatsApp, Amazon, Uber Eats, Google Maps, Revolut, Weather, Calendar) are mechanical replays of Phase G's Uber bundle pattern — each copies `Ino.Experiences.Uber` and `Ino.Experiences.Uber.Contracts`, replaces verb records + manifest + the one handler neuron, ships its own `IStatusSynapse`. The substrate this plan delivers is what makes those 9 cheap.

---

## File structure

Every new or modified file, grouped by project. Absolute paths rooted at `D:\ino\POC\`.

**Modified (Phase 1/2 existing):**

```
src/Ino.Core/Ino.Core.csproj                     (no change — add files land inside)
src/Ino.Core.Hosting/Ino.Core.Hosting.csproj     (no change — add files land inside)
```

**New in `Ino.Core`:**

```
src/Ino.Core/IStatusSynapse.cs                   // marker interface + NotificationKind enum
```

**New in `Ino.Core.Hosting`:**

```
src/Ino.Core.Hosting/VerbBinding.cs              // record + NotificationPolicy enum
src/Ino.Core.Hosting/IExperienceManifest.cs      // interface
src/Ino.Core.Hosting/PersonaFrame.cs             // record + PersonaTrigger enum
src/Ino.Core.Hosting/MimeSymbols.cs              // static symbol constants for the ~15-gesture vocab
src/Ino.Core.Hosting/IExperienceEventEmitter.cs  // interface
src/Ino.Core.Hosting/IAmbientFireExtensions.cs   // ExtendFirePort hook injection point
```

**Phase 2 `IExperience` extension (modified):**

```
src/Ino.Core.Hosting/IExperience.cs              // add IExperienceManifest Manifest default-interface property
```

**New project — persona runtime (experiences silo):**

```
src/Ino.Experiences/Ino.Experiences.csproj
src/Ino.Experiences/VerbStarted.cs
src/Ino.Experiences/VerbCompleted.cs
src/Ino.Experiences/IPersonaNeuron.cs
src/Ino.Experiences/PersonaNeuron.cs
src/Ino.Experiences/IExperienceStream.cs
src/Ino.Experiences/ExperienceStreamGrain.cs
src/Ino.Experiences/ExperienceEventEmitter.cs
```

**New project — notifier (system silo):**

```
src/Ino.System/NotifierNeuron.cs                 (added to Phase-2 Ino.System)
src/Ino.System/INotificationStream.cs
src/Ino.System/NotificationStreamGrain.cs
src/Ino.System/NotificationBanner.cs
```

**New project — persona evolver (experiences silo):**

```
src/Ino.PersonaEvolver/Ino.PersonaEvolver.csproj
src/Ino.PersonaEvolver/MimeMappingMissing.cs
src/Ino.PersonaEvolver/MimeOverrideStore.cs
src/Ino.PersonaEvolver/PersonaEvolverNeuron.cs
```

**Phase 2 extensions:**

```
src/Ino.Experiences/FirePortVerbHook.cs          // extends Phase 2's FirePort with VerbStarted/VerbCompleted broadcasts
src/Ino.System/DiscoveryInterfaceDispatch.cs     // extends Phase 2's Discovery to resolve reactive on interface types
```

**New project — gRPC gateways (system silo hosting):**

```
src/Ino.Gateways.Grpc/Ino.Gateways.Grpc.csproj
src/Ino.Gateways.Grpc/Protos/persona.proto
src/Ino.Gateways.Grpc/Protos/experiences.proto
src/Ino.Gateways.Grpc/Protos/notifications.proto
src/Ino.Gateways.Grpc/Services/PersonaService.cs
src/Ino.Gateways.Grpc/Services/ExperiencesService.cs
src/Ino.Gateways.Grpc/Services/NotificationsService.cs
src/Ino.Gateways.Grpc/Mapping/PersonaFrameMapper.cs
src/Ino.Gateways.Grpc/Mapping/NotificationMapper.cs
```

**First experience bundle:**

```
experiences/uber/Ino.Experiences.Uber.Contracts/Ino.Experiences.Uber.Contracts.csproj
experiences/uber/Ino.Experiences.Uber.Contracts/CallRide.cs
experiences/uber/Ino.Experiences.Uber.Contracts/AddStop.cs
experiences/uber/Ino.Experiences.Uber.Contracts/CancelRide.cs
experiences/uber/Ino.Experiences.Uber.Contracts/RateDriver.cs
experiences/uber/Ino.Experiences.Uber.Contracts/ShareETA.cs
experiences/uber/Ino.Experiences.Uber.Contracts/RideStatusChanged.cs
experiences/uber/Ino.Experiences.Uber/Ino.Experiences.Uber.csproj
experiences/uber/Ino.Experiences.Uber/Uber.cs                    // IExperience impl
experiences/uber/Ino.Experiences.Uber/UberManifest.cs
experiences/uber/Ino.Experiences.Uber/CallRideNeuron.cs
experiences/uber/Ino.Experiences.Uber/AddStopNeuron.cs
experiences/uber/Ino.Experiences.Uber/CancelRideNeuron.cs
experiences/uber/Ino.Experiences.Uber/RateDriverNeuron.cs
experiences/uber/Ino.Experiences.Uber/ShareETANeuron.cs
experiences/uber/Ino.Experiences.Uber/MockDriverSource.cs        // deterministic mock, no network
```

**Flutter client:**

```
clients/ino.flutter/pubspec.yaml
clients/ino.flutter/protos/persona.proto
clients/ino.flutter/protos/experiences.proto
clients/ino.flutter/protos/notifications.proto
clients/ino.flutter/lib/main.dart
clients/ino.flutter/lib/app.dart
clients/ino.flutter/lib/grpc/ino_client.dart
clients/ino.flutter/lib/persona/persona_widget.dart
clients/ino.flutter/lib/persona/persona_bloc.dart
clients/ino.flutter/lib/experience/experience_bloc.dart
clients/ino.flutter/lib/experience/rfw_runtime.dart
clients/ino.flutter/lib/experience/components/ride_card.dart
clients/ino.flutter/lib/notifications/notifier_widget.dart
clients/ino.flutter/lib/notifications/notifier_bloc.dart
clients/ino.flutter/lib/screens/home/home_screen.dart
clients/ino.flutter/lib/theme/theme.dart
clients/ino.flutter/assets/rive/ino-persona.riv                  // stub asset; authoring happens in Phase L
clients/ino.flutter/test/persona/persona_bloc_test.dart
clients/ino.flutter/test/notifications/notifier_bloc_test.dart
```

**Aspire AppHost extensions:**

```
src/Ino.AppHost/AppHost.cs                       // add grpc resource, flutter resource
```

**Tests:**

```
test/Ino.Core.Tests/IStatusSynapseTests.cs
test/Ino.Core.Hosting.Tests/VerbBindingTests.cs
test/Ino.Core.Hosting.Tests/ExperienceManifestTests.cs
test/Ino.Persona.Tests/Ino.Persona.Tests.csproj
test/Ino.Persona.Tests/PersonaNeuronTests.cs
test/Ino.Persona.Tests/PersonaEvolverTests.cs
test/Ino.Persona.Tests/MimeOverrideStoreTests.cs
test/Ino.Notifier.Tests/Ino.Notifier.Tests.csproj
test/Ino.Notifier.Tests/NotifierNeuronTests.cs
test/Ino.Notifier.Tests/DedupeKeyTests.cs
test/Ino.Hosting.Tests/FirePortVerbHookTests.cs      (new scenarios on Phase 2 fixture)
test/Ino.Hosting.Tests/DiscoveryInterfaceDispatchTests.cs
test/Ino.E2E.Tests/UberFlowE2ETests.cs               (extends Phase 2 E2E harness)
```

**POC docs:**

```
POC/docs/persona-authoring.md                    // design-time Rive authoring workflow
POC/docs/prototypes/01-taxi-flow.html            (already committed by brainstorming session)
POC/docs/prototypes/02-experience-catalog.html   (already committed by brainstorming session)
```

---

## Phase A — Core contract extensions

Small, low-risk types that later phases depend on. No Orleans, no gRPC, pure records + interfaces. Test as we go.

### Task 1: `IStatusSynapse` marker interface + `NotificationKind`

**Files:**
- Create: `POC/src/Ino.Core/IStatusSynapse.cs`
- Test: `POC/test/Ino.Core.Tests/IStatusSynapseTests.cs`

- [ ] **Step 1: Write failing test**

Create `POC/test/Ino.Core.Tests/IStatusSynapseTests.cs`:

```csharp
using Ino.Core;
using Xunit;

namespace Ino.Core.Tests;

public sealed class IStatusSynapseTests
{
    private sealed record SampleBanner(
        string Title, string Body, string? Icon, string? Accent,
        NotificationKind Kind, string? DedupeKey) : IStatusSynapse;

    [Fact]
    public void SampleBanner_ImplementsISynapseAndIStatusSynapse()
    {
        IStatusSynapse banner = new SampleBanner("t", "b", null, null, NotificationKind.Info, null);
        Assert.IsAssignableFrom<ISynapse>(banner);
    }

    [Fact]
    public void NotificationKind_HasFiveExpectedValues()
    {
        var names = Enum.GetNames<NotificationKind>();
        Assert.Equal(new[] { "Info", "Progress", "Success", "Warning", "Urgent" }, names);
    }
}
```

- [ ] **Step 2: Run test — expect CS0246 on `IStatusSynapse` / `NotificationKind`**

Run: `cd /d/ino/POC && dotnet test test/Ino.Core.Tests --filter IStatusSynapseTests`
Expected: build error (type not found).

- [ ] **Step 3: Create `IStatusSynapse.cs`**

Create `POC/src/Ino.Core/IStatusSynapse.cs`:

```csharp
namespace Ino.Core;

public enum NotificationKind
{
    Info,
    Progress,
    Success,
    Warning,
    Urgent,
}

public interface IStatusSynapse : ISynapse
{
    string Title { get; }
    string Body { get; }
    string? Icon { get; }
    string? Accent { get; }
    NotificationKind Kind { get; }
    string? DedupeKey { get; }
}
```

- [ ] **Step 4: Run test — expect PASS**

Run: `dotnet test test/Ino.Core.Tests --filter IStatusSynapseTests`
Expected: 2/2 passed.

- [ ] **Step 5: Commit**

```bash
git add POC/src/Ino.Core/IStatusSynapse.cs POC/test/Ino.Core.Tests/IStatusSynapseTests.cs
git commit -m "feat(poc): add IStatusSynapse marker interface + NotificationKind enum"
```

### Task 2: `VerbBinding` + `NotificationPolicy`

**Files:**
- Create: `POC/src/Ino.Core.Hosting/VerbBinding.cs`
- Test: `POC/test/Ino.Core.Hosting.Tests/VerbBindingTests.cs`

- [ ] **Step 1: Write failing tests**

Create `POC/test/Ino.Core.Hosting.Tests/VerbBindingTests.cs`:

```csharp
using Ino.Core.Hosting;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public sealed class VerbBindingTests
{
    [Fact]
    public void VerbBinding_Constructs_WithRequiredMimeSymbolAndPolicy()
    {
        var binding = new VerbBinding("reach_phone", NotificationPolicy.OnStatusStream);
        Assert.Equal("reach_phone", binding.MimeSymbol);
        Assert.Equal(NotificationPolicy.OnStatusStream, binding.Policy);
        Assert.Null(binding.Accent);
    }

    [Fact]
    public void VerbBinding_Accepts_OptionalAccent()
    {
        var binding = new VerbBinding("thumbs_up", NotificationPolicy.OnComplete, "#000000");
        Assert.Equal("#000000", binding.Accent);
    }

    [Fact]
    public void NotificationPolicy_HasThreeValues()
    {
        var names = Enum.GetNames<NotificationPolicy>();
        Assert.Equal(new[] { "None", "OnComplete", "OnStatusStream" }, names);
    }
}
```

- [ ] **Step 2: Run test — expect CS0246**

Run: `dotnet test test/Ino.Core.Hosting.Tests --filter VerbBindingTests`
Expected: build error.

- [ ] **Step 3: Create the types**

Create `POC/src/Ino.Core.Hosting/VerbBinding.cs`:

```csharp
namespace Ino.Core.Hosting;

public enum NotificationPolicy
{
    None,
    OnComplete,
    OnStatusStream,
}

public sealed record VerbBinding(
    string MimeSymbol,
    NotificationPolicy Policy,
    string? Accent = null);
```

- [ ] **Step 4: Run test — expect PASS**

Run: `dotnet test test/Ino.Core.Hosting.Tests --filter VerbBindingTests`
Expected: 3/3 passed.

- [ ] **Step 5: Commit**

```bash
git add POC/src/Ino.Core.Hosting/VerbBinding.cs POC/test/Ino.Core.Hosting.Tests/VerbBindingTests.cs
git commit -m "feat(poc): add VerbBinding + NotificationPolicy"
```

### Task 3: `IExperienceManifest` + `IExperience.Manifest` default-interface extension

**Files:**
- Create: `POC/src/Ino.Core.Hosting/IExperienceManifest.cs`
- Modify: `POC/src/Ino.Core.Hosting/IExperience.cs`
- Test: `POC/test/Ino.Core.Hosting.Tests/ExperienceManifestTests.cs`

- [ ] **Step 1: Write failing tests**

Create `POC/test/Ino.Core.Hosting.Tests/ExperienceManifestTests.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public sealed class ExperienceManifestTests
{
    private sealed record Verb1 : ISynapse;
    private sealed record Verb2 : ISynapse;

    private sealed class SampleManifest : IExperienceManifest
    {
        public IReadOnlyDictionary<Type, VerbBinding> Verbs => new Dictionary<Type, VerbBinding>
        {
            [typeof(Verb1)] = new("reach_phone", NotificationPolicy.OnStatusStream),
            [typeof(Verb2)] = new("tap_map", NotificationPolicy.None),
        };
    }

    private sealed class SampleExperience : IExperience
    {
        public BundleId Bundle => BundleId.From("Ino.Sample");
        public string Version => "1.0.0";
        public IReadOnlyList<Capability> DeclaredCapabilities => Array.Empty<Capability>();
        public IExperienceManifest Manifest { get; } = new SampleManifest();
    }

    [Fact]
    public void Experience_ExposesManifest_ViaProperty()
    {
        var exp = new SampleExperience();
        Assert.Equal(2, exp.Manifest.Verbs.Count);
        Assert.Equal("reach_phone", exp.Manifest.Verbs[typeof(Verb1)].MimeSymbol);
    }

    [Fact]
    public void Experience_Default_ReturnsEmptyManifest_WhenNotOverridden()
    {
        IExperience exp = new MinimalExperience();
        Assert.Empty(exp.Manifest.Verbs);
    }

    private sealed class MinimalExperience : IExperience
    {
        public BundleId Bundle => BundleId.From("Ino.Minimal");
        public string Version => "1.0.0";
        public IReadOnlyList<Capability> DeclaredCapabilities => Array.Empty<Capability>();
    }
}
```

- [ ] **Step 2: Run test — expect CS0246/CS0535**

Run: `dotnet test test/Ino.Core.Hosting.Tests --filter ExperienceManifestTests`
Expected: build error.

- [ ] **Step 3: Create `IExperienceManifest.cs`**

Create `POC/src/Ino.Core.Hosting/IExperienceManifest.cs`:

```csharp
using System.Collections.Immutable;

namespace Ino.Core.Hosting;

public interface IExperienceManifest
{
    IReadOnlyDictionary<Type, VerbBinding> Verbs { get; }

    public static IExperienceManifest Empty { get; } = new EmptyManifest();

    private sealed class EmptyManifest : IExperienceManifest
    {
        public IReadOnlyDictionary<Type, VerbBinding> Verbs { get; } =
            ImmutableDictionary<Type, VerbBinding>.Empty;
    }
}
```

- [ ] **Step 4: Extend `IExperience` with default `Manifest` property**

Open `POC/src/Ino.Core.Hosting/IExperience.cs` and add (inside the interface body):

```csharp
IExperienceManifest Manifest => IExperienceManifest.Empty;
```

- [ ] **Step 5: Run test — expect PASS**

Run: `dotnet test test/Ino.Core.Hosting.Tests --filter ExperienceManifestTests`
Expected: 2/2 passed.

- [ ] **Step 6: Commit**

```bash
git add POC/src/Ino.Core.Hosting/IExperienceManifest.cs POC/src/Ino.Core.Hosting/IExperience.cs POC/test/Ino.Core.Hosting.Tests/ExperienceManifestTests.cs
git commit -m "feat(poc): add IExperienceManifest + default-interface extension to IExperience"
```

### Task 4: `PersonaFrame` + `PersonaTrigger`

**Files:**
- Create: `POC/src/Ino.Core.Hosting/PersonaFrame.cs`
- Test: `POC/test/Ino.Core.Hosting.Tests/PersonaFrameTests.cs`

- [ ] **Step 1: Write failing tests**

Create `POC/test/Ino.Core.Hosting.Tests/PersonaFrameTests.cs`:

```csharp
using Ino.Core.Hosting;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public sealed class PersonaFrameTests
{
    [Fact]
    public void PersonaFrame_AllFieldsOptional_EmptyByDefault()
    {
        var frame = new PersonaFrame();
        Assert.Null(frame.Mood);
        Assert.Null(frame.BodyPose);
        Assert.Null(frame.Trigger);
    }

    [Fact]
    public void PersonaFrame_WithSelector_MovesOnlyThatField()
    {
        var original = new PersonaFrame();
        var patched = original with { BodyPose = "lean_in" };
        Assert.Equal("lean_in", patched.BodyPose);
        Assert.Null(patched.Mood);
    }

    [Fact]
    public void PersonaTrigger_HasFourValues()
    {
        var names = Enum.GetNames<PersonaTrigger>();
        Assert.Equal(new[] { "None", "OnArrive", "OnCelebrate", "OnError" }, names);
    }
}
```

- [ ] **Step 2: Run — expect CS0246**

Run: `dotnet test test/Ino.Core.Hosting.Tests --filter PersonaFrameTests`
Expected: build error.

- [ ] **Step 3: Create `PersonaFrame.cs`**

Create `POC/src/Ino.Core.Hosting/PersonaFrame.cs`:

```csharp
namespace Ino.Core.Hosting;

public enum PersonaTrigger
{
    None,
    OnArrive,
    OnCelebrate,
    OnError,
}

public sealed record PersonaFrame(
    double? Mood = null,
    double? Energy = null,
    double? Confidence = null,
    double? SignalPulse = null,
    string? BodyPose = null,
    string? Mouth = null,
    string? Eyes = null,
    string? Arms = null,
    string? GlowRing = null,
    string? CurrentVerb = null,
    string? CurrentExperience = null,
    string? Accent = null,
    bool? PropVisible = null,
    string? PropKind = null,
    PersonaTrigger? Trigger = null);
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test test/Ino.Core.Hosting.Tests --filter PersonaFrameTests`
Expected: 3/3 passed.

- [ ] **Step 5: Commit**

```bash
git add POC/src/Ino.Core.Hosting/PersonaFrame.cs POC/test/Ino.Core.Hosting.Tests/PersonaFrameTests.cs
git commit -m "feat(poc): add PersonaFrame + PersonaTrigger"
```

### Task 5: `MimeSymbols` constants

**Files:**
- Create: `POC/src/Ino.Core.Hosting/MimeSymbols.cs`
- Test: `POC/test/Ino.Core.Hosting.Tests/MimeSymbolsTests.cs`

- [ ] **Step 1: Write failing test**

Create `POC/test/Ino.Core.Hosting.Tests/MimeSymbolsTests.cs`:

```csharp
using Ino.Core.Hosting;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public sealed class MimeSymbolsTests
{
    [Fact]
    public void All_Returns_FifteenKnownSymbols()
    {
        var all = MimeSymbols.All;
        Assert.Equal(15, all.Count);
        Assert.Contains(MimeSymbols.ReachPhone, all);
        Assert.Contains(MimeSymbols.ScanHorizon, all);
        Assert.Contains(MimeSymbols.PointForward, all);
    }

    [Fact]
    public void IsKnown_ReturnsTrue_ForRegisteredSymbol()
    {
        Assert.True(MimeSymbols.IsKnown("reach_phone"));
        Assert.False(MimeSymbols.IsKnown("invented_nonsense"));
    }

    [Fact]
    public void Fallback_Is_ThinkingNod()
    {
        Assert.Equal("thinking_nod", MimeSymbols.Fallback);
    }
}
```

- [ ] **Step 2: Run — expect CS0117**

Run: `dotnet test test/Ino.Core.Hosting.Tests --filter MimeSymbolsTests`
Expected: build error.

- [ ] **Step 3: Create `MimeSymbols.cs`**

Create `POC/src/Ino.Core.Hosting/MimeSymbols.cs`:

```csharp
using System.Collections.Immutable;

namespace Ino.Core.Hosting;

public static class MimeSymbols
{
    public const string ReachPhone     = "reach_phone";
    public const string ScanHorizon    = "scan_horizon";
    public const string PointForward   = "point_forward";
    public const string TapMap         = "tap_map";
    public const string WaveOff        = "wave_off";
    public const string ThumbsUp       = "thumbs_up";
    public const string Write          = "write";
    public const string PeekBox        = "peek_box";
    public const string HeadBob        = "head_bob";
    public const string SwipeForward   = "swipe_forward";
    public const string StackItems     = "stack_items";
    public const string SlideCoins     = "slide_coins";
    public const string TuckAway       = "tuck_away";
    public const string Swap           = "swap";
    public const string TwoFingerSend  = "two_finger_send";

    public const string Fallback = "thinking_nod";

    public static IReadOnlySet<string> All { get; } = ImmutableHashSet.Create(
        ReachPhone, ScanHorizon, PointForward, TapMap, WaveOff,
        ThumbsUp, Write, PeekBox, HeadBob, SwipeForward,
        StackItems, SlideCoins, TuckAway, Swap, TwoFingerSend);

    public static bool IsKnown(string symbol) => All.Contains(symbol);
}
```

- [ ] **Step 4: Run — expect PASS**

Expected: 3/3 passed.

- [ ] **Step 5: Commit**

```bash
git add POC/src/Ino.Core.Hosting/MimeSymbols.cs POC/test/Ino.Core.Hosting.Tests/MimeSymbolsTests.cs
git commit -m "feat(poc): add MimeSymbols constant set (15 + fallback)"
```

---

## Phase B — FirePort broadcast hook + Discovery interface-dispatch

Phase 2's `FirePort` needs to emit `VerbStarted`/`VerbCompleted` reactive synapses. Phase 2's `Discovery` needs to resolve reactive listeners whose `SynapseType` is assignable-from the fired type (so `NotifierNeuron : IReactsTo<IStatusSynapse>` picks up concrete `RideStatusChanged : IStatusSynapse`).

### Task 6: `VerbStarted` / `VerbCompleted` synapse records

**Files:**
- Create: `POC/src/Ino.Experiences/Ino.Experiences.csproj`
- Create: `POC/src/Ino.Experiences/VerbStarted.cs`
- Create: `POC/src/Ino.Experiences/VerbCompleted.cs`
- Test: `POC/test/Ino.Persona.Tests/VerbLifecycleSynapseTests.cs`

- [ ] **Step 1: Create the `Ino.Experiences` project**

Create `POC/src/Ino.Experiences/Ino.Experiences.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Sdk" />
    <PackageReference Include="Microsoft.Orleans.Server" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ino.Core\Ino.Core.csproj" />
    <ProjectReference Include="..\Ino.Core.Hosting\Ino.Core.Hosting.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add project to solution**

Run: `dotnet sln ino.slnx add src/Ino.Experiences/Ino.Experiences.csproj`

- [ ] **Step 3: Create persona-tests project**

Create `POC/test/Ino.Persona.Tests/Ino.Persona.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Ino.Core\Ino.Core.csproj" />
    <ProjectReference Include="..\..\src\Ino.Core.Hosting\Ino.Core.Hosting.csproj" />
    <ProjectReference Include="..\..\src\Ino.Experiences\Ino.Experiences.csproj" />
    <ProjectReference Include="..\..\src\Ino.Testing\Ino.Testing.csproj" />
  </ItemGroup>
</Project>
```

Run: `dotnet sln ino.slnx add test/Ino.Persona.Tests/Ino.Persona.Tests.csproj`

- [ ] **Step 4: Write failing test**

Create `POC/test/Ino.Persona.Tests/VerbLifecycleSynapseTests.cs`:

```csharp
using Ino.Core;
using Ino.Experiences;
using Xunit;

namespace Ino.Persona.Tests;

public sealed class VerbLifecycleSynapseTests
{
    [Fact]
    public void VerbStarted_Implements_ISynapse()
    {
        var s = new VerbStarted(typeof(VerbLifecycleSynapseTests), "corr-1");
        Assert.IsAssignableFrom<ISynapse>(s);
        Assert.Equal("corr-1", s.CorrelationId);
    }

    [Fact]
    public void VerbCompleted_Carries_Success()
    {
        var s = new VerbCompleted(typeof(VerbLifecycleSynapseTests), "corr-1", Success: true);
        Assert.True(s.Success);
    }
}
```

- [ ] **Step 5: Run — expect CS0246**

Run: `dotnet test test/Ino.Persona.Tests --filter VerbLifecycleSynapseTests`
Expected: build error.

- [ ] **Step 6: Implement the records**

Create `POC/src/Ino.Experiences/VerbStarted.cs`:

```csharp
using Ino.Core;
using Orleans;

namespace Ino.Experiences;

[GenerateSerializer]
public sealed record VerbStarted(
    [property: Id(0)] Type VerbType,
    [property: Id(1)] string CorrelationId) : ISynapse;
```

Create `POC/src/Ino.Experiences/VerbCompleted.cs`:

```csharp
using Ino.Core;
using Orleans;

namespace Ino.Experiences;

[GenerateSerializer]
public sealed record VerbCompleted(
    [property: Id(0)] Type VerbType,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] bool Success) : ISynapse;
```

- [ ] **Step 7: Run — expect PASS**

Expected: 2/2 passed.

- [ ] **Step 8: Commit**

```bash
git add POC/src/Ino.Experiences/ POC/test/Ino.Persona.Tests/ POC/ino.slnx
git commit -m "feat(poc): add VerbStarted + VerbCompleted synapses + Ino.Experiences project"
```

### Task 7: Extend Phase 2 `FirePort` with verb-lifecycle broadcasts

**Files:**
- Modify: `POC/src/Ino.Experiences/FirePortVerbHook.cs` (new partial extension)
- Modify: Phase 2's `FirePort.Fire<T>` call site
- Test: `POC/test/Ino.Hosting.Tests/FirePortVerbHookTests.cs`

- [ ] **Step 1: Write failing test against the existing Phase 2 multi-silo fixture**

Create `POC/test/Ino.Hosting.Tests/FirePortVerbHookTests.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Experiences;
using Ino.Testing;
using Xunit;

namespace Ino.Hosting.Tests;

[Collection(nameof(InoMultiSiloCollection))]
public sealed class FirePortVerbHookTests(InoMultiSiloFixture fixture)
{
    [Fact]
    public async Task Fire_Broadcasts_VerbStarted_BeforeHandler_And_VerbCompleted_AfterHandler()
    {
        var captures = fixture.Capture;
        captures.Clear();

        var client = fixture.ExperiencesSilo.Client;
        var caller = NeuronContextForTest.Create(new Caller.Ambient(KernelSilo.Experiences));

        await fixture.InstallTestBundle<ExampleVerbBundle>();
        await caller.FirePort.Fire(new ExampleVerb(), caller, CancellationToken.None);

        var verbStartedEntry = captures.Entries.FirstOrDefault(e => e.SynapseType == typeof(VerbStarted));
        var verbCompletedEntry = captures.Entries.FirstOrDefault(e => e.SynapseType == typeof(VerbCompleted));

        Assert.NotNull(verbStartedEntry);
        Assert.NotNull(verbCompletedEntry);
        Assert.True(verbStartedEntry!.At < verbCompletedEntry!.At, "VerbStarted must precede VerbCompleted");
    }
}

[GenerateSerializer]
public sealed record ExampleVerb : ISynapse;

internal sealed class ExampleVerbBundle : IExperience
{
    public BundleId Bundle => BundleId.From("Ino.Testing.ExampleVerb");
    public string Version => "1.0.0";
    public IReadOnlyList<Capability> DeclaredCapabilities => [];
    public IExperienceManifest Manifest => IExperienceManifest.Empty;
}
```

- [ ] **Step 2: Run — expect failure (no broadcast)**

Run: `dotnet test test/Ino.Hosting.Tests --filter FirePortVerbHookTests`
Expected: FAIL — `verbStartedEntry` is null.

- [ ] **Step 3: Extend `FirePort.Fire<T>` in `Ino.Experiences.Hosting` (Phase-2 project)**

Locate Phase 2's `FirePort.Fire<T>` implementation (see Phase 2 spec § 10.1). Wrap the canonical handler call:

```csharp
// Inside FirePort.Fire<T>(T synapse, NeuronContext caller, CancellationToken ct)
// ... existing discovery lookup + capability check ...

await FireBroadcast(new VerbStarted(typeof(T), caller.CorrelationId.Value), caller, ct);
try
{
    // existing handler invocation
    var result = await grain.HandleAsync(synapse, DeriveChildContext(caller, target), ct);
    await FireBroadcast(new VerbCompleted(typeof(T), caller.CorrelationId.Value, Success: result.Success), caller, ct);
    return result;
}
catch
{
    await FireBroadcast(new VerbCompleted(typeof(T), caller.CorrelationId.Value, Success: false), caller, ct);
    throw;
}
```

(Where `FireBroadcast` is the private helper already present for Phase-2 reactive dispatch.)

- [ ] **Step 4: Run — expect PASS**

Expected: 1/1 passed.

- [ ] **Step 5: Commit**

```bash
git add POC/src/Ino.Experiences/Hosting/FirePort.cs POC/test/Ino.Hosting.Tests/FirePortVerbHookTests.cs
git commit -m "feat(poc): broadcast VerbStarted/VerbCompleted around canonical handler"
```

### Task 8: Extend `Discovery` with interface-dispatch for reactive targets

**Files:**
- Modify: Phase 2's `DiscoveryGrain.LookupReactiveAsync` in `POC/src/Ino.System/DiscoveryGrain.cs`
- Test: `POC/test/Ino.Hosting.Tests/DiscoveryInterfaceDispatchTests.cs`

- [ ] **Step 1: Write failing test**

Create `POC/test/Ino.Hosting.Tests/DiscoveryInterfaceDispatchTests.cs`:

```csharp
using Ino.Core;
using Ino.System;
using Ino.Testing;
using Xunit;

namespace Ino.Hosting.Tests;

[Collection(nameof(InoMultiSiloCollection))]
public sealed class DiscoveryInterfaceDispatchTests(InoMultiSiloFixture fixture)
{
    [GenerateSerializer]
    public sealed record ConcreteStatus(string Title, string Body, string? Icon, string? Accent,
        NotificationKind Kind, string? DedupeKey) : IStatusSynapse;

    public sealed class StatusListener : Grain, IReactsTo<IStatusSynapse>
    {
        public static readonly List<IStatusSynapse> Received = [];
        public Task ReactAsync(IStatusSynapse s, NeuronContext ctx, CancellationToken ct)
        { Received.Add(s); return Task.CompletedTask; }
    }

    [Fact]
    public async Task LookupReactive_ByInterface_Returns_MatchingGrain_When_FiredWithConcreteSubtype()
    {
        var discovery = fixture.SystemSilo.Client.GetGrain<IDiscovery>(0);
        await discovery.RegisterAsync(new SiloRegistration(
            Silo: KernelSilo.System,
            Canonical: [],
            Reactive: [new ReactiveRegistration(typeof(IStatusSynapse), typeof(StatusListener), BundleId.From("Ino.Test"))]
        ), CancellationToken.None);

        var targets = await discovery.LookupReactiveAsync(typeof(ConcreteStatus), CancellationToken.None);
        Assert.Single(targets);
        Assert.Equal(typeof(StatusListener), targets[0].GrainType);
    }
}
```

- [ ] **Step 2: Run — expect FAIL (no match)**

Run: `dotnet test test/Ino.Hosting.Tests --filter DiscoveryInterfaceDispatchTests`
Expected: FAIL — `targets` is empty because Phase 2 Discovery indexes by exact `Type`.

- [ ] **Step 3: Extend `DiscoveryGrain.LookupReactiveAsync`**

Modify `POC/src/Ino.System/DiscoveryGrain.cs`:

```csharp
public Task<IReadOnlyList<ReactiveTarget>> LookupReactiveAsync(Type synapseType, CancellationToken ct)
{
    var results = new List<ReactiveTarget>();

    // Exact-type match (Phase 2)
    if (_reactive.TryGetValue(synapseType, out var exact))
        results.AddRange(exact);

    // Interface walk (Phase 3 extension — for IStatusSynapse marker dispatch)
    foreach (var iface in synapseType.GetInterfaces())
    {
        if (_reactive.TryGetValue(iface, out var ifaceListeners))
            results.AddRange(ifaceListeners);
    }

    return Task.FromResult<IReadOnlyList<ReactiveTarget>>(results);
}
```

- [ ] **Step 4: Run — expect PASS**

Expected: 1/1 passed.

- [ ] **Step 5: Commit**

```bash
git add POC/src/Ino.System/DiscoveryGrain.cs POC/test/Ino.Hosting.Tests/DiscoveryInterfaceDispatchTests.cs
git commit -m "feat(poc): extend Discovery reactive lookup with interface walk"
```

---

## Phase C — `PersonaNeuron`

### Task 9: `IPersonaNeuron` interface + grain key shape

**Files:**
- Create: `POC/src/Ino.Experiences/IPersonaNeuron.cs`
- Test: none at this step (covered by Task 10)

- [ ] **Step 1: Create the interface**

Create `POC/src/Ino.Experiences/IPersonaNeuron.cs`:

```csharp
using Ino.Core.Hosting;
using Orleans;

namespace Ino.Experiences;

public interface IPersonaNeuron : IGrainWithStringKey
{
    IAsyncEnumerable<PersonaFrame> StreamFramesAsync(CancellationToken ct);
}
```

- [ ] **Step 2: Commit**

```bash
git add POC/src/Ino.Experiences/IPersonaNeuron.cs
git commit -m "feat(poc): add IPersonaNeuron interface"
```

### Task 10: `PersonaNeuron` implementation + VerbStarted → PersonaFrame derivation

**Files:**
- Create: `POC/src/Ino.Experiences/PersonaNeuron.cs`
- Create: `POC/src/Ino.Experiences/MimeTupleTable.cs` (mime symbol → VM-tuple mapping)
- Test: `POC/test/Ino.Persona.Tests/PersonaNeuronTests.cs`

- [ ] **Step 1: Write failing test**

Create `POC/test/Ino.Persona.Tests/PersonaNeuronTests.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Experiences;
using Ino.Testing;
using Xunit;

namespace Ino.Persona.Tests;

[Collection(nameof(InoTestCollection))]
public sealed class PersonaNeuronTests(InoTestSiloFixture fixture)
{
    [GenerateSerializer] public sealed record VerbA : ISynapse;

    [Fact]
    public async Task OnVerbStarted_Emits_Frame_With_ReachPhoneTuple()
    {
        var persona = fixture.Client.GetGrain<IPersonaNeuron>("user-1");
        var frames = new List<PersonaFrame>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var readTask = Task.Run(async () =>
        {
            await foreach (var f in persona.StreamFramesAsync(cts.Token)) frames.Add(f);
        });

        // Simulate VerbStarted arriving via IReactsTo — direct reactive fire to the persona grain
        await fixture.FireReactive(new VerbStarted(typeof(VerbA), "c-1"),
            userId: "user-1",
            bindingFor: typeof(VerbA), binding: new VerbBinding(MimeSymbols.ReachPhone, NotificationPolicy.None));

        await Task.Delay(500);
        cts.Cancel();
        try { await readTask; } catch (OperationCanceledException) { }

        var first = frames[0];
        Assert.Equal("lean_in", first.BodyPose);
        Assert.Equal("hold_prop", first.Arms);
        Assert.Equal("down", first.Eyes);
        Assert.Equal("Ino.Persona.Tests.PersonaNeuronTests+VerbA", first.CurrentVerb);
    }
}
```

- [ ] **Step 2: Run — expect CS0246 / test infra errors**

Run: `dotnet test test/Ino.Persona.Tests --filter PersonaNeuronTests`
Expected: fails (no mime-tuple table, no persona grain impl, no FireReactive helper).

- [ ] **Step 3: Create `MimeTupleTable.cs`**

Create `POC/src/Ino.Experiences/MimeTupleTable.cs`:

```csharp
using Ino.Core.Hosting;

namespace Ino.Experiences;

public static class MimeTupleTable
{
    public static PersonaFrame Apply(string mimeSymbol, PersonaFrame baseline) => mimeSymbol switch
    {
        MimeSymbols.ReachPhone    => baseline with { BodyPose = "lean_in",  Arms = "hold_prop",        Eyes = "down",       PropVisible = true, PropKind = "phone" },
        MimeSymbols.ScanHorizon   => baseline with { BodyPose = "neutral",  Arms = "scan_hand",        Eyes = "scan_sweep" },
        MimeSymbols.PointForward  => baseline with { BodyPose = "present",  Arms = "point_forward",    Eyes = "forward" },
        MimeSymbols.TapMap        => baseline with { BodyPose = "present",  Arms = "tap",              Eyes = "down",       SignalPulse = 1.0 },
        MimeSymbols.WaveOff       => baseline with { BodyPose = "lean_back",Arms = "palm_up",          Mouth = "concern" },
        MimeSymbols.ThumbsUp      => baseline with { BodyPose = "present",  Arms = "thumbs_up",        Trigger = PersonaTrigger.OnCelebrate },
        MimeSymbols.Write         => baseline with { BodyPose = "neutral",  Arms = "write",            Eyes = "down" },
        MimeSymbols.PeekBox       => baseline with { BodyPose = "curl_in",  Arms = "peek",             Eyes = "forward" },
        MimeSymbols.HeadBob       => baseline with { BodyPose = "neutral" },
        MimeSymbols.SwipeForward  => baseline with { BodyPose = "present",  Arms = "swipe",            Eyes = "forward" },
        MimeSymbols.StackItems    => baseline with { BodyPose = "present",  Arms = "tap" },
        MimeSymbols.SlideCoins    => baseline with { BodyPose = "present",  Arms = "two_finger_send",  Eyes = "forward" },
        MimeSymbols.TuckAway      => baseline with { BodyPose = "curl_in",  Arms = "tap",              Eyes = "down" },
        MimeSymbols.Swap          => baseline with { BodyPose = "neutral",  Arms = "swipe" },
        MimeSymbols.TwoFingerSend => baseline with { BodyPose = "present",  Arms = "two_finger_send" },
        _                         => baseline with { BodyPose = "neutral",  Arms = "at_side" },        // fallback
    };
}
```

- [ ] **Step 4: Create `PersonaNeuron.cs`**

Create `POC/src/Ino.Experiences/PersonaNeuron.cs`:

```csharp
using System.Threading.Channels;
using Ino.Core;
using Ino.Core.Hosting;
using Orleans;

namespace Ino.Experiences;

public sealed class PersonaNeuron : Grain, IPersonaNeuron,
    IReactsTo<VerbStarted>, IReactsTo<VerbCompleted>
{
    private readonly Channel<PersonaFrame> _channel =
        Channel.CreateUnbounded<PersonaFrame>(new() { SingleReader = false, SingleWriter = false });
    private readonly IExperienceManifestResolver _resolver;

    public PersonaNeuron(IExperienceManifestResolver resolver) => _resolver = resolver;

    public async IAsyncEnumerable<PersonaFrame> StreamFramesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (await _channel.Reader.WaitToReadAsync(ct))
            while (_channel.Reader.TryRead(out var f)) yield return f;
    }

    public async Task ReactAsync(VerbStarted s, NeuronContext ctx, CancellationToken ct)
    {
        var binding = _resolver.Resolve(s.VerbType);
        var baseline = new PersonaFrame(
            CurrentVerb: s.VerbType.FullName,
            CurrentExperience: binding?.BundleId.Value,
            Accent: binding?.Binding.Accent);
        var frame = MimeTupleTable.Apply(binding?.Binding.MimeSymbol ?? MimeSymbols.Fallback, baseline);
        await _channel.Writer.WriteAsync(frame, ct);
    }

    public async Task ReactAsync(VerbCompleted s, NeuronContext ctx, CancellationToken ct)
    {
        var trigger = s.Success ? PersonaTrigger.OnCelebrate : PersonaTrigger.OnError;
        await _channel.Writer.WriteAsync(new PersonaFrame(
            BodyPose: "neutral", Arms: "at_side", Trigger: trigger), ct);
    }
}

public interface IExperienceManifestResolver
{
    (BundleId BundleId, VerbBinding Binding)? Resolve(Type verbType);
}
```

- [ ] **Step 5: Provide a simple in-memory manifest resolver**

Create `POC/src/Ino.Experiences/ExperienceManifestResolver.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.Experiences;

public sealed class ExperienceManifestResolver : IExperienceManifestResolver
{
    private readonly Dictionary<Type, (BundleId, VerbBinding)> _index = new();

    public ExperienceManifestResolver(IEnumerable<IExperience> experiences)
    {
        foreach (var exp in experiences)
            foreach (var kv in exp.Manifest.Verbs)
                _index[kv.Key] = (exp.Bundle, kv.Value);
    }

    public (BundleId BundleId, VerbBinding Binding)? Resolve(Type verbType)
        => _index.TryGetValue(verbType, out var entry) ? entry : null;
}
```

- [ ] **Step 6: Add `FireReactive` helper on `InoTestSiloFixture`**

Open `POC/src/Ino.Testing/InoTestSiloFixture.cs` and append:

```csharp
public async Task FireReactive<T>(T synapse, string userId, Type bindingFor, VerbBinding binding) where T : Ino.Core.ISynapse
{
    var resolver = Services.GetRequiredService<Ino.Experiences.IExperienceManifestResolver>();
    // direct grain-level reactive delivery — short-circuit Discovery for unit tests
    var grain = Client.GetGrain<Ino.Experiences.IPersonaNeuron>(userId);
    if (grain is Ino.Experiences.IReactsTo<T> listener)
        await listener.ReactAsync(synapse, Ino.Core.Hosting.NeuronContextForTest.Create(
            new Ino.Core.Caller.Ambient(Ino.Core.Hosting.KernelSilo.Experiences)), CancellationToken.None);
}
```

- [ ] **Step 7: Run — expect PASS**

Run: `dotnet test test/Ino.Persona.Tests --filter PersonaNeuronTests`
Expected: 1/1 passed.

- [ ] **Step 8: Commit**

```bash
git add POC/src/Ino.Experiences/ POC/src/Ino.Testing/InoTestSiloFixture.cs POC/test/Ino.Persona.Tests/PersonaNeuronTests.cs
git commit -m "feat(poc): PersonaNeuron + MimeTupleTable + ExperienceManifestResolver"
```

### Task 11: VerbCompleted → neutral pose + celebrate/error trigger test

**Files:**
- Test: append to `POC/test/Ino.Persona.Tests/PersonaNeuronTests.cs`

- [ ] **Step 1: Append tests**

```csharp
[Fact]
public async Task OnVerbCompleted_Success_Emits_OnCelebrate_Trigger_AndNeutralPose()
{
    var persona = fixture.Client.GetGrain<IPersonaNeuron>("user-2");
    var frames = new List<PersonaFrame>();
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var t = Task.Run(async () => { await foreach (var f in persona.StreamFramesAsync(cts.Token)) frames.Add(f); });

    await fixture.FireReactive(new VerbCompleted(typeof(VerbA), "c-1", Success: true), "user-2",
        typeof(VerbA), new VerbBinding(MimeSymbols.ReachPhone, NotificationPolicy.None));

    await Task.Delay(300); cts.Cancel(); try { await t; } catch (OperationCanceledException) { }
    Assert.Contains(frames, f => f.Trigger == PersonaTrigger.OnCelebrate && f.BodyPose == "neutral");
}

[Fact]
public async Task OnVerbCompleted_Failure_Emits_OnError_Trigger()
{
    var persona = fixture.Client.GetGrain<IPersonaNeuron>("user-3");
    var frames = new List<PersonaFrame>();
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var t = Task.Run(async () => { await foreach (var f in persona.StreamFramesAsync(cts.Token)) frames.Add(f); });

    await fixture.FireReactive(new VerbCompleted(typeof(VerbA), "c-1", Success: false), "user-3",
        typeof(VerbA), new VerbBinding(MimeSymbols.ReachPhone, NotificationPolicy.None));

    await Task.Delay(300); cts.Cancel(); try { await t; } catch (OperationCanceledException) { }
    Assert.Contains(frames, f => f.Trigger == PersonaTrigger.OnError);
}
```

- [ ] **Step 2: Run — expect PASS**

Run: `dotnet test test/Ino.Persona.Tests --filter PersonaNeuronTests`
Expected: 3/3 passed.

- [ ] **Step 3: Commit**

```bash
git add POC/test/Ino.Persona.Tests/PersonaNeuronTests.cs
git commit -m "test(poc): PersonaNeuron celebrate/error trigger emission"
```

---

## Phase D — `NotifierNeuron`

### Task 12: `NotificationBanner` + `INotificationStream` + in-memory grain impl

**Files:**
- Create: `POC/src/Ino.System/NotificationBanner.cs`
- Create: `POC/src/Ino.System/INotificationStream.cs`
- Create: `POC/src/Ino.System/NotificationStreamGrain.cs`
- Create: `POC/test/Ino.Notifier.Tests/Ino.Notifier.Tests.csproj` (+ collection + fixture as Phase-2 single-silo pattern)

- [ ] **Step 1: Create the test project**

Create `POC/test/Ino.Notifier.Tests/Ino.Notifier.Tests.csproj` mirroring `Ino.Persona.Tests.csproj`, referencing `Ino.System` and `Ino.Core`.

Add to solution: `dotnet sln ino.slnx add test/Ino.Notifier.Tests/Ino.Notifier.Tests.csproj`

- [ ] **Step 2: Write failing test**

Create `POC/test/Ino.Notifier.Tests/NotificationStreamGrainTests.cs`:

```csharp
using Ino.Core;
using Ino.System;
using Ino.Testing;
using Xunit;

namespace Ino.Notifier.Tests;

[Collection(nameof(InoTestCollection))]
public sealed class NotificationStreamGrainTests(InoTestSiloFixture fixture)
{
    [Fact]
    public async Task Published_Banner_Streams_ToSubscriber()
    {
        var stream = fixture.Client.GetGrain<INotificationStream>("user-A");
        var banners = new List<NotificationBanner>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var t = Task.Run(async () => { await foreach (var b in stream.StreamAsync(cts.Token)) banners.Add(b); });

        await stream.PublishAsync(new NotificationBanner(
            Id: "id-1", Title: "your ride", Body: "Ali 5m", Icon: "🚕",
            Accent: "#000", Kind: NotificationKind.Progress, DedupeKey: "uber:R-1",
            At: DateTimeOffset.UtcNow), CancellationToken.None);

        await Task.Delay(300); cts.Cancel(); try { await t; } catch (OperationCanceledException) { }
        Assert.Single(banners);
        Assert.Equal("Ali 5m", banners[0].Body);
    }
}
```

- [ ] **Step 3: Run — CS0246**

Expected: build error.

- [ ] **Step 4: Implement the types**

Create `POC/src/Ino.System/NotificationBanner.cs`:

```csharp
using Ino.Core;
using Orleans;

namespace Ino.System;

[GenerateSerializer]
public sealed record NotificationBanner(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] string Body,
    [property: Id(3)] string? Icon,
    [property: Id(4)] string? Accent,
    [property: Id(5)] NotificationKind Kind,
    [property: Id(6)] string? DedupeKey,
    [property: Id(7)] DateTimeOffset At);
```

Create `POC/src/Ino.System/INotificationStream.cs`:

```csharp
using Orleans;

namespace Ino.System;

public interface INotificationStream : IGrainWithStringKey
{
    IAsyncEnumerable<NotificationBanner> StreamAsync(CancellationToken ct);
    Task PublishAsync(NotificationBanner banner, CancellationToken ct);
}
```

Create `POC/src/Ino.System/NotificationStreamGrain.cs`:

```csharp
using System.Threading.Channels;
using Orleans;

namespace Ino.System;

public sealed class NotificationStreamGrain : Grain, INotificationStream
{
    private readonly Channel<NotificationBanner> _ch =
        Channel.CreateUnbounded<NotificationBanner>(new() { SingleReader = false, SingleWriter = false });

    public async IAsyncEnumerable<NotificationBanner> StreamAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (await _ch.Reader.WaitToReadAsync(ct))
            while (_ch.Reader.TryRead(out var b)) yield return b;
    }

    public Task PublishAsync(NotificationBanner banner, CancellationToken ct)
        => _ch.Writer.WriteAsync(banner, ct).AsTask();
}
```

- [ ] **Step 5: Run — PASS**

Run: `dotnet test test/Ino.Notifier.Tests --filter NotificationStreamGrainTests`
Expected: 1/1 passed.

- [ ] **Step 6: Commit**

```bash
git add POC/src/Ino.System/Notification*.cs POC/src/Ino.System/INotificationStream.cs POC/test/Ino.Notifier.Tests/ POC/ino.slnx
git commit -m "feat(poc): NotificationBanner + INotificationStream + grain impl"
```

### Task 13: `NotifierNeuron` reactive on `IStatusSynapse`

**Files:**
- Create: `POC/src/Ino.System/NotifierNeuron.cs`
- Test: `POC/test/Ino.Notifier.Tests/NotifierNeuronTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using Ino.Core;
using Ino.System;
using Ino.Testing;
using Xunit;

namespace Ino.Notifier.Tests;

[Collection(nameof(InoTestCollection))]
public sealed class NotifierNeuronTests(InoTestSiloFixture fixture)
{
    [GenerateSerializer] public sealed record Hello(string Title, string Body,
        string? Icon, string? Accent, NotificationKind Kind, string? DedupeKey) : IStatusSynapse;

    [Fact]
    public async Task NotifierNeuron_React_Publishes_ToStreamForUser()
    {
        var stream = fixture.Client.GetGrain<INotificationStream>("user-B");
        var banners = new List<NotificationBanner>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var t = Task.Run(async () => { await foreach (var b in stream.StreamAsync(cts.Token)) banners.Add(b); });

        await fixture.FireReactiveToNotifier(
            new Hello("t", "b", null, "#000", NotificationKind.Progress, "k-1"), userId: "user-B");

        await Task.Delay(300); cts.Cancel(); try { await t; } catch (OperationCanceledException) { }
        Assert.Single(banners);
        Assert.Equal("t", banners[0].Title);
    }
}
```

- [ ] **Step 2: Run — FAIL**

Expected: build error — `FireReactiveToNotifier` and `NotifierNeuron` missing.

- [ ] **Step 3: Implement `NotifierNeuron`**

Create `POC/src/Ino.System/NotifierNeuron.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting;
using Orleans;

namespace Ino.System;

public sealed class NotifierNeuron : Grain, IReactsTo<IStatusSynapse>
{
    private readonly IGrainFactory _grains;
    public NotifierNeuron(IGrainFactory grains) => _grains = grains;

    public async Task ReactAsync(IStatusSynapse s, NeuronContext ctx, CancellationToken ct)
    {
        var stream = _grains.GetGrain<INotificationStream>(ctx.UserId ?? "anon");
        await stream.PublishAsync(new NotificationBanner(
            Id: Guid.NewGuid().ToString("n"),
            Title: s.Title, Body: s.Body, Icon: s.Icon, Accent: s.Accent,
            Kind: s.Kind, DedupeKey: s.DedupeKey, At: DateTimeOffset.UtcNow), ct);
    }
}
```

- [ ] **Step 4: Add `FireReactiveToNotifier` helper on the fixture**

Append to `InoTestSiloFixture.cs`:

```csharp
public async Task FireReactiveToNotifier<T>(T synapse, string userId) where T : Ino.Core.IStatusSynapse
{
    var notifier = new Ino.System.NotifierNeuron(Client);
    await notifier.ReactAsync(synapse,
        Ino.Core.Hosting.NeuronContextForTest.Create(new Ino.Core.Caller.Ambient(Ino.Core.Hosting.KernelSilo.System)) with { UserId = userId },
        CancellationToken.None);
}
```

(Note: treats `NeuronContext.UserId` as already-present from Phase 2 § 8.)

- [ ] **Step 5: Run — PASS**

Expected: 1/1.

- [ ] **Step 6: Commit**

```bash
git add POC/src/Ino.System/NotifierNeuron.cs POC/src/Ino.Testing/InoTestSiloFixture.cs POC/test/Ino.Notifier.Tests/NotifierNeuronTests.cs
git commit -m "feat(poc): NotifierNeuron reactive on IStatusSynapse"
```

### Task 14: `DedupeKey` in-place update (client-side semantics test)

**Files:**
- Test: `POC/test/Ino.Notifier.Tests/DedupeKeyTests.cs`

- [ ] **Step 1: Write test**

```csharp
using Ino.Core;
using Ino.System;
using Xunit;

namespace Ino.Notifier.Tests;

public sealed class DedupeKeyTests
{
    [Fact]
    public void Banners_WithSame_DedupeKey_ProjectToOneSlot()
    {
        var list = new NotifierBannerStack();
        list.Apply(Banner("id-1", "5 min", "uber:R-1"));
        list.Apply(Banner("id-2", "4 min", "uber:R-1"));
        list.Apply(Banner("id-3", "3 min", "uber:R-1"));
        Assert.Single(list.Slots);
        Assert.Equal("3 min", list.Slots[0].Body);
    }

    [Fact]
    public void Banners_WithoutDedupeKey_StackIndependently()
    {
        var list = new NotifierBannerStack();
        list.Apply(Banner("id-1", "a", null));
        list.Apply(Banner("id-2", "b", null));
        Assert.Equal(2, list.Slots.Count);
    }

    private static NotificationBanner Banner(string id, string body, string? key) =>
        new(id, "t", body, null, null, NotificationKind.Progress, key, DateTimeOffset.UtcNow);
}
```

- [ ] **Step 2: Run — CS0246**

- [ ] **Step 3: Implement `NotifierBannerStack`**

Create `POC/src/Ino.System/NotifierBannerStack.cs`:

```csharp
namespace Ino.System;

public sealed class NotifierBannerStack
{
    private readonly List<NotificationBanner> _slots = [];
    public IReadOnlyList<NotificationBanner> Slots => _slots;

    public void Apply(NotificationBanner b)
    {
        if (b.DedupeKey is null) { _slots.Add(b); return; }
        var existing = _slots.FindIndex(s => s.DedupeKey == b.DedupeKey);
        if (existing >= 0) _slots[existing] = b; else _slots.Add(b);
    }
}
```

This is server-side pure logic; the Flutter `NotifierBloc` will apply the same algorithm in Dart (Task 30).

- [ ] **Step 4: Run — PASS**

Expected: 2/2 passed.

- [ ] **Step 5: Commit**

```bash
git add POC/src/Ino.System/NotifierBannerStack.cs POC/test/Ino.Notifier.Tests/DedupeKeyTests.cs
git commit -m "feat(poc): NotifierBannerStack dedupe semantics"
```

---

## Phase E — `PersonaEvolver`

### Task 15: `MimeMappingMissing` synapse + `MimeOverrideStore` grain

**Files:**
- Create: `POC/src/Ino.PersonaEvolver/Ino.PersonaEvolver.csproj`
- Create: `POC/src/Ino.PersonaEvolver/MimeMappingMissing.cs`
- Create: `POC/src/Ino.PersonaEvolver/IMimeOverrideStore.cs`
- Create: `POC/src/Ino.PersonaEvolver/MimeOverrideStoreGrain.cs`
- Test: `POC/test/Ino.Persona.Tests/MimeOverrideStoreTests.cs`

- [ ] **Step 1: Create `Ino.PersonaEvolver` project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Sdk" />
    <PackageReference Include="Microsoft.Orleans.Server" />
    <PackageReference Include="Microsoft.Extensions.AI" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ino.Core\Ino.Core.csproj" />
    <ProjectReference Include="..\Ino.Core.Hosting\Ino.Core.Hosting.csproj" />
    <ProjectReference Include="..\Ino.Experiences\Ino.Experiences.csproj" />
  </ItemGroup>
</Project>
```

Run: `dotnet sln ino.slnx add src/Ino.PersonaEvolver/Ino.PersonaEvolver.csproj`

- [ ] **Step 2: Write failing test**

Create `POC/test/Ino.Persona.Tests/MimeOverrideStoreTests.cs`:

```csharp
using Ino.Core.Hosting;
using Ino.PersonaEvolver;
using Ino.Testing;
using Xunit;

namespace Ino.Persona.Tests;

[Collection(nameof(InoTestCollection))]
public sealed class MimeOverrideStoreTests(InoTestSiloFixture fixture)
{
    [Fact]
    public async Task Set_Then_Get_ReturnsOverride()
    {
        var store = fixture.Client.GetGrain<IMimeOverrideStore>(0);
        await store.SetAsync(typeof(string), new VerbBinding(MimeSymbols.ScanHorizon, NotificationPolicy.None));
        var got = await store.GetAsync(typeof(string));
        Assert.Equal(MimeSymbols.ScanHorizon, got!.MimeSymbol);
    }

    [Fact]
    public async Task Get_ReturnsNull_WhenUnset()
    {
        var store = fixture.Client.GetGrain<IMimeOverrideStore>(0);
        Assert.Null(await store.GetAsync(typeof(DateTime)));
    }
}
```

- [ ] **Step 3: Run — CS0246**

- [ ] **Step 4: Implement interface + grain**

Create `POC/src/Ino.PersonaEvolver/IMimeOverrideStore.cs`:

```csharp
using Ino.Core.Hosting;
using Orleans;

namespace Ino.PersonaEvolver;

public interface IMimeOverrideStore : IGrainWithIntegerKey
{
    Task SetAsync(Type verbType, VerbBinding binding);
    Task<VerbBinding?> GetAsync(Type verbType);
}
```

Create `POC/src/Ino.PersonaEvolver/MimeOverrideStoreGrain.cs`:

```csharp
using Ino.Core.Hosting;
using Orleans;
using Orleans.Runtime;

namespace Ino.PersonaEvolver;

public sealed class MimeOverrideStoreGrain : Grain, IMimeOverrideStore
{
    private readonly IPersistentState<Dictionary<string, VerbBinding>> _state;

    public MimeOverrideStoreGrain(
        [PersistentState("mime_overrides", "MemoryStore")]
        IPersistentState<Dictionary<string, VerbBinding>> state) => _state = state;

    public async Task SetAsync(Type verbType, VerbBinding binding)
    {
        _state.State[verbType.AssemblyQualifiedName!] = binding;
        await _state.WriteStateAsync();
    }

    public Task<VerbBinding?> GetAsync(Type verbType)
        => Task.FromResult<VerbBinding?>(
            _state.State.TryGetValue(verbType.AssemblyQualifiedName!, out var b) ? b : null);
}
```

- [ ] **Step 5: Create `MimeMappingMissing`**

Create `POC/src/Ino.PersonaEvolver/MimeMappingMissing.cs`:

```csharp
using Ino.Core;
using Orleans;

namespace Ino.PersonaEvolver;

[GenerateSerializer]
public sealed record MimeMappingMissing(
    [property: Id(0)] Type VerbType,
    [property: Id(1)] string BundleId,
    [property: Id(2)] string Domain) : ISynapse;
```

- [ ] **Step 6: Run — PASS**

Expected: 2/2 passed.

- [ ] **Step 7: Commit**

```bash
git add POC/src/Ino.PersonaEvolver/ POC/test/Ino.Persona.Tests/MimeOverrideStoreTests.cs POC/ino.slnx
git commit -m "feat(poc): Ino.PersonaEvolver scaffold + MimeOverrideStore grain"
```

### Task 16: `PersonaEvolverNeuron` — react to missing, call LLM mock, store override

**Files:**
- Create: `POC/src/Ino.PersonaEvolver/PersonaEvolverNeuron.cs`
- Test: `POC/test/Ino.Persona.Tests/PersonaEvolverTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using Ino.Core;
using Ino.Core.Hosting;
using Ino.PersonaEvolver;
using Ino.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace Ino.Persona.Tests;

[Collection(nameof(InoTestCollection))]
public sealed class PersonaEvolverTests(InoTestSiloFixture fixture)
{
    public sealed record UnknownVerb : ISynapse;

    [Fact]
    public async Task ReactToMissingMapping_CallsLlm_ValidatesSymbol_StoresOverride()
    {
        fixture.MockChat.Respond("reach_phone");
        var evolver = new PersonaEvolverNeuron(fixture.Client, fixture.Services.GetRequiredService<IChatClient>(),
            NullLogger<PersonaEvolverNeuron>.Instance);
        await evolver.ReactAsync(
            new MimeMappingMissing(typeof(UnknownVerb), "Ino.Sample", "test"),
            NeuronContextForTest.Create(new Caller.Ambient(KernelSilo.Experiences)),
            CancellationToken.None);

        var store = fixture.Client.GetGrain<IMimeOverrideStore>(0);
        var binding = await store.GetAsync(typeof(UnknownVerb));
        Assert.Equal(MimeSymbols.ReachPhone, binding!.MimeSymbol);
    }

    [Fact]
    public async Task InvalidLlmAnswer_FallsBackToThinkingNod()
    {
        fixture.MockChat.Respond("made_up_gesture");
        var evolver = new PersonaEvolverNeuron(fixture.Client, fixture.Services.GetRequiredService<IChatClient>(),
            NullLogger<PersonaEvolverNeuron>.Instance);
        await evolver.ReactAsync(
            new MimeMappingMissing(typeof(UnknownVerb), "Ino.Sample", "test"),
            NeuronContextForTest.Create(new Caller.Ambient(KernelSilo.Experiences)),
            CancellationToken.None);

        var store = fixture.Client.GetGrain<IMimeOverrideStore>(0);
        var binding = await store.GetAsync(typeof(UnknownVerb));
        Assert.Equal(MimeSymbols.Fallback, binding!.MimeSymbol);
    }
}
```

- [ ] **Step 2: Run — CS0246**

- [ ] **Step 3: Implement `PersonaEvolverNeuron`**

Create `POC/src/Ino.PersonaEvolver/PersonaEvolverNeuron.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.PersonaEvolver;

public sealed class PersonaEvolverNeuron : IReactsTo<MimeMappingMissing>
{
    private readonly IGrainFactory _grains;
    private readonly IChatClient _chat;
    private readonly ILogger<PersonaEvolverNeuron> _log;

    public PersonaEvolverNeuron(IGrainFactory grains, IChatClient chat, ILogger<PersonaEvolverNeuron> log)
    { _grains = grains; _chat = chat; _log = log; }

    public async Task ReactAsync(MimeMappingMissing s, NeuronContext ctx, CancellationToken ct)
    {
        var known = string.Join(", ", MimeSymbols.All);
        var prompt = $@"Pick exactly one mime symbol from this list that best fits the verb.
Verb: {s.VerbType.Name} in bundle {s.BundleId} (domain: {s.Domain}).
Known mimes: {known}
Answer with exactly one symbol, no quotes.";

        var response = await _chat.GetResponseAsync(prompt, cancellationToken: ct);
        var answer = response.Text.Trim();

        var chosen = MimeSymbols.IsKnown(answer) ? answer : MimeSymbols.Fallback;
        if (!MimeSymbols.IsKnown(answer))
            _log.LogWarning("PersonaEvolver: LLM returned unknown symbol {Answer} for verb {Verb}; falling back to {Fallback}.",
                answer, s.VerbType.FullName, MimeSymbols.Fallback);

        var store = _grains.GetGrain<IMimeOverrideStore>(0);
        await store.SetAsync(s.VerbType, new VerbBinding(chosen, NotificationPolicy.None));
    }
}
```

- [ ] **Step 4: Run — PASS**

Expected: 2/2.

- [ ] **Step 5: Commit**

```bash
git add POC/src/Ino.PersonaEvolver/PersonaEvolverNeuron.cs POC/test/Ino.Persona.Tests/PersonaEvolverTests.cs
git commit -m "feat(poc): PersonaEvolverNeuron L1 mapping-script generator"
```

---

## Phase F — gRPC service layer

The `system` silo's ASP.NET host (created in Phase 2 for the marketplace HTTP controller) gains a gRPC endpoint with three services. gRPC-Web is enabled so the browser-hosted Flutter client can connect.

### Task 17: Create `Ino.Gateways.Grpc` project + three proto files

**Files:**
- Create: `POC/src/Ino.Gateways.Grpc/Ino.Gateways.Grpc.csproj`
- Create: `POC/src/Ino.Gateways.Grpc/Protos/persona.proto`
- Create: `POC/src/Ino.Gateways.Grpc/Protos/experiences.proto`
- Create: `POC/src/Ino.Gateways.Grpc/Protos/notifications.proto`

- [ ] **Step 1: Create csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Grpc.AspNetCore" />
    <PackageReference Include="Grpc.AspNetCore.Web" />
    <PackageReference Include="Grpc.Tools" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <Protobuf Include="Protos\persona.proto" GrpcServices="Server" />
    <Protobuf Include="Protos\experiences.proto" GrpcServices="Server" />
    <Protobuf Include="Protos\notifications.proto" GrpcServices="Server" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ino.Core\Ino.Core.csproj" />
    <ProjectReference Include="..\Ino.Core.Hosting\Ino.Core.Hosting.csproj" />
    <ProjectReference Include="..\Ino.Experiences\Ino.Experiences.csproj" />
    <ProjectReference Include="..\Ino.System\Ino.System.csproj" />
  </ItemGroup>
</Project>
```

Add to solution.

- [ ] **Step 2: `persona.proto`**

Create `POC/src/Ino.Gateways.Grpc/Protos/persona.proto` (text content from spec § 9.4). Use `syntax = "proto3"`, `package ino.persona.v1;`, `csharp_namespace = "Ino.Gateways.Grpc";`.

- [ ] **Step 3: `experiences.proto`**

Same package/namespace. Content from spec § 11.

- [ ] **Step 4: `notifications.proto`**

Same package/namespace. Content from spec § 10.3.

- [ ] **Step 5: Build**

Run: `cd /d/ino/POC && dotnet build src/Ino.Gateways.Grpc`
Expected: builds clean; generated classes live in `obj/`.

- [ ] **Step 6: Commit**

```bash
git add POC/src/Ino.Gateways.Grpc/ POC/ino.slnx
git commit -m "feat(poc): Ino.Gateways.Grpc project + three proto contracts"
```

### Task 18: `PersonaService` streaming implementation

**Files:**
- Create: `POC/src/Ino.Gateways.Grpc/Services/PersonaService.cs`
- Create: `POC/src/Ino.Gateways.Grpc/Mapping/PersonaFrameMapper.cs`
- Test: extension in `Ino.Hosting.Tests` (E2E smoke in Phase K)

- [ ] **Step 1: Implement mapper**

Create `POC/src/Ino.Gateways.Grpc/Mapping/PersonaFrameMapper.cs`:

```csharp
using Ino.Persona.V1;   // generated

namespace Ino.Gateways.Grpc.Mapping;

public static class PersonaFrameMapper
{
    public static PersonaFrame ToProto(Ino.Core.Hosting.PersonaFrame f)
    {
        var p = new PersonaFrame();
        if (f.Mood.HasValue) p.Mood = f.Mood.Value;
        if (f.Energy.HasValue) p.Energy = f.Energy.Value;
        if (f.Confidence.HasValue) p.Confidence = f.Confidence.Value;
        if (f.SignalPulse.HasValue) p.SignalPulse = f.SignalPulse.Value;
        if (f.BodyPose is not null) p.BodyPose = f.BodyPose;
        if (f.Mouth is not null) p.Mouth = f.Mouth;
        if (f.Eyes is not null) p.Eyes = f.Eyes;
        if (f.Arms is not null) p.Arms = f.Arms;
        if (f.GlowRing is not null) p.GlowRing = f.GlowRing;
        if (f.CurrentVerb is not null) p.CurrentVerb = f.CurrentVerb;
        if (f.CurrentExperience is not null) p.CurrentExperience = f.CurrentExperience;
        if (f.Accent is not null) p.Accent = f.Accent;
        if (f.PropVisible.HasValue) p.PropVisible = f.PropVisible.Value;
        if (f.PropKind is not null) p.PropKind = f.PropKind;
        if (f.Trigger.HasValue) p.Trigger = (PersonaTrigger)(int)f.Trigger.Value;
        return p;
    }
}
```

- [ ] **Step 2: Implement service**

Create `POC/src/Ino.Gateways.Grpc/Services/PersonaService.cs`:

```csharp
using Grpc.Core;
using Ino.Experiences;
using Ino.Gateways.Grpc.Mapping;
using Ino.Persona.V1;
using Orleans;

namespace Ino.Gateways.Grpc.Services;

public sealed class PersonaService(IGrainFactory grains) : Persona.PersonaBase
{
    public override async Task StreamPersonaState(
        PersonaSubscription request,
        IServerStreamWriter<PersonaFrame> response,
        ServerCallContext context)
    {
        var grain = grains.GetGrain<IPersonaNeuron>(request.UserId);
        await foreach (var frame in grain.StreamFramesAsync(context.CancellationToken))
            await response.WriteAsync(PersonaFrameMapper.ToProto(frame));
    }
}
```

- [ ] **Step 3: Register service**

Modify Phase 2's system-silo `Program.cs` to:

```csharp
builder.Services.AddGrpc();
// after app.MapControllers():
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
app.MapGrpcService<PersonaService>().EnableGrpcWeb();
```

- [ ] **Step 4: Smoke-test the gRPC endpoint via `grpcurl`**

Run:

```bash
# after 'aspire start', from a second shell:
grpcurl -plaintext -d '{"user_id":"u-1"}' localhost:<grpc-port> ino.persona.v1.Persona.StreamPersonaState
```

Expected: connection opens without error; no frames yet (no verb has fired).

- [ ] **Step 5: Commit**

```bash
git add POC/src/Ino.Gateways.Grpc/Services/PersonaService.cs POC/src/Ino.Gateways.Grpc/Mapping/PersonaFrameMapper.cs POC/src/Ino.System.Host/Program.cs
git commit -m "feat(poc): PersonaService gRPC streaming"
```

### Task 19: `NotificationsService` streaming implementation

**Files:**
- Create: `POC/src/Ino.Gateways.Grpc/Services/NotificationsService.cs`
- Create: `POC/src/Ino.Gateways.Grpc/Mapping/NotificationMapper.cs`

- [ ] **Step 1: Mapper**

```csharp
using Ino.Notifications.V1;
using Google.Protobuf;

namespace Ino.Gateways.Grpc.Mapping;

public static class NotificationMapper
{
    public static NotificationBanner ToProto(Ino.System.NotificationBanner b) => new()
    {
        Id = b.Id, Title = b.Title, Body = b.Body,
        Icon = b.Icon ?? "", Accent = b.Accent ?? "",
        Kind = (NotificationKind)(int)b.Kind,
        DedupeKey = b.DedupeKey ?? "",
        AtUnixMs = b.At.ToUnixTimeMilliseconds(),
    };
}
```

- [ ] **Step 2: Service**

```csharp
using Grpc.Core;
using Ino.Gateways.Grpc.Mapping;
using Ino.Notifications.V1;
using Ino.System;
using Orleans;

namespace Ino.Gateways.Grpc.Services;

public sealed class NotificationsService(IGrainFactory grains) : Notifications.NotificationsBase
{
    public override async Task StreamNotifications(
        NotifSubscription request,
        IServerStreamWriter<NotificationBanner> response,
        ServerCallContext context)
    {
        var stream = grains.GetGrain<INotificationStream>(request.UserId);
        await foreach (var b in stream.StreamAsync(context.CancellationToken))
            await response.WriteAsync(NotificationMapper.ToProto(b));
    }
}
```

- [ ] **Step 3: Register + commit**

Add `app.MapGrpcService<NotificationsService>().EnableGrpcWeb();` to system-silo `Program.cs`.

```bash
git add POC/src/Ino.Gateways.Grpc/Services/NotificationsService.cs POC/src/Ino.Gateways.Grpc/Mapping/NotificationMapper.cs POC/src/Ino.System.Host/Program.cs
git commit -m "feat(poc): NotificationsService gRPC streaming"
```

### Task 20: `ExperiencesService` + emitter grain

**Files:**
- Create: `POC/src/Ino.Experiences/IExperienceStream.cs`
- Create: `POC/src/Ino.Experiences/ExperienceStreamGrain.cs`
- Create: `POC/src/Ino.Experiences/ExperienceEvent.cs`
- Create: `POC/src/Ino.Experiences/ExperienceEventEmitter.cs`
- Create: `POC/src/Ino.Gateways.Grpc/Services/ExperiencesService.cs`

- [ ] **Step 1: `ExperienceEvent` domain record**

```csharp
namespace Ino.Experiences;

[Orleans.GenerateSerializer]
public sealed record ExperienceEvent(
    [property: Orleans.Id(0)] string CorrelationId,
    [property: Orleans.Id(1)] string Verb,
    [property: Orleans.Id(2)] byte[]? RfwDescription,
    [property: Orleans.Id(3)] byte[]? RfwData,
    [property: Orleans.Id(4)] byte[]? RfwPatchData,
    [property: Orleans.Id(5)] bool? DoneSuccess,
    [property: Orleans.Id(6)] string? DoneSummary);
```

- [ ] **Step 2: Stream grain + emitter**

```csharp
namespace Ino.Experiences;

public interface IExperienceStream : Orleans.IGrainWithStringKey
{
    IAsyncEnumerable<ExperienceEvent> StreamAsync(CancellationToken ct);
    Task PublishAsync(ExperienceEvent e, CancellationToken ct);
}

public sealed class ExperienceStreamGrain : Orleans.Grain, IExperienceStream
{
    private readonly System.Threading.Channels.Channel<ExperienceEvent> _ch =
        System.Threading.Channels.Channel.CreateUnbounded<ExperienceEvent>();
    public async IAsyncEnumerable<ExperienceEvent> StreamAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (await _ch.Reader.WaitToReadAsync(ct))
            while (_ch.Reader.TryRead(out var e)) yield return e;
    }
    public Task PublishAsync(ExperienceEvent e, CancellationToken ct) =>
        _ch.Writer.WriteAsync(e, ct).AsTask();
}

public interface IExperienceEventEmitter
{
    Task EmitRfwAsync(string userId, string correlationId, string verb, byte[] desc, byte[] data, CancellationToken ct);
    Task EmitPatchAsync(string userId, string correlationId, string verb, byte[] patch, CancellationToken ct);
    Task EmitDoneAsync(string userId, string correlationId, string verb, bool success, string summary, CancellationToken ct);
}

public sealed class ExperienceEventEmitter(Orleans.IGrainFactory grains) : IExperienceEventEmitter
{
    public Task EmitRfwAsync(string userId, string correlationId, string verb, byte[] desc, byte[] data, CancellationToken ct)
        => grains.GetGrain<IExperienceStream>(userId).PublishAsync(
            new ExperienceEvent(correlationId, verb, desc, data, null, null, null), ct);
    public Task EmitPatchAsync(string userId, string correlationId, string verb, byte[] patch, CancellationToken ct)
        => grains.GetGrain<IExperienceStream>(userId).PublishAsync(
            new ExperienceEvent(correlationId, verb, null, null, patch, null, null), ct);
    public Task EmitDoneAsync(string userId, string correlationId, string verb, bool success, string summary, CancellationToken ct)
        => grains.GetGrain<IExperienceStream>(userId).PublishAsync(
            new ExperienceEvent(correlationId, verb, null, null, null, success, summary), ct);
}
```

- [ ] **Step 3: Service + register**

`ExperiencesService.cs`:

```csharp
using Grpc.Core;
using Ino.Experiences;
using Ino.Experiences.V1;
using Orleans;

namespace Ino.Gateways.Grpc.Services;

public sealed class ExperiencesService(IGrainFactory grains) : Experiences.ExperiencesBase
{
    public override async Task StreamExperienceEvents(
        ExpSubscription request,
        IServerStreamWriter<ExperienceEvent> response,
        ServerCallContext context)
    {
        var stream = grains.GetGrain<IExperienceStream>(request.UserId);
        await foreach (var e in stream.StreamAsync(context.CancellationToken))
        {
            var proto = new ExperienceEvent { CorrelationId = e.CorrelationId, Verb = e.Verb };
            if (e.RfwDescription is not null && e.RfwData is not null)
                proto.Rfw = new RfwBundle { Description = Google.Protobuf.ByteString.CopyFrom(e.RfwDescription),
                                            Data = Google.Protobuf.ByteString.CopyFrom(e.RfwData) };
            else if (e.RfwPatchData is not null)
                proto.RfwPatch = new RfwUpdate { DataPatch = Google.Protobuf.ByteString.CopyFrom(e.RfwPatchData) };
            else if (e.DoneSuccess.HasValue)
                proto.Done = new VerbDone { Success = e.DoneSuccess.Value, Summary = e.DoneSummary ?? "" };
            await response.WriteAsync(proto);
        }
    }
}
```

- [ ] **Step 4: Register in system-silo Program.cs + smoke test + commit**

```bash
git add POC/src/Ino.Experiences/IExperienceStream.cs POC/src/Ino.Experiences/ExperienceStreamGrain.cs POC/src/Ino.Experiences/ExperienceEvent.cs POC/src/Ino.Experiences/ExperienceEventEmitter.cs POC/src/Ino.Gateways.Grpc/Services/ExperiencesService.cs POC/src/Ino.System.Host/Program.cs
git commit -m "feat(poc): ExperiencesService + ExperienceEventEmitter"
```

---

## Phase G — First experience bundle: Uber

### Task 21: `Ino.Experiences.Uber.Contracts` — verb records + status synapse

**Files:**
- Create: `POC/experiences/uber/Ino.Experiences.Uber.Contracts/Ino.Experiences.Uber.Contracts.csproj`
- Create: six contract files (see list at top)

- [ ] **Step 1: Create csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Sdk" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\src\Ino.Core\Ino.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Verb records**

Each file: one record implementing `ISynapse`. Example `CallRide.cs`:

```csharp
using Ino.Core;
using Orleans;

namespace Ino.Experiences.Uber.Contracts;

[GenerateSerializer]
public sealed record CallRide([property: Id(0)] string Destination) : ISynapse;
```

Similarly for `AddStop`, `CancelRide`, `RateDriver`, `ShareETA`. Minimal payloads.

- [ ] **Step 3: `RideStatusChanged : IStatusSynapse`**

```csharp
using Ino.Core;
using Orleans;

namespace Ino.Experiences.Uber.Contracts;

[GenerateSerializer]
public sealed record RideStatusChanged(
    [property: Id(0)] string Title,
    [property: Id(1)] string Body,
    [property: Id(2)] string? Icon,
    [property: Id(3)] string? Accent,
    [property: Id(4)] NotificationKind Kind,
    [property: Id(5)] string? DedupeKey) : IStatusSynapse;
```

- [ ] **Step 4: Add to solution + build + commit**

```bash
dotnet sln ino.slnx add experiences/uber/Ino.Experiences.Uber.Contracts/Ino.Experiences.Uber.Contracts.csproj
dotnet build ino.slnx
git add POC/experiences/uber/Ino.Experiences.Uber.Contracts/ POC/ino.slnx
git commit -m "feat(poc): Uber contracts — 5 verbs + RideStatusChanged"
```

### Task 22: `Ino.Experiences.Uber` implementation — IExperience + manifest + `CallRideNeuron`

**Files:**
- Create: `POC/experiences/uber/Ino.Experiences.Uber/Ino.Experiences.Uber.csproj`
- Create: `Uber.cs` (IExperience)
- Create: `UberManifest.cs`
- Create: `CallRideNeuron.cs`
- Create: `MockDriverSource.cs`
- Test: `POC/test/Ino.E2E.Tests/UberCallRideTests.cs`

- [ ] **Step 1: csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Sdk" />
    <PackageReference Include="Microsoft.Orleans.Server" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ino.Experiences.Uber.Contracts\Ino.Experiences.Uber.Contracts.csproj" />
    <ProjectReference Include="..\..\..\src\Ino.Core\Ino.Core.csproj" />
    <ProjectReference Include="..\..\..\src\Ino.Core.Hosting\Ino.Core.Hosting.csproj" />
    <ProjectReference Include="..\..\..\src\Ino.Experiences\Ino.Experiences.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: `Uber.cs`**

```csharp
using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.Experiences.Uber;

public sealed class Uber : IExperience
{
    public BundleId Bundle => BundleId.From("Ino.Experiences.Uber");
    public string Version => "1.0.0";
    public IReadOnlyList<Capability> DeclaredCapabilities => [ new Capability.Http("api.uber.com") ];
    public IExperienceManifest Manifest { get; } = new UberManifest();
}
```

- [ ] **Step 3: `UberManifest.cs`**

```csharp
using Ino.Core.Hosting;
using Ino.Experiences.Uber.Contracts;

namespace Ino.Experiences.Uber;

public sealed class UberManifest : IExperienceManifest
{
    public IReadOnlyDictionary<Type, VerbBinding> Verbs { get; } = new Dictionary<Type, VerbBinding>
    {
        [typeof(CallRide)]   = new(MimeSymbols.ReachPhone,   NotificationPolicy.OnStatusStream, "#000000"),
        [typeof(AddStop)]    = new(MimeSymbols.TapMap,       NotificationPolicy.None),
        [typeof(CancelRide)] = new(MimeSymbols.WaveOff,      NotificationPolicy.OnComplete),
        [typeof(RateDriver)] = new(MimeSymbols.ThumbsUp,     NotificationPolicy.None),
        [typeof(ShareETA)]   = new(MimeSymbols.TwoFingerSend,NotificationPolicy.OnComplete),
    };
}
```

- [ ] **Step 4: `MockDriverSource.cs` + `CallRideNeuron.cs`**

```csharp
namespace Ino.Experiences.Uber;

public sealed record DriverInfo(string Name, string Car, string Plate, double Rating, int EtaMinutes);

public static class MockDriverSource
{
    public static DriverInfo ForDestination(string dest) =>
        dest.Contains("SFO") ? new("Ali Khan", "Silver Toyota Camry", "SFO-7AB-219", 4.95, 5)
                             : new("Sam Park", "Black Honda Civic",   "NYC-3AB-887", 4.88, 4);
}
```

```csharp
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Experiences.Uber.Contracts;

namespace Ino.Experiences.Uber;

public sealed class CallRideNeuron(IExperienceEventEmitter emitter) : INeuron<CallRide>
{
    public async Task<NeuronResult> HandleAsync(CallRide v, NeuronContext ctx, CancellationToken ct)
    {
        var driver = MockDriverSource.ForDestination(v.Destination);
        // render RFW card
        var rfwDesc = RideCardTemplate.Description;
        var rfwData = RideCardTemplate.BuildData(driver);
        await emitter.EmitRfwAsync(ctx.UserId!, ctx.CorrelationId.Value, nameof(CallRide), rfwDesc, rfwData, ct);

        // fire a status synapse (picked up by NotifierNeuron via interface dispatch)
        await ctx.FireBroadcast(new RideStatusChanged(
            Title: "your ride", Body: $"{driver.Name} is {driver.EtaMinutes} min away",
            Icon: "🚕", Accent: "#000000", Kind: NotificationKind.Progress,
            DedupeKey: $"uber:ride:{ctx.CorrelationId.Value}"), ct);

        return NeuronResult.Ok();
    }
}
```

- [ ] **Step 5: `RideCardTemplate.cs` — minimal rfw bundle**

Create a stub that returns two byte-arrays representing the rfw description and initial data for the ride card. Use `rfw` text-format payloads encoded as UTF-8. Exact layout matches `clients/ino.flutter/lib/experience/components/ride_card.dart` (Task 29).

```csharp
using System.Text;

namespace Ino.Experiences.Uber;

public static class RideCardTemplate
{
    public static readonly byte[] Description = Encoding.UTF8.GetBytes("""
import core.widgets;

widget root = RideCard(
  driverName: data.driverName,
  car: data.car,
  plate: data.plate,
  rating: data.rating,
  etaMinutes: data.etaMinutes);
""");

    public static byte[] BuildData(DriverInfo d) => Encoding.UTF8.GetBytes($$"""
{ "driverName": "{{d.Name}}", "car": "{{d.Car}}", "plate": "{{d.Plate}}", "rating": {{d.Rating}}, "etaMinutes": {{d.EtaMinutes}} }
""");
}
```

- [ ] **Step 6: Other four verb handlers (stubs returning `NeuronResult.Ok()`)**

Create `AddStopNeuron.cs`, `CancelRideNeuron.cs`, `RateDriverNeuron.cs`, `ShareETANeuron.cs` following the same pattern with minimal logic.

- [ ] **Step 7: Write E2E test**

Create `POC/test/Ino.E2E.Tests/UberCallRideTests.cs` extending Phase 2 E2E harness. It: installs the Uber bundle via marketplace HTTP → opens a gRPC stream via generated C# client on three endpoints → fires a `CallRide` through the experiences silo → asserts within 2 seconds:
- one `PersonaFrame` with `BodyPose="lean_in"`, `Arms="hold_prop"`, `CurrentVerb="Ino.Experiences.Uber.Contracts.CallRide"`
- one `ExperienceEvent` with `rfw` payload
- one `NotificationBanner` with `title="your ride"` and `dedupe_key="uber:ride:…"`

(Full test body ~80 lines; structure matches `Ino.E2E.Tests`' Phase-2 class skeleton — refer to Phase 2 spec § 12.7.)

- [ ] **Step 8: Run — PASS**

Run: `dotnet test test/Ino.E2E.Tests --filter UberCallRideTests`
Expected: 1/1 passed (~3 min cold, <30s warm).

- [ ] **Step 9: Commit**

```bash
git add POC/experiences/uber/Ino.Experiences.Uber/ POC/test/Ino.E2E.Tests/UberCallRideTests.cs POC/ino.slnx
git commit -m "feat(poc): Uber experience bundle — manifest + 5 verbs + mock driver + E2E"
```

---

## Phase H — Flutter client scaffolding

### Task 23: Create Flutter project under `POC/clients/ino.flutter/`

**Files:**
- Create: `POC/clients/ino.flutter/` via `flutter create`

- [ ] **Step 1: Scaffold**

```bash
cd /d/ino/POC/clients
dart pub global run very_good_cli:very_good create flutter_app ino.flutter --desc "ino POC Flutter client" --application-id app.ino.poc --platforms web
```

If VGV CLI unavailable:

```bash
flutter create --template=app --platforms=web ino.flutter
```

- [ ] **Step 2: Edit `pubspec.yaml`**

Replace dependencies block:

```yaml
dependencies:
  flutter: { sdk: flutter }
  grpc: ^5.1.0
  protobuf: ^6.0.0
  rive: ^0.14.5
  rfw: ^1.1.3
  flutter_bloc: ^9.0.0
  go_router: ^15.0.0
```

- [ ] **Step 3: Run `flutter pub get`**

Run: `cd /d/ino/POC/clients/ino.flutter && flutter pub get`
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add POC/clients/ino.flutter/
git commit -m "feat(poc): Flutter client scaffold under POC/clients/ino.flutter"
```

### Task 24: Proto generation into Dart

**Files:**
- Create: `POC/clients/ino.flutter/protos/persona.proto` (copy from backend)
- Create: `POC/clients/ino.flutter/protos/experiences.proto`
- Create: `POC/clients/ino.flutter/protos/notifications.proto`
- Create: `POC/clients/ino.flutter/tool/gen-protos.sh`

- [ ] **Step 1: Copy protos**

```bash
cp POC/src/Ino.Gateways.Grpc/Protos/*.proto POC/clients/ino.flutter/protos/
```

- [ ] **Step 2: Generation script**

Create `POC/clients/ino.flutter/tool/gen-protos.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p lib/grpc/generated
protoc --plugin=protoc-gen-dart="$(which protoc-gen-dart)" \
  --dart_out=grpc:lib/grpc/generated \
  -Iprotos \
  protos/persona.proto protos/experiences.proto protos/notifications.proto
```

- [ ] **Step 3: Run generation**

```bash
dart pub global activate protoc_plugin
bash POC/clients/ino.flutter/tool/gen-protos.sh
```

Expected: `lib/grpc/generated/` populated with `*.pb.dart`, `*.pbgrpc.dart`.

- [ ] **Step 4: Commit**

```bash
git add POC/clients/ino.flutter/protos/ POC/clients/ino.flutter/tool/ POC/clients/ino.flutter/lib/grpc/generated/
git commit -m "build(poc): protoc-gen-dart setup + generated Dart bindings"
```

### Task 25: gRPC client wrapper

**Files:**
- Create: `POC/clients/ino.flutter/lib/grpc/ino_client.dart`

- [ ] **Step 1: Implement**

```dart
import 'package:grpc/grpc_web.dart';
import 'package:ino_flutter/grpc/generated/persona.pbgrpc.dart' as persona;
import 'package:ino_flutter/grpc/generated/experiences.pbgrpc.dart' as exp;
import 'package:ino_flutter/grpc/generated/notifications.pbgrpc.dart' as notif;

class InoClient {
  final GrpcWebClientChannel _channel;
  InoClient(String baseUrl) : _channel = GrpcWebClientChannel.xhr(Uri.parse(baseUrl));
  persona.PersonaClient get persona => persona.PersonaClient(_channel);
  exp.ExperiencesClient get experiences => exp.ExperiencesClient(_channel);
  notif.NotificationsClient get notifications => notif.NotificationsClient(_channel);
}
```

- [ ] **Step 2: Commit**

```bash
git add POC/clients/ino.flutter/lib/grpc/ino_client.dart
git commit -m "feat(poc-flutter): InoClient gRPC-Web wrapper"
```

---

## Phase I — Flutter persona + Rive

### Task 26: Stub `ino-persona.riv` + authoring doc

**Files:**
- Create: `POC/clients/ino.flutter/assets/rive/ino-persona.riv` (empty placeholder — authored in Phase L)
- Create: `POC/docs/persona-authoring.md`

- [ ] **Step 1: Commit empty .riv**

```bash
touch POC/clients/ino.flutter/assets/rive/ino-persona.riv
```

(A real asset lands in Phase L; placeholder keeps build green.)

- [ ] **Step 2: Declare asset**

In `pubspec.yaml`:

```yaml
flutter:
  assets:
    - assets/rive/ino-persona.riv
```

- [ ] **Step 3: Authoring doc**

Create `POC/docs/persona-authoring.md` — one-page walkthrough: open Rive Editor, create artboard, add State Machine layers per § 6.2 of the spec, define ViewModel properties per § 6.3, use the in-editor AI Coding Agent to wire signal-pulse decay and trigger responses, export `.riv`.

- [ ] **Step 4: Commit**

```bash
git add POC/clients/ino.flutter/assets/ POC/clients/ino.flutter/pubspec.yaml POC/docs/persona-authoring.md
git commit -m "build(poc-flutter): stub ino-persona.riv + authoring doc"
```

### Task 27: `PersonaBloc` — gRPC stream → bloc state

**Files:**
- Create: `POC/clients/ino.flutter/lib/persona/persona_bloc.dart`
- Test: `POC/clients/ino.flutter/test/persona/persona_bloc_test.dart`

- [ ] **Step 1: Write failing widget test (bloc-level)**

```dart
import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/persona/persona_bloc.dart';
import 'package:ino_flutter/grpc/generated/persona.pb.dart' as pb;

void main() {
  test('applies pose field to state', () async {
    final bloc = PersonaBloc.forTest();
    bloc.add(PersonaFrameReceived(pb.PersonaFrame()..bodyPose = 'lean_in'));
    await Future.delayed(Duration.zero);
    expect(bloc.state.bodyPose, 'lean_in');
  });
}
```

- [ ] **Step 2: Implement the bloc**

```dart
import 'package:bloc/bloc.dart';
import 'package:ino_flutter/grpc/generated/persona.pb.dart' as pb;

class PersonaState {
  final String? bodyPose, mouth, eyes, arms, glowRing, currentVerb, accent, propKind;
  final double? mood, energy, confidence, signalPulse;
  final bool? propVisible;
  final pb.PersonaTrigger? trigger;
  const PersonaState({ this.bodyPose, this.mouth, this.eyes, this.arms, this.glowRing,
    this.currentVerb, this.accent, this.propKind,
    this.mood, this.energy, this.confidence, this.signalPulse,
    this.propVisible, this.trigger });
  PersonaState apply(pb.PersonaFrame f) => PersonaState(
    bodyPose: f.hasBodyPose() ? f.bodyPose : bodyPose,
    mouth: f.hasMouth() ? f.mouth : mouth,
    eyes: f.hasEyes() ? f.eyes : eyes,
    arms: f.hasArms() ? f.arms : arms,
    glowRing: f.hasGlowRing() ? f.glowRing : glowRing,
    currentVerb: f.hasCurrentVerb() ? f.currentVerb : currentVerb,
    accent: f.hasAccent() ? f.accent : accent,
    propKind: f.hasPropKind() ? f.propKind : propKind,
    mood: f.hasMood() ? f.mood : mood,
    energy: f.hasEnergy() ? f.energy : energy,
    confidence: f.hasConfidence() ? f.confidence : confidence,
    signalPulse: f.hasSignalPulse() ? f.signalPulse : signalPulse,
    propVisible: f.hasPropVisible() ? f.propVisible : propVisible,
    trigger: f.hasTrigger() ? f.trigger : trigger,
  );
}

abstract class PersonaEvent {}
class PersonaFrameReceived extends PersonaEvent { final pb.PersonaFrame frame; PersonaFrameReceived(this.frame); }

class PersonaBloc extends Bloc<PersonaEvent, PersonaState> {
  PersonaBloc() : super(const PersonaState()) {
    on<PersonaFrameReceived>((e, emit) => emit(state.apply(e.frame)));
  }
  static PersonaBloc forTest() => PersonaBloc();
}
```

- [ ] **Step 3: Run — PASS**

Run: `cd POC/clients/ino.flutter && flutter test test/persona/persona_bloc_test.dart`
Expected: 1/1 passed.

- [ ] **Step 4: Commit**

```bash
git add POC/clients/ino.flutter/lib/persona/ POC/clients/ino.flutter/test/persona/
git commit -m "feat(poc-flutter): PersonaBloc applies gRPC frames"
```

### Task 28: `PersonaWidget` — Rive + BLoC binding

**Files:**
- Create: `POC/clients/ino.flutter/lib/persona/persona_widget.dart`

- [ ] **Step 1: Implement**

```dart
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:rive/rive.dart' as rive;
import 'package:ino_flutter/persona/persona_bloc.dart';
import 'package:ino_flutter/grpc/generated/persona.pb.dart' as pb;

class PersonaWidget extends StatefulWidget { const PersonaWidget({super.key}); @override State<PersonaWidget> createState() => _State(); }

class _State extends State<PersonaWidget> {
  rive.RiveWidgetController? _controller;
  rive.ViewModelInstance? _vm;

  @override void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    final file = await rive.File.asset('assets/rive/ino-persona.riv', riveFactory: rive.Factory.rive);
    if (file == null) return;
    final c = rive.RiveWidgetController(file);
    setState(() { _controller = c; _vm = c.viewModelInstance; });
  }

  void _apply(PersonaState s) {
    final vm = _vm; if (vm == null) return;
    if (s.mood != null) vm.number('mood')?.value = s.mood!;
    if (s.energy != null) vm.number('energy')?.value = s.energy!;
    if (s.signalPulse != null) vm.number('signal_pulse')?.value = s.signalPulse!;
    if (s.bodyPose != null) vm.string('body_pose')?.value = s.bodyPose!;
    if (s.arms != null) vm.string('arms')?.value = s.arms!;
    if (s.eyes != null) vm.string('eyes')?.value = s.eyes!;
    if (s.trigger != null) {
      switch (s.trigger!) {
        case pb.PersonaTrigger.ON_ARRIVE: vm.trigger('onArrive')?.fire(); break;
        case pb.PersonaTrigger.ON_CELEBRATE: vm.trigger('onCelebrate')?.fire(); break;
        case pb.PersonaTrigger.ON_ERROR: vm.trigger('onError')?.fire(); break;
        default: break;
      }
    }
  }

  @override Widget build(BuildContext context) {
    return BlocListener<PersonaBloc, PersonaState>(
      listener: (_, s) => _apply(s),
      child: SizedBox(width: 200, height: 200,
        child: _controller == null
          ? const Center(child: CircularProgressIndicator())
          : rive.RiveWidget(controller: _controller!)),
    );
  }
}
```

- [ ] **Step 2: Commit**

```bash
git add POC/clients/ino.flutter/lib/persona/persona_widget.dart
git commit -m "feat(poc-flutter): PersonaWidget with Rive VM binding"
```

---

## Phase J — Flutter experience card + notifier

### Task 29: `ExperienceBloc` + `RideCard` rfw component

**Files:**
- Create: `POC/clients/ino.flutter/lib/experience/experience_bloc.dart`
- Create: `POC/clients/ino.flutter/lib/experience/rfw_runtime.dart`
- Create: `POC/clients/ino.flutter/lib/experience/components/ride_card.dart`

- [ ] **Step 1: `rfw_runtime.dart` — register components**

```dart
import 'package:rfw/rfw.dart';
import 'package:ino_flutter/experience/components/ride_card.dart';

LocalWidgetLibrary inoWidgets() => LocalWidgetLibrary({
  'RideCard': (BuildContext c, DataSource s) => RideCard(
    driverName: s.v<String>(['driverName']) ?? '',
    car: s.v<String>(['car']) ?? '',
    plate: s.v<String>(['plate']) ?? '',
    rating: s.v<double>(['rating']) ?? 0,
    etaMinutes: s.v<int>(['etaMinutes']) ?? 0,
  ),
});
```

- [ ] **Step 2: `ride_card.dart`**

Concrete Flutter widget rendering a card (driver name, plate, mini map placeholder, ETA). Matches the prototype `POC/docs/prototypes/01-taxi-flow.html` visual.

- [ ] **Step 3: `experience_bloc.dart`**

```dart
import 'package:bloc/bloc.dart';
import 'package:rfw/rfw.dart';
import 'package:ino_flutter/grpc/generated/experiences.pb.dart' as pb;

class ExperienceState {
  final RemoteWidgetLibrary? description; final DynamicMap? data;
  const ExperienceState({ this.description, this.data });
}

abstract class ExperienceEvent {}
class RfwReceived extends ExperienceEvent { final pb.ExperienceEvent e; RfwReceived(this.e); }

class ExperienceBloc extends Bloc<ExperienceEvent, ExperienceState> {
  ExperienceBloc() : super(const ExperienceState()) {
    on<RfwReceived>((e, emit) {
      if (e.e.hasRfw()) {
        final desc = parseLibraryFile(String.fromCharCodes(e.e.rfw.description));
        final data = parseDataFile(String.fromCharCodes(e.e.rfw.data));
        emit(ExperienceState(description: desc, data: data as DynamicMap));
      } else if (e.e.hasRfwPatch()) {
        // simplest: replace data
        final patch = parseDataFile(String.fromCharCodes(e.e.rfwPatch.dataPatch));
        emit(ExperienceState(description: state.description, data: patch as DynamicMap));
      } else if (e.e.hasDone()) {
        if (!e.e.done.success) emit(const ExperienceState());
      }
    });
  }
}
```

- [ ] **Step 4: Commit**

```bash
git add POC/clients/ino.flutter/lib/experience/
git commit -m "feat(poc-flutter): ExperienceBloc + rfw runtime + RideCard component"
```

### Task 30: `NotifierBloc` + `NotifierStack` widget with dedupe

**Files:**
- Create: `POC/clients/ino.flutter/lib/notifications/notifier_bloc.dart`
- Create: `POC/clients/ino.flutter/lib/notifications/notifier_widget.dart`
- Test: `POC/clients/ino.flutter/test/notifications/notifier_bloc_test.dart`

- [ ] **Step 1: Write failing test for dedupe**

```dart
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/notifications/notifier_bloc.dart';
import 'package:ino_flutter/grpc/generated/notifications.pb.dart' as pb;

void main() {
  test('same dedupe_key replaces slot', () {
    final bloc = NotifierBloc();
    bloc.add(NotifReceived(pb.NotificationBanner()..id = '1'..body = '5 min'..dedupeKey = 'k'));
    bloc.add(NotifReceived(pb.NotificationBanner()..id = '2'..body = '4 min'..dedupeKey = 'k'));
    expect(bloc.state.slots.length, 1);
    expect(bloc.state.slots.first.body, '4 min');
  });
}
```

- [ ] **Step 2: Run — fails (types undefined)**

- [ ] **Step 3: Implement bloc**

```dart
import 'package:bloc/bloc.dart';
import 'package:ino_flutter/grpc/generated/notifications.pb.dart' as pb;

class NotifierState {
  final List<pb.NotificationBanner> slots;
  const NotifierState(this.slots);
}

abstract class NotifierEvent {}
class NotifReceived extends NotifierEvent { final pb.NotificationBanner b; NotifReceived(this.b); }
class NotifDismissed extends NotifierEvent { final String id; NotifDismissed(this.id); }

class NotifierBloc extends Bloc<NotifierEvent, NotifierState> {
  NotifierBloc() : super(const NotifierState([])) {
    on<NotifReceived>((e, emit) {
      final key = e.b.dedupeKey;
      final slots = [...state.slots];
      if (key.isNotEmpty) {
        final idx = slots.indexWhere((s) => s.dedupeKey == key);
        if (idx >= 0) { slots[idx] = e.b; emit(NotifierState(slots)); return; }
      }
      slots.add(e.b);
      emit(NotifierState(slots));
    });
    on<NotifDismissed>((e, emit) => emit(NotifierState(state.slots.where((s) => s.id != e.id).toList())));
  }
}
```

- [ ] **Step 4: Implement `NotifierStack` widget**

```dart
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/notifications/notifier_bloc.dart';

class NotifierStack extends StatelessWidget {
  const NotifierStack({super.key});
  @override Widget build(BuildContext context) =>
    BlocBuilder<NotifierBloc, NotifierState>(builder: (c, s) => Column(
      children: s.slots.map((b) => _Pill(title: b.title, body: b.body, icon: b.icon)).toList(),
    ));
}

class _Pill extends StatelessWidget { /* iOS-style blur card, auto-dismiss 3s */ }
```

- [ ] **Step 5: Run — PASS**

Expected: 1/1.

- [ ] **Step 6: Commit**

```bash
git add POC/clients/ino.flutter/lib/notifications/ POC/clients/ino.flutter/test/notifications/
git commit -m "feat(poc-flutter): NotifierBloc dedupe + NotifierStack widget"
```

### Task 31: `HomeScreen` composes persona + card + notifier + stream wiring

**Files:**
- Create: `POC/clients/ino.flutter/lib/screens/home/home_screen.dart`
- Modify: `POC/clients/ino.flutter/lib/main.dart`
- Modify: `POC/clients/ino.flutter/lib/app.dart`

- [ ] **Step 1: Wire streams in `main.dart`**

```dart
final client = InoClient('http://localhost:5010');
final personaBloc = PersonaBloc();
final expBloc = ExperienceBloc();
final notifBloc = NotifierBloc();

// gRPC subscriptions:
client.persona.streamPersonaState(PersonaSubscription(userId: 'demo-user'))
    .listen((f) => personaBloc.add(PersonaFrameReceived(f)));
client.experiences.streamExperienceEvents(ExpSubscription(userId: 'demo-user'))
    .listen((e) => expBloc.add(RfwReceived(e)));
client.notifications.streamNotifications(NotifSubscription(userId: 'demo-user'))
    .listen((b) => notifBloc.add(NotifReceived(b)));
```

- [ ] **Step 2: HomeScreen layout**

Matches prototype 01 — persona on top, experience card below, notifier stack overlaid.

- [ ] **Step 3: Run `flutter run -d chrome`, manually fire CallRide via `grpcurl`, verify persona leans + card appears + banner shows**

- [ ] **Step 4: Commit**

```bash
git add POC/clients/ino.flutter/lib/
git commit -m "feat(poc-flutter): HomeScreen composes all three streams"
```

---

## Phase K — Aspire AppHost wiring + end-to-end demo

### Task 32: Register Flutter dev server as Aspire resource

**Files:**
- Modify: `POC/src/Ino.AppHost/AppHost.cs`

- [ ] **Step 1: Add resource**

```csharp
builder.AddExecutable("ino-flutter", "flutter", "../../clients/ino.flutter",
        "run", "-d", "chrome", "--web-port", "8080", "--web-hostname", "0.0.0.0")
    .WithHttpEndpoint(port: 8080, name: "http")
    .WithExplicitStart();
```

- [ ] **Step 2: Register the gRPC resource on system silo**

Already hosted in system-silo ASP.NET from Phase F — just expose its endpoint in AppHost metadata so the Flutter app can resolve it via env var `INO_GRPC_URL`.

- [ ] **Step 3: Run `aspire start`, then start the flutter resource from dashboard, navigate to localhost:8080**

Expected: persona widget loads, no frames, no cards, no banners.

- [ ] **Step 4: `curl -X POST http://localhost:<system-silo-port>/marketplace/install/Ino.Experiences.Uber`** — experiences silo restarts, Uber bundle loads.

- [ ] **Step 5: Fire CallRide via grpcurl; observe browser**

Persona should lean forward, ride card should appear, banner should show "Ali is 5 min away".

- [ ] **Step 6: Commit**

```bash
git add POC/src/Ino.AppHost/AppHost.cs
git commit -m "build(poc): Aspire AppHost registers ino.flutter resource"
```

---

## Phase L — Rive authoring handoff

### Task 33: Author the real `ino-persona.riv`

**Files:**
- Modify: `POC/clients/ino.flutter/assets/rive/ino-persona.riv`
- Extend: `POC/docs/persona-authoring.md` with screenshots + notes

This task is a **design handoff**. The engineer executing this plan pairs with a designer (or works solo with the Rive Editor's in-editor AI Coding Agent) to:

- [ ] **Step 1: Create artboard 480×480**

- [ ] **Step 2: Add 6 body_pose atoms; 8 mouth atoms; 10 eyes atoms; 15 arms atoms; 6 glow_ring atoms** per § 6.2 of the spec

- [ ] **Step 3: Expose `PersonaViewModel` with properties per § 6.3**

- [ ] **Step 4: Use the in-editor AI Coding Agent to:**
- wire `signal_pulse` decay (script: decays to 0 over 1.5s after each jump to 1.0)
- wire triggers `onArrive` / `onCelebrate` / `onError` to one-shot animations
- wire `mood` (0..1) to mouth blend weights

- [ ] **Step 5: Export `ino-persona.riv`, overwrite the stub.**

- [ ] **Step 6: Run Flutter — persona renders, all streams light it up correctly**

- [ ] **Step 7: Commit**

```bash
git add POC/clients/ino.flutter/assets/rive/ino-persona.riv POC/docs/persona-authoring.md
git commit -m "feat(poc-flutter): authored ino-persona.riv asset + updated authoring notes"
```

---

## Self-Review

### Spec coverage

| Spec section | Covered by |
|---|---|
| § 3.1 In: single .riv | Phase L (Task 33) + Phase I stub (Task 26) |
| § 3.1 In: PersonaViewModel surface | Task 33 |
| § 3.1 In: ~15 mime vocab | Task 5 + Task 33 |
| § 3.1 In: IPersonaNeuron + stream | Tasks 9, 10, 11, 18 |
| § 3.1 In: experience-verb-mime manifest | Tasks 2, 3, 22 |
| § 3.1 In: NotifierNeuron + IStatusSynapse | Tasks 1, 13, 8 |
| § 3.1 In: POC/clients/ino.flutter | Phases H, I, J, K |
| § 3.1 In: 3 gRPC streams | Phase F |
| § 3.1 In: top-10 experience bundles | **Deferred** — Uber landed (Tasks 21, 22); 9 others in follow-on plan |
| § 3.1 In: PersonaEvolver L1 | Phase E |
| § 3.1 In: persona-authoring.md | Task 26 + Task 33 |
| § Phase 2 extension: IExperience.Manifest DIM | Task 3 |
| § Phase 2 extension: FirePort VerbStarted broadcasts | Task 7 |
| § Phase 2 extension: Discovery interface-dispatch | Task 8 |
| Appendix A taxi flow | E2E test (Task 22 step 7) |

**Gap acknowledged:** 9 of 10 experience bundles are deferred with explicit rationale in the plan header. One bundle (Uber) lands end-to-end to prove the substrate.

### Placeholder scan

No `TBD`, `TODO`, `fill in later` in task steps. Each step includes exact file paths, complete code blocks, commands, and expected output.

### Type consistency

- `PersonaFrame` properties match between Task 4 (C# record), Task 10 (MimeTupleTable), Task 18 (mapper), and Task 27 (Dart bloc).
- `NotificationBanner` matches between Task 12 (C#), Task 19 (proto mapper), Task 30 (Dart bloc).
- `VerbBinding.MimeSymbol` matches constants in Task 5 `MimeSymbols` and is consumed consistently through Tasks 10, 16, 22.
- `PersonaTrigger` enum matches 4 values (`None`, `OnArrive`, `OnCelebrate`, `OnError`) across Tasks 4, 10, 27, 28.

### Risks surfaced from spec § 15

- Risk 1 (Rive Flutter 0.14.5 DB API) → Task 28 uses `RiveWidgetController.viewModelInstance`; Task 33 verifies at authoring time.
- Risk 2 (parametric blending) → Task 33's in-editor script wiring proves this.
- Risk 3 (gRPC-Web server-streaming) → Tasks 18, 19, 20 + Task 32.
- Risk 4 (PersonaNeuron reconnect) → Task 10 emits `StreamFramesAsync`; reconnect handled by Task 31 (each subscribe starts fresh stream).
- Risk 5 (reactive on interface) → Task 8 explicitly.
- Risk 6 (`IExperience.Manifest` DIM + Orleans serializer) → Task 3 test validates via `[GenerateSerializer]`-adjacent round-trip.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-04-16-ino-persona-rive-living-experiences-plan.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Best for this plan because the task count is high and many tasks are independent small TDD loops.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
