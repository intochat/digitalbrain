# ino persona — living Rive character + experience-verb-mime architecture (design)

**Date:** 2026-04-16
**Scope:** greenfield inside `D:\ino\POC\` — adds the persona surface, experience-verb-mime model, cross-cutting Notifier neuron, and a Flutter client wired to Phase 2 silos; **does not modify `D:\ino\src\`**
**Status:** design locked in brainstorming session 2026-04-16; ready for implementation planning
**Builds on:**
- POC Phase 1 — `docs/superpowers/specs/2026-04-14-ino-poc-core-primitives-design.md`
- POC Phase 2 — `docs/superpowers/specs/2026-04-16-ino-poc-phase-2-cross-silo-runtime-design.md`
- Prototypes — `POC/docs/prototypes/01-taxi-flow.html`, `POC/docs/prototypes/02-experience-catalog.html`

**Supersedes (for the POC):** the Rive Editor MCP Server path in `docs/superpowers/specs/2026-04-11-ino-200-domains-persona-design.md` § 6.4.1. Rive's MCP integration was deprecated by Rive (see § 15.1); the POC replaces that path with Data Binding + ViewModels.

---

## 1. Goal

Prove that ino's product-layer unit — the **experience** — can be expressed as a triple `(IExperience bundle, canonical verb, persona mime)` such that:

1. Installing an experience adds a handful of verbs to ino without authoring any new persona animation.
2. When the user says a verb, the persona visibly *does* the verb in real-time — the character mimes the action, the RFW card streams the domain data, and the iOS/Android-style notification surface reports async status.
3. The persona is a single well-authored `.riv` asset driven parametrically through Rive Data Binding ViewModels — not an enumeration of pre-baked states, and not a runtime-generated asset.
4. ino grows richer over time by Claude writing new *mapping scripts* (experience-verb → VM property writes), never by generating new `.riv` binaries.

The thesis: the persona is a **durable UI substrate**, and experiences decorate it.

## 2. North-star — what "done" looks like

1. `aspire start` on the POC AppHost, wait for three silos healthy (§ Phase 2).
2. `POST /marketplace/install/Ino.Experiences.Uber` → experiences silo restarts → Uber verbs (`CallRide`, `AddStop`, `CancelRide`, `RateDriver`, `ShareETA`) are live.
3. Open `POC/clients/ino.flutter/` in a browser, type *"get me to SFO"*.
4. Persona in the top zone visibly mimes `reach_phone`: arm raises, phone prop fades in via in-.riv Rive Scripting, body leans slightly. At the same time:
   - An RFW "ride in progress" card streams in below (driver name, plate, ETA, mini-map).
   - A top-of-screen iOS-style notification banner appears: *"ino · your ride · Ali is 5 min away"*.
5. The Aspire traces tab shows: a `fire Uber.Contracts.CallRide` producer span → `handle` on `CallRideNeuron` → reactive `fire Uber.Contracts.RideStatusChanged` → `react` on the system-silo `NotifierNeuron` → client banner event emitted via gRPC stream.
6. Install `Ino.Experiences.Spotify`, type *"play say it by flume"*. The persona does `head_bob` (same VM writes; different clip selection driven by current verb string). No new animation authored between Uber install and Spotify install.

Zero hand-rolled Rive-per-verb assets. Zero per-experience notification plumbing. One persona asset, one notifier neuron, many experiences.

## 3. Scope

### 3.1 In

- One authored `ino-persona.riv` asset — single base body with composable atoms (body pose, mouth, eyes, arms, glow ring, prop layer)
- `PersonaViewModel` — the typed VM surface exposed by the asset for Flutter to write into (§ 6)
- ~15 shared **mime gestures** — the vocabulary every experience draws from (§ 7.1)
- `IPersonaNeuron` on the experiences silo translating synapse events into VM writes, streamed to the client via gRPC (§ 9)
- **Experience-verb-mime manifest** — a per-bundle static declaration mapping each verb (synapse type) to a mime symbol (§ 8)
- **NotifierNeuron** in the system silo — one cross-cutting `IReactsTo<IStatusSynapse>` listener that surfaces native-style banners via client gRPC stream (§ 10)
- `IStatusSynapse` marker — any reactive synapse an experience wants the Notifier to pick up (§ 10.1)
- `POC/clients/ino.flutter/` — Flutter web client with BLoC + gRPC + Rive + RFW, wired to Phase 2 silos (§ 12)
- Three proto-contract streams: `StreamPersonaState`, `StreamExperienceEvents`, `StreamNotifications` (§ 11)
- Top-10 experience bundles (from the top-100 app-store list, games excluded) authored as `IExperience` bundles with their verb manifests (§ 8.4): Uber, Spotify, Gmail, WhatsApp, Amazon, Uber Eats, Google Maps, Revolut, Weather, Calendar
- `PersonaEvolver` neuron — L1 mapping-script generator: when a new experience's verb lacks a mime mapping, fires Claude to produce a mapping declaration, stores it, hot-binds it (§ 13)
- Design-time tooling — a one-page `POC/docs/persona-authoring.md` describing the Rive Editor + in-editor AI Coding Agent workflow for authoring new atoms/mimes

### 3.2 Out — deferred to later phases

| Deferred piece | Why | Phase |
|---|---|---|
| Multi-persona library (Jarvis/Luna/Cortex/Coach/Sage) | Single body first; restyle needs its own design pass once the VM surface is proven | Phase 4+ |
| Runtime `.riv` generation | Rive MCP deprecated; binary format has readers but no public writers | Not planned |
| Headless Rive Editor as Aspire resource | Replaced by Data Binding approach | Not planned |
| Per-user persona blob storage | No runtime-generated assets to store | Not planned |
| Voice input / TTS / lip-sync | Big enough for its own spec | Phase 5 |
| Domain affinity visual morphing | Needs telemetry-stable signals; revisit after L1 mapping-script pipeline lands | Phase 4 |
| Remaining 90 experience bundles | Architecture supports them; top-10 prove the loop | Phase 4+ |
| Native iOS/Android app wrappers for real OS notifications | POC uses in-app banners matching the native look; true OS integration requires device builds | Phase 5 |
| Brain View, Timeline/Branch screens (from 2026-04-11 spec) | Orthogonal UI surfaces; this spec scopes to persona + experience + notification | Phase 6 |
| Rive Scripting for in-persona prop animation (phone, map, stars) | Optional polish pass once VM-only baseline is proven | Phase 4 |

## 4. Architecture overview

### 4.1 Three surfaces

```
┌────────────────────────────────────────────────────────────────┐
│                         client                                 │
│  POC/clients/ino.flutter  (web-first, mobile/desktop later)    │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────────────┐    │
│  │ Persona zone │  │  Card zone   │  │  Notification zone │    │
│  │   Rive       │  │    RFW       │  │ iOS-style banners  │    │
│  │   widget     │  │   runtime    │  │                    │    │
│  └─────▲────────┘  └─────▲────────┘  └─────▲──────────────┘    │
│        │ VM writes       │ rfw bytes       │ banner events     │
│        │                 │                 │                   │
│  ┌─────┴─────────────────┴─────────────────┴─────────────┐     │
│  │  BLoC + gRPC-Web streams:                             │     │
│  │   StreamPersonaState / StreamExperienceEvents /       │     │
│  │   StreamNotifications                                 │     │
│  └─────▲─────────────────────────────────────────────────┘     │
└────────┼───────────────────────────────────────────────────────┘
         │ gRPC / gRPC-Web (Phase 2 silos)
┌────────┴───────────────────────────────────────────────────────┐
│   experiences silo                 system silo                 │
│   ┌───────────────────────┐        ┌──────────────────────┐    │
│   │ installed IExperience │        │   NotifierNeuron     │    │
│   │ bundles (Uber, ...)   │        │  IReactsTo           │    │
│   │ verb handlers +       │        │  <IStatusSynapse>    │    │
│   │ verb→mime manifest    │        │                      │    │
│   └──────┬────────────────┘        └──────▲───────────────┘    │
│          │  fires reactive                │                    │
│          │  IStatusSynapse                │                    │
│          └───────────────────────────────>┘                    │
│   ┌───────────────────────┐                                    │
│   │ IPersonaNeuron        │                                    │
│   │ on-verb-start/end:    │                                    │
│   │ emit PersonaFrame     │                                    │
│   │ to client stream      │                                    │
│   └───────────────────────┘                                    │
└────────────────────────────────────────────────────────────────┘
```

### 4.2 Load-bearing decisions

| Decision | Choice | Why |
|---|---|---|
| Persona authoring model | Single `.riv` + composable atoms + VM surface | Continuous parameters, zero runtime generation, Rive's native data-binding path |
| Animation generation strategy | Design-time, in-editor, AI-assisted by Rive's own AI Coding Agent | External MCP deprecated; in-editor agent is stable and paid-plan gated |
| Runtime persona transport | ViewModel property writes | Parametric blends beat discrete state triggers |
| New-experience expressiveness | Verb→mime mapping in the bundle manifest | No new animation authored per experience; vocabulary stays flat |
| Async status | `IStatusSynapse` marker + one `NotifierNeuron` | Cross-cutting, opt-in per experience |
| Client location | `POC/clients/ino.flutter/` | Greenfield Flutter inside POC, wired to Phase 2 silos |
| Notification surface (POC) | In-app iOS-style banners rendered by the client | Web-first POC; real OS notifications deferred to device builds |
| Self-improvement unit | Mapping scripts (C#, L1) not `.riv` assets | Fits ino's existing L1 model; persona vocabulary is stable |

## 5. Experience × verb × mime — the product primitive

Every app in the top-100 app-store list (games excluded) maps to the same triple:

```
Experience   ≔  IExperience bundle (Phase 2 § 6.1)
Verb         ≔  a canonical synapse type inside that bundle  (an INeuron<T> handler)
Mime         ≔  a symbol from the shared gesture vocabulary  (§ 7.1)
```

- **Experience** installs, uninstalls, owns capability declarations, lifecycle.
- **Verbs** are what the user speaks or types: *"call a ride"*, *"add a stop"*, *"cancel"*, *"play say it"*. Each verb is a canonical synapse; each verb completes with a `NeuronResult`.
- **Mime** is what the character *does* with its body while the verb is executing. Mimes are universal and pre-authored; experiences reference them by symbol.

Installing `Ino.Experiences.Uber` adds 5 verbs (`CallRide`, `AddStop`, `CancelRide`, `RateDriver`, `ShareETA`), each mapped to an existing mime. No new Rive authoring. That is the load-bearing claim of this design.

## 6. The persona asset — one `.riv`, composable atoms, VM surface

### 6.1 Single asset

`POC/clients/ino.flutter/assets/rive/ino-persona.riv`. Authored once by a designer, with in-editor AI Coding Agent assistance, during a focused authoring sprint. One body (character), no per-persona variants in the POC. The asset is a long-lived design artifact versioned with the repo.

### 6.2 Composable atoms (Rive-side architecture)

The artboard contains independent animation layers the runtime blends automatically:

| Layer | Domain | Atom count | Examples |
|---|---|---|---|
| `body_pose` | overall posture | 6 | `neutral · lean_in · lean_back · present · curl_in · stretch` |
| `mouth` | expression | 8 | `neutral · smile · surprise · concern · speak · sigh · closed · micro_O` |
| `eyes` | gaze + lid | 10 | `forward · up · down · left · right · closed · squint · wide · blink · scan_sweep` |
| `arms` | gestural | 15 | `at_side · reach_out · point_forward · point_up · wave · thumbs_up · tap · swipe · write · peek · hold_prop · palm_up · shrug · scan_hand · two_finger_send` |
| `glow_ring` | ambient aura | 6 | `off · idle_pulse · scan_sweep · alert · celebrate_flash · urgent_throb` |
| `prop_layer` | optional in-hand / near-body element | (driven by Rive Scripting in a later phase) | `phone · map_fragment · stars · envelope · music_bars` |

Total atoms ≈ 45 authored at design time. The runtime never picks a cartesian product; it picks one atom per layer and blends them. The arrangement space is 6×8×10×15×6 ≈ 43,200 felt combinations from 45 authored clips.

### 6.3 `PersonaViewModel` — the typed surface

Rive Data Binding ViewModel. This is what the Flutter side writes into. Treat it like an interface contract:

```
PersonaViewModel {
  // continuous emotion + energy
  number  mood           0..1   // -0.5..+0.5 bipolar inside the artboard
  number  energy         0..1
  number  confidence     0..1
  number  signal_pulse   0..1   // spikes to 1.0 on synapse fire, decays in-asset

  // discrete layer selectors (enum strings or indices)
  string  body_pose        // one of the 6 body_pose atoms
  string  mouth            // one of the 8 mouth atoms
  string  eyes             // one of the 10 eyes atoms
  string  arms             // one of the 15 arms atoms
  string  glow_ring        // one of the 6 glow_ring atoms

  // current verb context (for prop layer selection + mini-overlays)
  string  current_verb       // e.g. "Uber.CallRide" or null/empty
  string  current_experience // e.g. "Uber" for theming the glow
  color   accent             // hex color; experience-provided override

  // prop layer (Phase 4 Rive Scripting)
  bool    prop_visible
  string  prop_kind          // "phone" · "map_fragment" · "stars" · ...

  // one-shots (triggers)
  trigger onArrive           // fires a quick bounce when card arrives
  trigger onCelebrate        // confetti-style flash
  trigger onError            // red ring flicker
}
```

The VM surface is the entire persona API. Flutter writes, Rive animates. Any mime symbol is a *tuple of writes* to these properties.

### 6.4 Mime symbols as VM-tuples

```
reach_phone          body_pose=lean_in  arms=hold_prop  eyes=down  prop_visible=true  prop_kind=phone
scan_horizon         body_pose=neutral  arms=scan_hand  eyes=scan_sweep
point_forward        body_pose=present  arms=point_forward  eyes=forward
tap_map              body_pose=present  arms=tap  eyes=down  signal_pulse→1.0 (decay)
wave_off             body_pose=lean_back arms=palm_up  mouth=concern
thumbs_up            body_pose=present  arms=thumbs_up  onCelebrate!
write                body_pose=neutral  arms=write  eyes=down
head_bob             body_pose=neutral  (internal head-nod animation keyed off signal_pulse pulses)
peek_box             body_pose=curl_in  arms=peek  eyes=forward
speak_into_hand      body_pose=lean_in  arms=hold_prop  prop_visible=true  prop_kind=phone  mouth=speak
slide_coins          body_pose=present  arms=two_finger_send  eyes=forward
…
```

~15 entries in total for the POC (§ 7.1). Adding a sixteenth mime is a design-time authoring task; until then, new experiences must map their verbs to one of the existing 15.

## 7. The shared mime vocabulary

### 7.1 The vocabulary (POC)

```
reach_phone          CallRide, StartCall, SendMessage voice
scan_horizon         SearchProduct, SearchPlace, ListAvailable
point_forward        Navigate, OpenApp
tap_map              AddStop, AddWaypoint, DropPin
wave_off             Cancel, Dismiss, Reject
thumbs_up            Confirm, RateDriver, RateFood, ApproveTx
write                Compose, ReplyMessage, CreateNote
peek_box             TrackOrder, CheckDelivery, ViewReceipt
head_bob             PlayMusic, Listen, Acknowledge
swipe_forward        Skip, Next, Advance
stack_items          Queue, CreatePlaylist, GroupItems
slide_coins          SendMoney, Pay, Transfer
tuck_away            Archive, Save, Hide
swap                 ExchangeFx, SwitchMode, ToggleSetting
two_finger_send      Share, ShareETA, ForwardMessage
```

15 mimes cover the top-10 experiences' 40-odd verbs. The per-verb mime mapping lives in each bundle's manifest (§ 8.2), not in the persona asset.

### 7.2 Why this is compact

Most product verbs are one of: reach, scan, point, tap, wave, approve, write, peek, listen, skip, stack, pay, tuck, swap, share. The *object* of the verb differs (ride vs message vs money) but the gestural archetype does not. The asset encodes archetypes; the RFW card + notification carry the object.

## 8. Experience manifest — the verb→mime declaration

### 8.1 Shape

Each `IExperience` implementation ships an immutable manifest declaring how its verbs map to mimes and which ones opt into notifications:

```csharp
namespace Ino.Core.Hosting;

public interface IExperienceManifest
{
    IReadOnlyDictionary<Type, VerbBinding> Verbs { get; }
}

public sealed record VerbBinding(
    string MimeSymbol,            // e.g. "reach_phone"
    NotificationPolicy Policy,    // § 10.4
    string? Accent = null);       // optional hex color override

public enum NotificationPolicy { None, OnComplete, OnStatusStream }
```

### 8.2 Example — Uber manifest

```csharp
namespace Ino.Experiences.Uber;

public sealed class UberManifest : IExperienceManifest
{
    public IReadOnlyDictionary<Type, VerbBinding> Verbs => new Dictionary<Type, VerbBinding>
    {
        [typeof(CallRide)]   = new("reach_phone",  NotificationPolicy.OnStatusStream, "#000000"),
        [typeof(AddStop)]    = new("tap_map",      NotificationPolicy.None),
        [typeof(CancelRide)] = new("wave_off",     NotificationPolicy.OnComplete),
        [typeof(RateDriver)] = new("thumbs_up",    NotificationPolicy.None),
        [typeof(ShareETA)]   = new("two_finger_send", NotificationPolicy.OnComplete),
    };
}
```

`IExperience` (Phase 2 § 6.1) is extended by a default interface method returning the manifest:

```csharp
public interface IExperience
{
    BundleId Bundle { get; }
    string Version { get; }
    IReadOnlyList<Capability> DeclaredCapabilities { get; }
    IReadOnlyDictionary<Type, IReadOnlyList<Capability>> PerGrainCapabilities { get; }
    IExperienceManifest Manifest { get; }         // NEW
}
```

Bundles without a manifest throw at registration — the system refuses to host an experience whose verbs have no persona expression. This forces authors to think about UX at bundle creation time, and keeps the runtime tight.

### 8.3 Unknown verb → `PersonaEvolver` L1 generation

When a bundle loads and any verb has a mime symbol the asset doesn't know (or the bundle is missing a `VerbBinding` entry for a newly-added handler), the experiences silo fires a `MimeMappingMissing` reactive synapse. `PersonaEvolver` (§ 13) reacts, calls Claude with the verb's synapse type + domain, generates a `VerbBinding` JSON, validates against the known-mimes list, and stores it. The next time the verb fires, the mapping is present. First fire in the missing window falls back to a default (`thinking_nod`).

### 8.4 Top-10 bundles in scope

| Bundle | Verbs | Notifications |
|---|---|---|
| `Ino.Experiences.Uber` | CallRide · AddStop · CancelRide · RateDriver · ShareETA | yes (ride status stream) |
| `Ino.Experiences.Spotify` | Play · Queue · Skip · CreatePlaylist | no (audio is its own feedback) |
| `Ino.Experiences.Gmail` | Compose · Reply · Archive · Search | yes (new-mail stream) |
| `Ino.Experiences.WhatsApp` | SendMessage · StartCall · ShareLocation · VoiceNote | yes (inbound messages) |
| `Ino.Experiences.Amazon` | SearchProduct · Buy · TrackOrder · Return | yes (order status stream) |
| `Ino.Experiences.UberEats` | OrderFood · Reorder · TrackOrder · RateFood | yes (courier status stream) |
| `Ino.Experiences.GoogleMaps` | Navigate · SearchNearby · SavePlace · ShareLocation | yes (traffic re-routes) |
| `Ino.Experiences.Revolut` | SendMoney · ExchangeFx · Split · CheckBalance | yes (transaction confirmations) |
| `Ino.Experiences.Weather` | ShowForecast · SetAlert | yes (severe-weather alerts) |
| `Ino.Experiences.Calendar` | ShowNext · CreateEvent · RespondInvite | yes (upcoming-event reminders) |

## 9. `IPersonaNeuron` — turning synapse events into VM writes

### 9.1 Purpose

One neuron per user session, hosted on the experiences silo. Observes verb-handler lifecycle (start → end, success/fail, and intermediate RFW card emissions), maintains a `PersonaFrame` stream, and pushes frames to the client via a gRPC server-streaming RPC.

### 9.2 Shape

```csharp
public interface IPersonaNeuron : IGrainWithStringKey
{
    // user id is the grain key
    IAsyncEnumerable<PersonaFrame> StreamFramesAsync(CancellationToken ct);
    Task OnVerbStartedAsync(Type verbType, CorrelationId correlation, CancellationToken ct);
    Task OnVerbCompletedAsync(Type verbType, CorrelationId correlation, NeuronResult result, CancellationToken ct);
    Task OnSignalPulseAsync(CorrelationId correlation, CancellationToken ct);   // other synapses firing
}

public sealed record PersonaFrame(
    // every write is optional — only what changed is sent
    double? Mood       = null,
    double? Energy     = null,
    double? Confidence = null,
    double? SignalPulse = null,
    string? BodyPose   = null,
    string? Mouth      = null,
    string? Eyes       = null,
    string? Arms       = null,
    string? GlowRing   = null,
    string? CurrentVerb = null,
    string? CurrentExperience = null,
    string? Accent     = null,
    bool?   PropVisible = null,
    string? PropKind   = null,
    PersonaTrigger? Trigger = null);

public enum PersonaTrigger { None, OnArrive, OnCelebrate, OnError }
```

### 9.3 Verb → frame derivation

This phase extends Phase 2's `FirePort` (§ 10.1) with a pre/post hook that broadcasts `VerbStarted`/`VerbCompleted` **reactive** synapses around every canonical `INeuron<T>.HandleAsync` call. The hook is a single change to `FirePort.Fire<T>`; it adds two `FireBroadcast` calls per verb, keyed by the caller's `CorrelationId`.

When a verb starts:
1. `FirePort` broadcasts `VerbStarted(verbType, correlation)` just before the canonical handler invocation.
2. `IPersonaNeuron` reacts via `IReactsTo<VerbStarted>`, resolves `verbType` → bundle → `IExperienceManifest.Verbs[verbType]` → mime symbol → VM-tuple (§ 6.4).
3. Emits one `PersonaFrame` carrying the tuple + `CurrentVerb` + `CurrentExperience` + `Accent` onto the per-user stream.

When the verb completes:
1. `FirePort` broadcasts `VerbCompleted(verbType, correlation, result)` after the canonical handler returns or throws.
2. `IPersonaNeuron` emits either `Trigger = OnCelebrate` (success) or `Trigger = OnError` (failure) + returns body to `body_pose=neutral`.

When any other synapse fires (not the current verb): `SignalPulse` bumps to `1.0`; Rive side decays it in-asset.

### 9.4 Transport

gRPC proto:

```protobuf
service Persona {
  rpc StreamPersonaState(PersonaSubscription) returns (stream PersonaFrame);
}

message PersonaSubscription { string user_id = 1; }

message PersonaFrame {
  // field presence communicates "change"
  optional double mood         = 1;
  optional double energy       = 2;
  optional double confidence   = 3;
  optional double signal_pulse = 4;
  optional string body_pose    = 5;
  optional string mouth        = 6;
  optional string eyes         = 7;
  optional string arms         = 8;
  optional string glow_ring    = 9;
  optional string current_verb = 10;
  optional string current_experience = 11;
  optional string accent       = 12;
  optional bool   prop_visible = 13;
  optional string prop_kind    = 14;
  optional PersonaTrigger trigger = 15;
}

enum PersonaTrigger { NONE = 0; ON_ARRIVE = 1; ON_CELEBRATE = 2; ON_ERROR = 3; }
```

Field presence (`optional` in proto3) is load-bearing: absent = don't write.

## 10. `NotifierNeuron` — one cross-cutting async-status surface

### 10.1 The marker

```csharp
namespace Ino.Core;

public interface IStatusSynapse : ISynapse
{
    string Title { get; }     // e.g. "your ride"
    string Body { get; }      // e.g. "Ali is 5 min away"
    string? Icon { get; }     // resource id / emoji / data-uri; null = use experience default
    string? Accent { get; }   // hex; null = use experience default
    NotificationKind Kind { get; }
    string? DedupeKey { get; } // optional — collapse updates to same subject
}

public enum NotificationKind { Info, Progress, Success, Warning, Urgent }
```

Any reactive synapse that implements `IStatusSynapse` is picked up by `NotifierNeuron`. Experiences opt in by fire-and-forget-firing these synapses from inside their verb handlers (e.g. Uber's `CallRideNeuron` fires `RideStatusChanged : IStatusSynapse` on every ETA update).

### 10.2 The neuron

```csharp
namespace Ino.System;

public sealed class NotifierNeuron(
    ILogger<NotifierNeuron> logger,
    INotificationStream stream) : Grain, IReactsTo<IStatusSynapse>
{
    public async Task ReactAsync(IStatusSynapse synapse, NeuronContext ctx, CancellationToken ct)
    {
        var banner = new NotificationBanner(
            Id: Ulid.NewUlid().ToString(),
            Title: synapse.Title,
            Body: synapse.Body,
            Icon: synapse.Icon,
            Accent: synapse.Accent,
            Kind: synapse.Kind,
            DedupeKey: synapse.DedupeKey,
            At: DateTimeOffset.UtcNow);

        await stream.PublishAsync(ctx.UserId ?? "anon", banner, ct);
    }
}
```

Hosted on the **system silo** (not experiences) — it's a cross-cutting capability, available to every installed bundle automatically, independent of any one experience's install state.

### 10.3 Transport

```protobuf
service Notifications {
  rpc StreamNotifications(NotifSubscription) returns (stream NotificationBanner);
}

message NotificationBanner {
  string id          = 1;
  string title       = 2;
  string body        = 3;
  string icon        = 4;
  string accent      = 5;
  NotificationKind kind = 6;
  string dedupe_key  = 7;
  int64  at_unix_ms  = 8;
}

enum NotificationKind { INFO = 0; PROGRESS = 1; SUCCESS = 2; WARNING = 3; URGENT = 4; }
```

Client receives bannersand renders iOS-style top-of-screen pills (blur, rounded, auto-dismiss 3s). On mobile app builds (Phase 5) this maps to native OS notifications.

### 10.4 Policy on the bundle side

`NotificationPolicy` on `VerbBinding` (§ 8.1) is advisory:
- `None` — verb does not fire `IStatusSynapse`.
- `OnComplete` — verb handler fires one `IStatusSynapse` on completion.
- `OnStatusStream` — verb handler fires `IStatusSynapse` repeatedly as status updates arrive (e.g. driver ETA, courier location).

The Notifier doesn't enforce the policy; it just listens. Policy is declarative documentation that the bundle author read the contract.

### 10.5 Dedupe

`DedupeKey` collapses update notifications to the same subject. *"Ali is 5 min away"* → *"Ali is 4 min away"* → *"Ali has arrived"* all share `dedupe_key="uber:ride:R-2a4"`. The client-side banner stack shows one slot per key; the body and icon update in-place.

## 11. `StreamExperienceEvents` — card bytes to the RFW zone

Third stream: the RFW card side. The POC reuses the existing pattern from `docs/superpowers/specs/2026-04-10-flutter-grpc-persona-design.md` § Proto contract → `SkillUIRequest/SkillUIResponse`, adapted to server-streaming per verb:

```protobuf
service Experiences {
  rpc StreamExperienceEvents(ExpSubscription) returns (stream ExperienceEvent);
}

message ExperienceEvent {
  string correlation_id = 1;
  string verb           = 2;      // e.g. "Uber.Contracts.CallRide"
  oneof payload {
    RfwBundle rfw       = 3;      // replace/update card
    RfwUpdate rfw_patch = 4;      // partial update
    VerbDone done       = 5;      // remove card or leave pinned
  }
}

message RfwBundle { bytes description = 1; bytes data = 2; }
message RfwUpdate { bytes data_patch = 1; }   // new rfw data only, desc unchanged
message VerbDone  { bool  success    = 1; string summary = 2; }
```

Bundles push `ExperienceEvent`s from inside their verb handlers via an injected `IExperienceEventEmitter`:

```csharp
public interface IExperienceEventEmitter
{
    Task EmitRfwAsync(string verb, RfwBundle bundle, NeuronContext ctx, CancellationToken ct);
    Task EmitPatchAsync(string verb, RfwUpdate patch, NeuronContext ctx, CancellationToken ct);
    Task EmitDoneAsync(string verb, VerbDone done, NeuronContext ctx, CancellationToken ct);
}
```

The emitter is a singleton on the experiences silo; it writes onto the per-user `IExperienceStream` grain and the gRPC `StreamExperienceEvents` pulls from that stream. Orthogonal to the persona/notification streams — the client wires all three into one screen and reconciles.

## 12. Client — `POC/clients/ino.flutter/`

Greenfield Flutter inside POC. Web-first (to run against the Phase 2 AppHost with zero native build setup); Android/iOS/desktop added in Phase 5.

### 12.1 Layout

```
POC/clients/
└── ino.flutter/
    ├── pubspec.yaml
    ├── protos/
    │   ├── persona.proto
    │   ├── experiences.proto
    │   └── notifications.proto
    ├── lib/
    │   ├── main.dart
    │   ├── app.dart                 // MaterialApp, theme, GoRouter
    │   ├── grpc/
    │   │   ├── generated/
    │   │   └── ino_client.dart      // GrpcOrGrpcWebClientChannel wrapper
    │   ├── persona/
    │   │   ├── persona_widget.dart  // RiveWidget + PersonaViewModel binding
    │   │   └── persona_bloc.dart    // gRPC frame stream → Rive VM writes
    │   ├── experience/
    │   │   ├── experience_bloc.dart
    │   │   ├── rfw_runtime.dart     // registered components
    │   │   └── components/          // card widgets
    │   ├── notifications/
    │   │   ├── notifier_widget.dart // iOS-style banner stack
    │   │   └── notifier_bloc.dart
    │   ├── screens/
    │   │   └── home/
    │   │       └── home_screen.dart // persona + RFW zone + banner overlay
    │   └── theme/
    ├── assets/
    │   └── rive/
    │       └── ino-persona.riv      // single authored asset
    └── test/
```

### 12.2 The three BLoCs

| BLoC | Input | Output |
|---|---|---|
| `PersonaBloc` | `StreamPersonaState` frames | applies each non-null field to the Rive ViewModel |
| `ExperienceBloc` | `StreamExperienceEvents` | maintains the active card; replaces/patches/removes |
| `NotifierBloc` | `StreamNotifications` | maintains an ordered banner stack keyed by `dedupe_key` |

Each BLoC owns one gRPC stream subscription. No shared state between them; their outputs co-exist on screen.

### 12.3 Rive VM binding

```dart
class _PersonaWidgetState extends State<PersonaWidget> {
  late final rive.RiveWidgetController _controller;
  late final rive.ViewModelInstance _vm;

  @override
  void initState() {
    super.initState();
    // ...load ino-persona.riv, get default ViewModel instance...
  }

  void _apply(PersonaFrame f) {
    if (f.hasMood())        _vm.number("mood").value        = f.mood;
    if (f.hasEnergy())      _vm.number("energy").value      = f.energy;
    if (f.hasSignalPulse()) _vm.number("signal_pulse").value = f.signalPulse;
    if (f.hasBodyPose())    _vm.string("body_pose").value   = f.bodyPose;
    if (f.hasArms())        _vm.string("arms").value        = f.arms;
    // ...
    switch (f.trigger) {
      case PersonaTrigger.ON_CELEBRATE: _vm.trigger("onCelebrate").fire(); break;
      case PersonaTrigger.ON_ARRIVE:    _vm.trigger("onArrive").fire(); break;
      case PersonaTrigger.ON_ERROR:     _vm.trigger("onError").fire(); break;
      default: break;
    }
  }
}
```

### 12.4 Screen composition

```
┌──────────────────────────────────────┐
│  [ NotifierStack — overlay top-0 ]   │
│                                      │
│  ┌──────────────────────────────┐    │
│  │      PersonaWidget (Rive)    │    │ fixed-size, top of body
│  └──────────────────────────────┘    │
│                                      │
│  ┌──────────────────────────────┐    │
│  │   Active experience card     │    │ expands to content
│  │   (RFW, streaming)           │    │
│  └──────────────────────────────┘    │
│                                      │
│  [ input row — text / voice ]        │
└──────────────────────────────────────┘
```

The prototypes at `POC/docs/prototypes/` show this composition rendered.

## 13. `PersonaEvolver` — L1 mapping-script generation

### 13.1 When it fires

Two triggers:
1. An `IExperience` registration includes a verb with no entry in its `IExperienceManifest`.
2. An entry maps to a mime symbol the persona asset doesn't expose.

Both produce a `MimeMappingMissing` reactive synapse carrying `(BundleId, Type verbType, string? proposedMime, string? reason)`.

### 13.2 What it does

```
1. Reacts to MimeMappingMissing.
2. Calls Claude via IChatClient with a small prompt:
   - full list of known mime symbols (from the Rive asset manifest)
   - the verb's synapse type name + summary of its payload
   - the bundle's domain (transport / music / messaging / ...)
   - "which mime best fits this verb? answer with one symbol from the list."
3. Validates the answer against the known-mimes list.
4. Persists a PatchedVerbBinding override in a small system-silo state grain
   (MimeOverrideStore — Orleans IPersistentState).
5. IPersonaNeuron's verb→mime lookup consults the override store first,
   manifest second. Next fire of the verb uses the generated mime.
```

### 13.3 Failure modes + fallback

- Claude returns an unknown symbol → `thinking_nod` (default fallback) + error logged.
- Claude unavailable → default fallback + retry on next verb fire, with exponential backoff.
- Multiple competing mappings for the same verb → first-writer-wins; the override survives silo restart. Human can edit `~/.ino/mime-overrides.json` (serialized mirror of `MimeOverrideStore`) to correct.

### 13.4 Why this is L1 not L3

This doesn't add code, doesn't restart silos, doesn't change the Rive asset. It writes a tiny row to a grain. Matches the Phase 1 L1 criterion: ~10ms grain write, no silo restart. Same shape as neuron creation — the persona's expression vocabulary is a *neuron-like* resource that grows by grain writes, not by build/deploy.

## 14. Claude's role — design-time vs runtime

| Timing | What Claude does | Rive asset touched? |
|---|---|---|
| Design time (authoring sprint) | Inside the Rive Editor via the in-editor AI Coding Agent: scripts particle behavior, blend states, ViewModel property wiring. Accelerates the atom-authoring pass. | Yes — atoms, VM surface, scripts inside the artboard |
| Install time (bundle registration) | Fills in missing `VerbBinding` entries via `PersonaEvolver`. One-shot per verb. | No |
| Runtime (every user interaction) | Drives intent recognition + verb selection (existing ino loop). Does **not** choose mimes — that's a pure lookup on the experience manifest + overrides. | No |

This is the strict boundary. The Rive asset is a design-time artifact; Claude never emits Rive binary at runtime. Everything dynamic flows through VM writes and mapping overrides.

## 15. Risks to verify via Context7 during implementation

1. **Rive Flutter 0.14.5 Data Binding API surface** — confirm `RiveWidgetController.viewModelInstance` (or equivalent) is stable; verify number/string/trigger property write semantics; confirm `optional` proto fields translate cleanly to Dart nil-guards.
2. **Rive Data Binding parametric blending** — verify continuous number properties actually drive blend weights (not just discrete state triggers). Mock the artboard if the runtime API requires.
3. **gRPC-Web server-streaming in the POC browser context** — Phase 2 spec (§ 4.4) already defers Flutter gRPC to this spec; confirm the POC AppHost can expose gRPC-Web alongside the marketplace HTTP endpoints on the system silo.
4. **`IPersonaNeuron` lifecycle across client reconnects** — Orleans grain stays alive; Flutter reconnect should pick up from the current frame snapshot (send a synthetic "full-state" frame on each new subscription).
5. **Notifier Reactive dispatch through the `IStatusSynapse` marker** — Phase 2's `IDiscovery.LookupReactiveAsync` indexes by exact `Type`. Reactive-on-interface is a new shape: either (a) extend Phase-2 Discovery to walk declared interfaces of each fired synapse type and return every reactive target whose `SynapseType` is assignable-from the fired type, or (b) require experience bundles to re-fire a concrete `StatusBanner` synapse and let Notifier listen on that. Option (a) preserves the simple bundle API shown in § 10.1; option (b) keeps Discovery exact-type. Pick during implementation; default to (a).
6. **`IExperience.Manifest` default interface method + serializer** — verify Orleans' source generator for `[GenerateSerializer]` handles manifests carrying `Type` keys.

Each risk becomes an explicit Context7 verification task in the implementation plan.

### 15.1 Rive ecosystem reality-check (verified 2026-04-16)

- Rive MCP integration explicitly **deprecated** by Rive; docs redirect users to the in-editor AI Agent. Spec's 2026-04-11 plan around the MCP server is replaced by this spec.
- Rive AI Agent runs inside the Rive Editor; requires Cadet/Voyager/Enterprise plan. No external API.
- Rive Scripting is live and first-party; scripts live inside `.riv` files and are authored in-editor.
- Rive Data Binding with ViewModels supports numbers, booleans, strings, colors, triggers, enums, lists, VM references, across all runtimes including Flutter.
- `.riv` binary format has public readers in Dart/C++ but no public writer; direct LLM emission of binary is impractical. This spec relies on neither.

## 16. Test strategy

### 16.1 Layers

| Layer | Project | Scope | Speed target |
|---|---|---|---|
| **L1** | `Ino.Core.Tests` (extend) | `VerbBinding` immutability, `IStatusSynapse` interface default values, manifest validation | <5s |
| **L2** | `Ino.Persona.Tests` (new) | `IPersonaNeuron` mime lookup, `PersonaFrame` derivation from verb types, override-store precedence | <30s |
| **L2** | `Ino.Notifier.Tests` (new) | `NotifierNeuron` reactive dispatch; `DedupeKey` collapsing; `IStatusSynapse` marker resolution | <30s |
| **L3** | `Ino.Hosting.Tests` (extend) | Multi-silo — experiences fires `VerbStarted`, system-silo `NotifierNeuron` receives `IStatusSynapse`, client-stream observer sees frames/banners | <60s |
| **L5** | `Ino.E2E.Tests` (extend) | Install Uber bundle via marketplace HTTP, run `Chat`-equivalent, verify gRPC Persona + Notifications + Experiences streams emit the expected events | ~3 min |
| **L6 (Flutter)** | `ino.flutter/test/` | Widget tests: PersonaWidget VM application; NotifierStack dedupe; ExperienceCard rfw render | <20s |

### 16.2 Scenario spine (new ones on top of Phase 2's 16)

| # | Scenario | Proves | Layer |
|---|---|---|---|
| 17 | `UberManifest.Verbs[CallRide]` maps to `reach_phone`; `PersonaFrame` emits `arms=hold_prop · body_pose=lean_in` | Manifest plumbing | L2 |
| 18 | Verb handler fires `RideStatusChanged` three times with the same `DedupeKey`; `NotifierBloc` sees one banner slot updated three times | Dedupe semantics | L6 |
| 19 | Bundle installed with a verb missing from its manifest → `PersonaEvolver` fires → Claude mock returns `scan_horizon` → second verb-fire uses the override | L1 mapping-script loop end-to-end | L3 |
| 20 | Concurrent verbs on same user (`CallRide` + `Navigate`) → `PersonaNeuron` emits interleaved frames keyed by correlation; client replays in order | Concurrent verbs + persona | L3 |
| 21 | Rive asset missing `arms=hold_prop` atom → L6 widget test logs warning + falls back gracefully | Asset-drift tolerance | L6 |
| 22 | Flutter reconnect mid-stream → next `StreamPersonaState` delivers a full-state frame (not a diff) | Reconnect semantics | L5 |

## 17. Project layout (additions to POC)

```
POC/
├── src/
│   ├── Ino.Core/                       (existing)
│   │   ├── IStatusSynapse.cs           NEW
│   │   └── NotificationKind.cs         NEW
│   │
│   ├── Ino.Core.Hosting/               (existing; extended for IExperienceManifest, VerbBinding, NotificationPolicy)
│   │
│   ├── Ino.System/                     (Phase 2)
│   │   └── NotifierNeuron.cs           NEW
│   │
│   ├── Ino.Experiences/                (Phase 2; extended with IPersonaNeuron + hooks)
│   │   ├── IPersonaNeuron.cs           NEW
│   │   ├── PersonaNeuron.cs            NEW
│   │   ├── VerbStarted.cs              NEW
│   │   ├── VerbCompleted.cs            NEW
│   │   └── MimeOverrideStore.cs        NEW
│   │
│   ├── Ino.PersonaEvolver/             NEW  L1 mapping-script generator neuron
│   │   ├── PersonaEvolverNeuron.cs
│   │   └── MimeMappingMissing.cs
│   │
│   ├── Ino.Gateways.Grpc/              NEW  gRPC service hosting on system silo ASP.NET
│   │   ├── Protos/
│   │   │   ├── persona.proto
│   │   │   ├── experiences.proto
│   │   │   └── notifications.proto
│   │   └── Services/
│   │       ├── PersonaService.cs
│   │       ├── ExperiencesService.cs
│   │       └── NotificationsService.cs
│   │
│   └── Ino.Experiences.* (Uber, Spotify, Gmail, WhatsApp, Amazon, UberEats, GoogleMaps, Revolut, Weather, Calendar)
│       NEW  ten IExperience bundles with contracts + handlers + manifests
│       (each bundle ships <Name> + <Name>.Contracts projects, matching Phase 2 § 6.2)
│
├── clients/
│   └── ino.flutter/                    NEW  (§ 12)
│
├── docs/
│   ├── prototypes/
│   │   ├── 01-taxi-flow.html           (committed; hero reference)
│   │   └── 02-experience-catalog.html  (committed; pattern reference)
│   └── persona-authoring.md            NEW  one-pager on authoring atoms in Rive Editor
│
└── test/
    ├── Ino.Core.Tests/                 (extended — L1)
    ├── Ino.Persona.Tests/              NEW  (L2)
    ├── Ino.Notifier.Tests/             NEW  (L2)
    ├── Ino.Hosting.Tests/              (extended — L3)
    └── Ino.E2E.Tests/                  (extended — L5)
```

## 18. Relationship to Phase 2 and later phases

- **Phase 2 stays unchanged** except for two additive extensions: `IExperience.Manifest` default method, and `IStatusSynapse` marker in `Ino.Core`. Everything else builds *on top of* Phase 2's cross-silo runtime.
- **Phase 3 (analyzer + source generator)** — `IExperienceManifest.Verbs` is a natural candidate for source-gen from `INeuron<T>` discovery. Manifest authoring becomes attribute-free boilerplate elimination.
- **Phase 4** — Rive Scripting for prop-layer animations (phone, map fragment, stars, music bars spawned inside the artboard via script), plus the domain-affinity visual morphing that got deferred here.
- **Phase 5** — Voice input (speech → verb), TTS (persona speaks), native iOS/Android app wrappers for real OS notifications.
- **Phase 6** — Brain View, Timeline, Branch screens (from the 2026-04-11 spec) layered on top of the persona + experience streams already flowing.

Every later phase is additive. The VM surface (§ 6.3) is the persona's durable API — a multi-year artifact — and the mime vocabulary (§ 7.1) is the first stable product primitive above the Phase 2 runtime.

---

## Appendix A — Example taxi flow timeline

Annotated with the synapse / VM-write events that produce the demo in `POC/docs/prototypes/01-taxi-flow.html`.

```
t=0.00s  user: "get me to sfo"
          client → gRPC Chat(...)
          → SynapseRouter selects Uber bundle, picks CallRide verb

t=0.02s  fire VerbStarted(CallRide, corr=C-001)
          → PersonaNeuron reacts: manifest lookup → reach_phone →
            PersonaFrame(
              body_pose=lean_in, arms=hold_prop, eyes=down,
              current_verb="Uber.CallRide", current_experience="Uber",
              accent="#000000", prop_visible=true, prop_kind="phone")
          → PersonaBloc applies writes to Rive VM
          → character raises arm, phone fades in, body leans

t=0.04s  fire CallRide(dest="SFO")
          → CallRideNeuron calls Uber API

t=0.10s  CallRideNeuron emits ExperienceEvent(rfw=RideCard initial)
          → client renders card skeleton below persona

t=0.24s  CallRideNeuron emits ExperienceEvent(rfw_patch={driver, car, plate})
          → card populates live

t=0.25s  CallRideNeuron fires reactive RideStatusChanged(
            Title="your ride", Body="Ali is 5 min away",
            Icon="🚕", Accent="#000", Kind=Progress,
            DedupeKey="uber:ride:R-2a4")
          → NotifierNeuron reacts → NotificationsService pushes banner
          → client shows iOS-style top banner, auto-dismiss 3s

t=0.50s  fire VerbCompleted(CallRide, corr=C-001, success)
          → PersonaFrame(trigger=ON_CELEBRATE, body_pose=neutral)
          → character small celebration, returns to neutral

t=3.00s  (banner dismisses)
t=60s+   CallRideNeuron fires periodic RideStatusChanged (same DedupeKey)
          → banner slot updates in-place as ETA decreases
```

Every line is a concrete gRPC or synapse event the spec covers.

## Appendix B — Why the 15-mime vocabulary is load-bearing

Enumerating 100 apps × 5 verbs = 500 verbs. Naïve design: 500 animations. Actual shape: 15 gestural archetypes cover them all because verbs differ in *object* (ride / money / message) not in *gesture* (reach / pay / write). The object lives in RFW cards and notification text; the gesture lives in the persona.

This is the same reason the language has ~2000 verbs but the body has ~15 root gestures. ino's persona inherits that asymmetry.

## Appendix C — Prior-art lineage

This spec supersedes (inside POC scope):
- `2026-04-10-flutter-grpc-persona-design.md` — keeps its gRPC + BLoC patterns; drops its enumerated-state model in favor of parametric VM.
- `2026-04-11-ino-200-domains-persona-design.md` — keeps the verb-rich product vision; drops Rive MCP as deprecated; defers multi-persona library to a later phase.

Both specs remain the authoritative record of ino's older design path in `src/`. The POC starts fresh from the Phase 2 substrate and this spec.
