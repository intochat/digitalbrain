# Domain / Experience vocabulary — design spec

**Date.** 2026-04-23
**Track.** 1 of 7 (opensource-readiness roadmap — see the "Nine-track scope map" conversation 2026-04-23).
**Status.** Design approved; implementation plan pending.
**Related.**
- `docs/product-vision-final.md` — Decision 6 (Cortex), Decision 8 (two vocabularies), Decision 13 (Travel neurons), Decision 14 (marketplace).
- `docs/plan-poc-phase-3.md` — Slice 14 (marketplace tile).
- GitHub issue #15 — evaluated and rejected in the same session; not a follow-up.

---

## Mission

ino's current codebase uses the word "experience" in two contradictory senses — sometimes a whole installable bundle (`Ino.Experiences.Travel`, `IExperienceRestartService`), sometimes a single user-verb (marketplace drill-in copy, `.feature` scenarios). The two meanings collide most visibly in the POC's own comments ("The Travel experience bundle ships with the FlightSearch neuron"), and they'll collide louder once `SelfImproving` ships 50 verbs inside one bundle.

This spec locks one vocabulary:

- **Domain** = installable bundle of neurons + synapses + contracts. `IDomain`.
- **Experience** = one user-verb that a domain offers. `IExperience`.
- **Bundle** = deleted as a concept. `BundleId` → `DomainId`.

It also introduces `IExperience` as a first-class, metadata-rich type that Cortex routes to, the marketplace renders, and BDD scenarios align with. It does *not* change Cortex's default routing path, does not add remote-catalog behaviour, does not change the install unit, and does not ship the SelfImproving domain — those are later tracks.

---

## Vocabulary rule

End users see **domain names** as brand ("Travel noticed your flight was delayed") — `product-vision-final.md` Decision 8 is preserved verbatim.

End users see **experience names** in three surfaces only:

1. Marketplace drill-in — *"Travel · 6 experiences: Plan a trip, Find flights, …"*.
2. Cortex navigation trail — *"Travel · plan-a-trip → ItineraryComposer → …"* (the opensource demo moment from Track 3).
3. Creator drawer / inspector — builders already speak the creator vocabulary.

Creators see **neurons + synapses** as before — no change to creator vocabulary.

The word "bundle" is removed from docs wherever it meant "domain". It survives only in historical commit messages and Phase 2 design notes that are frozen.

---

## Core contracts

All new / modified types live in `Ino.Core` and `Ino.Core.Hosting`.

### Identifier types (Ino.Core)

```csharp
public readonly record struct DomainId(string Value)
{
    public static DomainId From(string value) => new(value);
    public override string ToString() => Value;
}

public readonly record struct ExperienceId(string Value)
{
    public static ExperienceId From(string value) => new(value);
    public override string ToString() => Value;
}
```

- `DomainId` replaces `BundleId` 1:1 — same string shape (`"Ino.Domains.Travel"`), same JSON converter pattern (`DomainIdJsonConverter`), same install-URL surface.
- `ExperienceId` convention: `"{short-domain}.{verb}"` — e.g. `travel.plan-trip`, `travel.find-flights`, `self-improving.commit`. Short prefix keeps trails readable.

### `IDomain` (Ino.Core.Hosting)

```csharp
public interface IDomain
{
    DomainId Id { get; }                                      // renamed from Bundle
    string Version { get; }
    IReadOnlyList<Capability> DeclaredCapabilities { get; }
    IReadOnlyList<IExperience> DeclaredExperiences { get; }   // NEW
    IReadOnlyDictionary<Type, IReadOnlyList<Capability>> PerGrainCapabilities
        => ImmutableDictionary<Type, IReadOnlyList<Capability>>.Empty;
}
```

`DeclaredCapabilities` stays on `IDomain` — it describes whole-domain requirements (LLM tier, etc.). It does **not** move to `IExperience`. Per-experience capability declaration is YAGNI for v0.1.

### `IExperience` (Ino.Core.Hosting)

```csharp
public interface IExperience
{
    ExperienceId Id { get; }
    string DisplayName { get; }
    string Description { get; }
    Type CanonicalSynapseType { get; }
    IReadOnlyList<string> PromptExamples { get; }
}

public sealed record Experience(
    ExperienceId Id,
    string DisplayName,
    string Description,
    Type CanonicalSynapseType,
    IReadOnlyList<string> PromptExamples) : IExperience;
```

The record default covers 99% of declarations (including all 50 SelfImproving verbs). Domains needing behaviour on an experience (dynamic prompt examples, lazy-loaded metadata) implement `IExperience` directly.

### Example: Travel domain under the new shape

```csharp
public sealed class Travel : IDomain
{
    public DomainId Id => DomainId.From("Ino.Domains.Travel");
    public string Version => "0.1.0";
    public IReadOnlyList<Capability> DeclaredCapabilities => [new Capability.Llm(LlmTier.Default)];

    public IReadOnlyList<IExperience> DeclaredExperiences =>
    [
        new Experience(
            ExperienceId.From("travel.plan-trip"),
            DisplayName: "Plan a trip",
            Description: "Build an itinerary with flights, hotels, and things to do.",
            CanonicalSynapseType: typeof(PlanTripRequest),
            PromptExamples: [
                "plan a trip to bali",
                "help me plan 5 days in tokyo",
                "i want to visit lisbon next month"
            ]),
        new Experience(
            ExperienceId.From("travel.find-flights"),
            DisplayName: "Find flights",
            Description: "Search flights ranked by your learned preferences.",
            CanonicalSynapseType: typeof(FlightSearchRequest),
            PromptExamples: [
                "find flights to bali",
                "cheapest flight from berlin to tokyo"
            ]),
        // HotelSearch, PlaceSearch, RestaurantSearch, TransportPlanner — Decision 13.
    ];
}
```

---

## Rename map

### Folders + csprojs

| Before | After |
|---|---|
| `POC/experiences/` | `POC/domains/` |
| `POC/experiences/travel/Ino.Experiences.Travel/` | `POC/domains/travel/Ino.Domains.Travel/` |
| `POC/experiences/travel/Ino.Experiences.Travel.Contracts/` | `POC/domains/travel/Ino.Domains.Travel.Contracts/` |
| `POC/experiences/taxi/Ino.Experiences.Taxi/` | `POC/domains/taxi/Ino.Domains.Taxi/` |
| `POC/experiences/testing/Ino.Testing.Fixture.{Alpha,Beta,Gamma,Delta}{,.Contracts}/` | `POC/domains/testing/Ino.Testing.Fixture.{Alpha,Beta,Gamma,Delta}{,.Contracts}/` |
| `POC/src/Ino.Experiences/` | `POC/src/Ino.Domains/` |
| `POC/src/Ino.Experiences.Host/` | `POC/src/Ino.Domains.Host/` |

### Namespaces

One-to-one with folders. `Ino.Experiences.Travel` → `Ino.Domains.Travel`; same for Taxi, Testing fixtures, package assembly (`Ino.Experiences` → `Ino.Domains`), host assembly (`Ino.Experiences.Host` → `Ino.Domains.Host`).

### Core contract types

| Before | After |
|---|---|
| `BundleId` | `DomainId` |
| `BundleIdJsonConverter` | `DomainIdJsonConverter` |
| `IDomain.Bundle` (property) | `IDomain.Id` |
| *(new)* | `IExperience`, `Experience` record |
| *(new)* | `ExperienceId`, `ExperienceIdJsonConverter` |
| `CanonicalRegistration.Bundle` | `CanonicalRegistration.Domain` |
| `ReactiveRegistration.Bundle` | `ReactiveRegistration.Domain` |

### Infrastructure types

| Before | After |
|---|---|
| `IExperienceRestartService` / `NullExperienceRestartService` / `ExperienceRestartService` | `IDomainRestartService` / `NullDomainRestartService` / `DomainRestartService` |
| `ExperienceRegistrar` | `DomainRegistrar` |
| `RegistrationHostedService` | unchanged (name already generic) |
| `WithExperience<T>()` extension | `WithDomain<T>()` |
| `IInoBuilder.RegisteredExperiences` / `RegisterExperience()` | `RegisteredDomains` / `RegisterDomain()` |
| `FakeExperienceRestartService` (tests) | `FakeDomainRestartService` |
| `ExperiencesSiloConfigurator` | `DomainsSiloConfigurator` |

### Marketplace feed (breaking shape change)

| Before | After |
|---|---|
| `MarketplaceFeed.Experiences` (property) | `MarketplaceFeed.Domains` |
| `MarketplaceFeedEntry.Id` is `BundleId` | `MarketplaceFeedEntry.Id` is `DomainId` |
| `GET /marketplace/installed` returns `{ installed: string[] }` | unchanged — values are `DomainId`s |
| *(new)* | `GET /marketplace/installed/{domainId}/experiences` — returns `IDomain.DeclaredExperiences` for the named installed domain |

### Aspire resource display names

The `experiences` resource in `Ino.AppHost` renames to `domains`. Aspire dashboard shows *"domains"*. `mcp__aspire__execute_resource_command(resourceName="domains", …)` replaces today's `"experiences"`. `CLAUDE.md` gets a one-line update in the "To restart individual resources" section.

### Docs sweep

`README.md`, `POC/README.md`, `CLAUDE.md`, `docs/product-vision-final.md`, `docs/plan-poc-phase-3.md` — rewritten to use the §1 vocabulary rule. "Experience" means user-verb exclusively. "Bundle" removed where it meant "domain".

### What does NOT rename

`Neuron`, `Synapse`, `INeuron<>`, `IReactsTo<>`, `IFirePort`, `NeuronContext`, `SynapseFired`, `Discovery`, `Cortex`, all telemetry / activity-source names, all RFW card template names, all Flutter-side widget names, all existing `.feature` files. The primitives don't move; only the container's name.

---

## Ripple effects

### Cortex routing

Today: `Cortex : Neuron<ChatIntent>` asks `IDiscovery.DumpAsync()` for all canonical synapse types, keyword-matches the chat text against the synapse `Type.Name`, fires.

Under the new contract:

1. `IDiscovery` gains `DumpExperiencesAsync()` — parallel to `DumpAsync()`, zero breakage. Backed by `DomainRegistrar` aggregating `IDomain.DeclaredExperiences` across installed domains.
2. Cortex scores each `IExperience` against the chat text on `(DisplayName, Description, PromptExamples)`. `BddMockChatClient` already does scenario matching on `.feature` examples — same matching logic reused.
3. On match, Cortex records `ExperienceId` + matched prompt example in the reasoning probe. Inspector's Reasoning panel reads `Experience: travel.plan-trip` alongside the existing `Scenario` / `Feature` / `ReasoningSource` metadata.
4. On match, Cortex fires `experience.CanonicalSynapseType` via `IFirePort` (same fire path as today).
5. On no match, fall through to today's keyword-match path — prevents regressions in existing tests that assert the direct synapse-fire route.

Cortex stays one neuron in `system` silo (Decision 6 preserved). No per-domain `<Domain>Cortex`.

### Marketplace

- `GET /marketplace/available` — feed JSON re-shapes:
  ```json
  {
    "domains": [
      {
        "id": "Ino.Domains.Travel",
        "version": "0.1.0",
        "experiences": [
          { "id": "travel.plan-trip", "displayName": "Plan a trip", "description": "..." }
        ]
      }
    ]
  }
  ```
  Experiences in the feed are metadata-only — no `CanonicalSynapseType`.
- `GET /marketplace/installed` — wire format unchanged.
- **NEW:** `GET /marketplace/installed/{domainId}/experiences` — returns live `IDomain.DeclaredExperiences` of an installed domain. Consumer is Track 3's Cortex navigation UI.
- Install unit stays the whole domain.
- `~/.ino/installed.json` stays `string[]` of `DomainId`s — file format unchanged. A previously-installed `installed.json` written under the old `BundleId` naming still reads correctly (values are strings either way).

### BDD scenarios as source of truth for prompt examples

Convention: a `.feature` file under a domain declares scenarios whose `Given a user says "…"` steps *are* the experience's `PromptExamples`. A domain's `DeclaredExperiences` list cross-references by feature + scenario tag. Example:

```csharp
new Experience(
    ExperienceId.From("travel.plan-trip"),
    // …
    PromptExamples: BddPromptExamples.From(
        feature: "PlanTripRoute",
        scenario: "user asks for a multi-day itinerary"))
```

`BddPromptExamples.From` is a new helper in `Ino.Llm` that loads the named scenario at construction time and exposes its example strings. One source of truth (the `.feature`), two readers (BDD test + Cortex routing).

**If `Ino.Llm` doesn't already expose a scenario-loader surface by the time Track 1 ships:** the helper is a follow-up. Track 1 declares `PromptExamples` as inline `string[]` in every domain and schedules the helper extraction as a post-merge task.

### Telemetry

- New tags on existing activity sources: `domain.id`, `experience.id` — attached wherever today we attach `bundle.id` or nothing. Exporter configuration unchanged.
- `SynapseFired` envelope gains `Experience` (optional `ExperienceId`) alongside the existing `Scenario`, `Feature`, `ReasoningSource` fields. Flutter inspector reads it in the Reasoning panel — one-line addition.
- No new activity source. Metric names unchanged. No changes to OTLP exporters.

### Tests

- Every `I?Experience*` test renames per §Rename map — mechanical only.
- `IDomainTests.cs` (renamed from `IExperienceTests.cs`) picks up new assertions:
  - `DeclaredExperiences_defaults_to_empty` for minimal `IDomain` impls.
  - `DeclaredExperiences_roundtrips_from_record` to cover the `Experience` record default.
- `DomainRegistrarTests.cs` (renamed from `ExperienceRegistrarTests.cs`) adds one case: *"registrar aggregates experiences from all installed domains"*.
- Wire-shape integration tests on `/marketplace/available` flip to assert `domains` + `experiences` keys.
- `FakeExperience : IDomain` helper renames to `FakeDomain`. Shape unchanged.
- E2E tests: zero logic changes. Browser assertions are still "card renders". Wire shape to Flutter is unchanged.

### No backward-compat shims

Per the project convention (`CLAUDE.md` + `Executing actions with care`): clean break, one PR, no deprecation aliases, no parallel APIs, no type forwarders. `src/` is already deleted; nothing consumes `IExperienceRestartService` / `BundleId` / `WithExperience<T>` outside POC + tests.

### Orleans grain identity

Grain class namespaces change (`Ino.Experiences.Travel.Neurons.FlightSearchNeuron` → `Ino.Domains.Travel.Neurons.FlightSearchNeuron`), which changes Orleans' source-generated `GrainType.Name`. This is acceptable because:

- POC clusters are ephemeral (in-memory membership, no persistent grain state that pre-dates the rename PR).
- `installed.json` stores `DomainId` strings, not grain types.
- `CanonicalTarget` / `ReactiveTarget` are computed at silo startup from the installed domain set, not serialized.
- The cold-boot race fix from PR #14 (drop `grainClassNamePrefix` in `SystemFirePort` / `FirePort`) already routes by interface only, so no `Type.FullName` strings are being passed to `IGrainFactory.GetGrain`. Any that are reintroduced during the rename must use the new `Ino.Domains.*` path or — preferably — use `[Alias("…")]` on the grain class so routing survives future renames.

### `git mv` for history

Every folder rename uses `git mv`. Reviewers checking `git log --follow` on moved files see the full history. Where `git mv` + content rewrite are combined in one commit, git's rename-detection threshold stays above 50% — verified by doing the folder move first as an empty content change, then the `using` / namespace rewrite as a second commit on the same file.

---

## Migration outline

*Sequencing is finalised by `superpowers:writing-plans`. Outlined here to scope the PR and catch dependencies.*

1. **Introduce new types additively** — `DomainId`, `ExperienceId`, `IExperience`, `Experience` record alongside the existing `BundleId` / `IDomain`. `IDomain` temporarily exposes both `Bundle` and `Id` (transitional scaffolding, internal to this commit chain only) so existing tests keep passing. Step 2 removes the scaffolding before the PR lands — the merged branch contains no dual accessors, honouring the §"No backward-compat shims" rule.
2. **Rename Core types in place** — `BundleId` → `DomainId`, `IDomain.Bundle` → `IDomain.Id`. Update all call sites. Delete the transitional scaffolding from step 1.
3. **Rename infrastructure types** — `IExperienceRestartService`, `ExperienceRegistrar`, `WithExperience<T>`, etc. Update all call sites. One commit per renamed public-surface group (`feat(poc): rename ExperienceRegistrar → DomainRegistrar`) to keep the diff reviewable.
4. **`git mv` folders + namespaces** — `POC/experiences/` → `POC/domains/`. Update every csproj `<ProjectReference>`, every `using` directive, every `ino.slnx` entry.
5. **Add `DeclaredExperiences` to Travel + Taxi** — first real experience declarations; wire inline `string[]` `PromptExamples`.
6. **Wire `DumpExperiencesAsync` through `IDiscovery`** — Cortex reads but only as a fallback enrichment; no behaviour change yet.
7. **Re-shape marketplace feed + add `/installed/{domainId}/experiences`** — update `MarketplaceController` + integration tests.
8. **Docs sweep** — `CLAUDE.md`, `README.md`s, vision + plan docs.
9. **Aspire resource rename** — `experiences` → `domains` in `Ino.AppHost`; `CLAUDE.md` rebuild-commands example updates.
10. **`BddPromptExamples.From` helper** — if `Ino.Llm` exposes a scenario loader, land it and swap Travel inline strings over; otherwise flag as a follow-up.

Verification loop per `CLAUDE.md` after each commit:
- `dotnet build POC/ino.slnx`
- `dotnet test POC/ino.slnx`
- `aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated` and confirm every resource Healthy
- Flutter web smoke — open the system-silo HTTPS URL, onboarding renders, Travel demo routes.
- E2E — `INO_E2E_NO_BROWSER=true dotnet test POC/test/Ino.E2E.Tests --filter "Category=E2E"`.

---

## Out of scope

*If a reviewer suggests adding any of these, defer with a link to the named track.*

- **Track 2 (opensource onboarding)** — per-clone `ClusterId` GUID, `InoStarted` synapse, login flow.
- **Track 3 (live Cortex nav UI)** — Flutter trail widget, per-hop navigation event emission, inspector consumer of `DumpExperiencesAsync`.
- **Track 4 (InoCloud)** — remote marketplace feed, Cortex-routes-to-uninstalled-domain install prompt, developer submission.
- **Track 5 (SystemDomain + L1/L2/L3)** — `Ino.Domains.System` bundle, self-creation primitives.
- **Track 6 (SelfImproving domain)** — the domain itself + 50 dev verbs + git/MCP wiring + BDD coverage.
- **Track 7 (telemetry-testable BDD)** — `CommunityToolkit.Aspire.Hosting.OpenTelemetryCollector`, OTel-assertion test fixtures.
- **Track 9 (repo-wide quality pass)** — not a separate feature, follows after Tracks 1–7 inform what needs cleaning.
- **Per-experience install/uninstall** — contract allows it (stable `ExperienceId`); v0.1 marketplace installs whole domains only.
- **Issue #15 (`Microsoft.Orleans.Serialization.Protobuf`)** — evaluated and rejected. Not a follow-up.

---

## Acceptance criteria for Track 1 PR

The PR is ready to merge when:

- [ ] `dotnet build POC/ino.slnx` — green.
- [ ] `dotnet test POC/ino.slnx` — all tests renamed and passing, plus the two new `IDomainTests` cases and the new `DomainRegistrarTests` aggregation case.
- [ ] `aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated` — all three silos (`system`, `identity`, `domains`) Healthy.
- [ ] System-silo HTTPS URL — Flutter onboarding renders, `Run Travel` demo routes, zero console errors.
- [ ] `GET /marketplace/available` returns the new `{ domains: [...] }` shape; integration test asserts it.
- [ ] `GET /marketplace/installed/Ino.Domains.Travel/experiences` returns Travel's declared experiences; integration test asserts it.
- [ ] `grep -rn "BundleId\b\|WithExperience<\|ExperienceRegistrar\b\|IExperienceRestartService\b\|NullExperienceRestartService\b\|FakeExperienceRestartService\b\|ExperiencesSiloConfigurator\b\|RegisteredExperiences\b\|RegisterExperience\b\|IDomain\.Bundle\b" POC/` returns zero matches.
- [ ] `grep -rn "Ino\.Experiences\." POC/` returns zero matches.
- [ ] The only symbols starting with `Experience` in `POC/` source are `IExperience`, `Experience` (the record default), `ExperienceId`, `ExperienceIdJsonConverter`.
- [ ] Docs (`CLAUDE.md`, `README.md`s, `docs/product-vision-final.md`, `docs/plan-poc-phase-3.md`) use the §1 vocabulary rule exclusively.

No feature regression is acceptable — the PR is a rename + additive contract extension only.
