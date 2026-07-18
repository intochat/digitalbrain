# Domain / Experience Vocabulary Rename — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lock one vocabulary across the POC — *domain* (`IDomain`) is the installable bundle, *experience* (`IExperience`) is a user-verb inside a domain — by renaming the current `IExperience` interface to `IDomain`, renaming `BundleId` to `DomainId`, introducing a first-class `IExperience` user-verb contract, reshaping the marketplace feed, and sweeping folders / namespaces / infrastructure / Aspire resource names to match.

**Architecture:** Eleven sequential commits, each self-contained and verifiable. Slices 1, 2, 4, 5, 6, 10 are pure mechanical renames (grep-driven) with build + test green at commit. Slices 3, 7, 8, 9 add new types / behaviour using TDD. Slice 11 is optional — only lands if `Ino.Llm` exposes a scenario loader by the time we get there. No backward-compat shims. No feature regression acceptable.

**Tech Stack:** .NET 9, Orleans 10, ASP.NET Core, Aspire 13, xUnit + FluentAssertions, Flutter 3.41 / CanvasKit (verification only — no Flutter code changes in this PR).

**Spec:** `docs/superpowers/specs/2026-04-23-domain-experience-vocabulary-design.md`

---

## File Structure Overview

### Renamed files (git mv — preserve history)

| Before | After |
|---|---|
| `POC/src/Ino.Core.Hosting/IExperience.cs` | `POC/src/Ino.Core.Hosting/IDomain.cs` |
| `POC/src/Ino.Core/BundleId.cs` | `POC/src/Ino.Core/DomainId.cs` |
| `POC/src/Ino.Core/ExperienceMetadata.cs` | `POC/src/Ino.Core/DomainMetadata.cs` |
| `POC/src/Ino.Aspire.Hosting/BundleIdJsonConverter.cs` | `POC/src/Ino.Aspire.Hosting/DomainIdJsonConverter.cs` |
| `POC/src/Ino.Aspire.Hosting/WithExperienceExtensions.cs` | `POC/src/Ino.Aspire.Hosting/WithDomainExtensions.cs` |
| `POC/src/Ino.System/IExperienceRestartService.cs` | `POC/src/Ino.System/IDomainRestartService.cs` |
| `POC/src/Ino.System/ExperienceRestartService.cs` | `POC/src/Ino.System/DomainRestartService.cs` |
| `POC/src/Ino.System/ExperienceRegistrar.cs` | `POC/src/Ino.System/DomainRegistrar.cs` |
| `POC/src/Ino.Experiences/ExperiencesSiloConfigurator.cs` | `POC/src/Ino.Domains/DomainsSiloConfigurator.cs` |
| `POC/src/Ino.Experiences/` directory | `POC/src/Ino.Domains/` |
| `POC/src/Ino.Experiences.Host/` directory | `POC/src/Ino.Domains.Host/` |
| `POC/experiences/` directory | `POC/domains/` |
| `POC/experiences/travel/Ino.Experiences.Travel/` | `POC/domains/travel/Ino.Domains.Travel/` |
| `POC/experiences/travel/Ino.Experiences.Travel.Contracts/` | `POC/domains/travel/Ino.Domains.Travel.Contracts/` |
| `POC/experiences/taxi/Ino.Experiences.Taxi/` | `POC/domains/taxi/Ino.Domains.Taxi/` |
| `POC/experiences/taxi/Ino.Experiences.Taxi.Contracts/` | `POC/domains/taxi/Ino.Domains.Taxi.Contracts/` |
| `POC/experiences/testing/Ino.Testing.Fixture.*/` | `POC/domains/testing/Ino.Testing.Fixture.*/` |
| `POC/test/Ino.Core.Tests/IExperienceTests.cs` | `POC/test/Ino.Core.Tests/IDomainTests.cs` |
| `POC/test/Ino.Core.Tests/ExperienceMetadataTests.cs` | `POC/test/Ino.Core.Tests/DomainMetadataTests.cs` |
| `POC/test/Ino.System.Tests/ExperienceRegistrarTests.cs` | `POC/test/Ino.System.Tests/DomainRegistrarTests.cs` |
| `POC/test/Ino.System.Tests/FakeExperienceRestartService.cs` | `POC/test/Ino.System.Tests/FakeDomainRestartService.cs` |
| `POC/test/Ino.Experiences.Tests/` directory | `POC/test/Ino.Domains.Tests/` |

### Net-new files

| Path | Purpose |
|---|---|
| `POC/src/Ino.Core/ExperienceId.cs` | new user-verb identifier record struct |
| `POC/src/Ino.Aspire.Hosting/ExperienceIdJsonConverter.cs` | JSON round-trip for `ExperienceId` |
| `POC/src/Ino.Core.Hosting/IExperience.cs` | new user-verb contract (re-uses the name freed by slice 1) |
| `POC/src/Ino.Core.Hosting/Experience.cs` | record default implementation of `IExperience` |
| `POC/test/Ino.Core.Tests/ExperienceIdTests.cs` | contract tests for `ExperienceId` |
| `POC/test/Ino.Core.Tests/IExperienceTests.cs` | contract tests for the user-verb `IExperience` + `Experience` record |

### Deleted files

None. Every old type is renamed in place (or its file renamed via `git mv`), not deleted and recreated, to keep `git log --follow` useful for reviewers.

### Slices

| # | Commit type | Title |
|---|---|---|
| 1 | `refactor(poc)` | Rename `IExperience` bundle interface → `IDomain` |
| 2 | `refactor(poc)` | Rename `BundleId` → `DomainId`, `Bundle` property → `Id` |
| 3 | `feat(poc)` | Add `ExperienceId`, user-verb `IExperience`, `Experience` record, extend `IDomain` with `DeclaredExperiences` |
| 4 | `refactor(poc)` | Rename infrastructure types: `IExperienceRestartService` → `IDomainRestartService`, `ExperienceRegistrar` → `DomainRegistrar`, `WithExperience<T>` → `WithDomain<T>`, `ExperienceMetadata` → `DomainMetadata`, `ExperiencesSiloConfigurator` → `DomainsSiloConfigurator`, `RegisteredExperiences` → `RegisteredDomains` |
| 5 | `refactor(poc)` | Move `POC/experiences/` → `POC/domains/` + rename `Ino.Experiences.*` namespaces → `Ino.Domains.*` |
| 6 | `refactor(poc)` | Rename `KernelSilo.Experiences` → `KernelSilo.Domains` + Aspire resource `experiences` → `domains` |
| 7 | `feat(poc)` | Travel + Taxi declare their user-verb experiences |
| 8 | `feat(poc)` | `IDiscovery.DumpExperiencesAsync` + `DomainRegistrar` aggregation |
| 9 | `feat(poc)` | Marketplace feed reshape + `GET /marketplace/installed/{domainId}/experiences` |
| 10 | `docs` | Sweep CLAUDE.md / README.md / vision / plan docs to the §1 vocabulary rule |
| 11 | `feat(poc)` | `BddPromptExamples.From(feature, scenario)` helper (optional — only if `Ino.Llm` has a scenario loader) |

Each slice ends at a committed, green working tree with:

```bash
dotnet build POC/ino.slnx
dotnet test POC/ino.slnx
```

Aspire boot smoke once per slice (step "Slice-end verification" below) to catch resource-wiring drift early. Slices 5, 6, 9 must also pass the full verification loop (aspire start + Flutter web smoke + E2E).

### Per-slice verification block

Each slice's final verification task runs this block unless otherwise noted:

```bash
# Build + full test suite
dotnet build POC/ino.slnx
dotnet test POC/ino.slnx

# If slice touches AppHost/silos/wire shape — full boot smoke.
# If slice is core-only (1–4, 10) — skip the boot smoke, it's redundant.
aspire stop                                     # kill any prior session
aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated
# wait for all three silos Healthy in the dashboard, then:
# (via Aspire MCP) mcp__aspire__list_resources — all three show "Running + Healthy"
# Open the system silo HTTPS URL in Chrome (MCP), confirm Flutter onboarding loads with zero console errors

# E2E
INO_E2E_NO_BROWSER=true dotnet test POC/test/Ino.E2E.Tests --filter "Category=E2E"
```

Abort the slice and investigate before committing if any step fails. Never commit to mask a red build.

---

## Slice 1 — Rename `IExperience` bundle interface → `IDomain`

**Goal.** Free the name `IExperience` (currently means "a bundle") so slice 3 can reintroduce it with the user-verb meaning. Pure mechanical rename of the type + the test that names it. No behaviour change, no new types.

**Files:**

- Rename: `POC/src/Ino.Core.Hosting/IExperience.cs` → `POC/src/Ino.Core.Hosting/IDomain.cs`
- Rename: `POC/test/Ino.Core.Tests/IExperienceTests.cs` → `POC/test/Ino.Core.Tests/IDomainTests.cs`
- Modify (type references): `POC/experiences/travel/Ino.Experiences.Travel/Travel.cs`, `POC/experiences/taxi/Ino.Experiences.Taxi/Taxi.cs`, `POC/experiences/testing/Ino.Testing.Fixture.{Alpha,Beta,Gamma,Delta}/*.cs`, `POC/src/Ino.Aspire.Hosting/IInoBuilder.cs`, `POC/src/Ino.Aspire.Hosting/InoBuilder.cs`, `POC/src/Ino.Aspire.Hosting/WithExperienceExtensions.cs`, `POC/src/Ino.Experiences/ExperiencesSiloConfigurator.cs`, `POC/src/Ino.System/ExperienceRegistrar.cs`, `POC/src/Ino.System/RegistrationOptions.cs`, `POC/src/Ino.Experiences.Host/Program.cs`, `POC/test/Ino.System.Tests/ExperienceRegistrarTests.cs`

### Steps

- [ ] **Step 1.1 — Git-mv the interface file and rewrite the type name**

```bash
git mv POC/src/Ino.Core.Hosting/IExperience.cs POC/src/Ino.Core.Hosting/IDomain.cs
```

Then edit `POC/src/Ino.Core.Hosting/IDomain.cs` so its body reads:

```csharp
using System.Collections.Immutable;
using Ino.Core;

namespace Ino.Core.Hosting;

public interface IDomain
{
    BundleId Bundle { get; }
    string Version { get; }
    IReadOnlyList<Capability> DeclaredCapabilities { get; }

    // Optional — bundles without per-grain detail return an empty dictionary.
    // Phase 2 enforcement is bundle-level; Phase 3 source gen may populate this automatically.
    IReadOnlyDictionary<Type, IReadOnlyList<Capability>> PerGrainCapabilities
        => ImmutableDictionary<Type, IReadOnlyList<Capability>>.Empty;
}
```

(Slice 2 renames `Bundle`/`BundleId`. Don't touch those words in this slice.)

- [ ] **Step 1.2 — Rewrite the interface name at every call site**

```bash
# Replace the exact token "IExperience" (word-boundary-aware) with "IDomain" across POC sources.
# Windows note: if `grep -rlZ ... | xargs -0 sed -i` isn't available in your shell, use a PowerShell
# equivalent or an editor multi-file replace. On Linux/macOS/WSL:
grep -rlZ --include='*.cs' -w 'IExperience' POC/ \
  | xargs -0 sed -i 's/\bIExperience\b/IDomain/g'

# Verify zero residual literal matches of the old token remain:
grep -rn --include='*.cs' -w 'IExperience' POC/ || echo "clean"
```

Expected: `clean` (every match was rewritten).

- [ ] **Step 1.3 — Rename internal helper/fake classes named after the old interface**

Some test fakes and classes are named `FakeExperience`, `DefaultShapeExperience`, `ExperienceWithPerGrain`, `FakeExperienceWithCaps` — these remain valid names semantically (they're test fakes implementing what is now `IDomain`). Don't touch them in slice 1. (Slice 4 renames the infrastructure `FakeExperienceRestartService`, which is a different file.)

- [ ] **Step 1.4 — Rename the test class + file**

```bash
git mv POC/test/Ino.Core.Tests/IExperienceTests.cs POC/test/Ino.Core.Tests/IDomainTests.cs
```

Then edit the file. Change:

```csharp
public class IExperienceTests
```

to:

```csharp
public class IDomainTests
```

Also rewrite the test method `PerGrainCapabilities_subset_rule_can_be_asserted_from_IExperience_alone` → `PerGrainCapabilities_subset_rule_can_be_asserted_from_IDomain_alone`.

- [ ] **Step 1.5 — Run the full build + test suite**

```bash
dotnet build POC/ino.slnx
dotnet test POC/ino.slnx
```

Expected: PASS, 0 warnings introduced (pre-existing analyzer warnings tolerated), all pre-existing tests pass unchanged.

- [ ] **Step 1.6 — Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
refactor(poc): rename IExperience bundle interface → IDomain

Frees the name IExperience so the upcoming user-verb contract
(docs/superpowers/specs/2026-04-23-domain-experience-vocabulary-design.md
§ Core contracts) can land under that name without collision.

No behaviour change; no new types; every implementer and call site
now references IDomain. BundleId / IDomain.Bundle are unchanged
this commit — those migrate in the next slice.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Slice 2 — Rename `BundleId` → `DomainId`, `Bundle` property → `Id`

**Goal.** Retire the word "Bundle" from the codebase. `BundleId` (value type) becomes `DomainId`; `IDomain.Bundle` becomes `IDomain.Id`; `CanonicalRegistration.Bundle` / `ReactiveRegistration.Bundle` become `.Domain`. `RegistrationOptions.BuiltInBundleId` becomes `BuiltInDomainId`.

**Files:**

- Rename: `POC/src/Ino.Core/BundleId.cs` → `POC/src/Ino.Core/DomainId.cs`
- Rename: `POC/src/Ino.Aspire.Hosting/BundleIdJsonConverter.cs` → `POC/src/Ino.Aspire.Hosting/DomainIdJsonConverter.cs`
- Modify (type + property references): 36 files contain `BundleId` — full list from `grep -rln --include='*.cs' -w BundleId POC/`. Key ones: `IDomain.cs`, `CanonicalRegistration.cs`, `ReactiveRegistration.cs`, `Discovery.cs`, `RegistrationOptions.cs`, `InstalledSet.cs`, `InstalledState.cs`, `MarketplaceFeed.cs`, `Travel.cs`, `Taxi.cs`, all fixtures, `MarketplaceController.cs`, `IDomainTests.cs` (renamed in slice 1), `ExperienceRegistrarTests.cs`, `InstalledSetTests.cs`, `MarketplaceControllerTests.cs`, `DiscoveryGrainTests.cs`, `DiscoveryClientTests.cs`, `SystemFirePortTests.cs`, `InstallFlowTests.cs`, `TypedIdentityTests.cs`, `CallerTests.cs`, `FirePortTests.cs`, `CapabilityEnforcerTests.cs`.

### Steps

- [ ] **Step 2.1 — Rename the value-type source file**

```bash
git mv POC/src/Ino.Core/BundleId.cs POC/src/Ino.Core/DomainId.cs
```

Then rewrite its contents:

```csharp
namespace Ino.Core;

[GenerateSerializer]
public readonly record struct DomainId([property: Id(0)] string Value)
{
    public override string ToString() => Value;

    public static DomainId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("DomainId cannot be empty.", nameof(value));
        return new DomainId(value);
    }
}
```

- [ ] **Step 2.2 — Rename the JSON converter file**

```bash
git mv POC/src/Ino.Aspire.Hosting/BundleIdJsonConverter.cs POC/src/Ino.Aspire.Hosting/DomainIdJsonConverter.cs
```

Rewrite contents:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Ino.Core;

namespace Ino.Aspire.Hosting;

public sealed class DomainIdJsonConverter : JsonConverter<DomainId>
{
    public override DomainId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString() ?? throw new JsonException("DomainId cannot be null");
        return DomainId.From(raw);
    }

    public override void Write(Utf8JsonWriter writer, DomainId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
```

- [ ] **Step 2.3 — Replace `BundleId` type references across the repo**

```bash
grep -rlZ --include='*.cs' -w 'BundleId' POC/ \
  | xargs -0 sed -i 's/\bBundleId\b/DomainId/g'

grep -rlZ --include='*.cs' -w 'BundleIdJsonConverter' POC/ \
  | xargs -0 sed -i 's/\bBundleIdJsonConverter\b/DomainIdJsonConverter/g'

# Already-covered by the first sed, but assert the cleanup:
grep -rn --include='*.cs' -w 'BundleId' POC/ || echo "clean"
grep -rn --include='*.cs' -w 'BundleIdJsonConverter' POC/ || echo "clean"
```

Expected: `clean` twice.

- [ ] **Step 2.4 — Rename the `IDomain.Bundle` property → `IDomain.Id`**

Edit `POC/src/Ino.Core.Hosting/IDomain.cs`:

```csharp
using System.Collections.Immutable;
using Ino.Core;

namespace Ino.Core.Hosting;

public interface IDomain
{
    DomainId Id { get; }
    string Version { get; }
    IReadOnlyList<Capability> DeclaredCapabilities { get; }

    IReadOnlyDictionary<Type, IReadOnlyList<Capability>> PerGrainCapabilities
        => ImmutableDictionary<Type, IReadOnlyList<Capability>>.Empty;
}
```

- [ ] **Step 2.5 — Rename `Bundle` property implementations and accesses**

All implementations of `IDomain.Bundle` must become `IDomain.Id`. This is also a mechanical rename, but it's *not* safe to `sed s/\.Bundle/\.Id/g` because `Bundle` is a common word elsewhere. Instead use more specific anchors:

Per-file, rewrite these exact patterns (one-liner `sed` helpers, then verify):

```bash
# Implementations — records/properties named Bundle on IDomain classes:
#   `public DomainId Bundle =>`  →  `public DomainId Id =>`
grep -rlZ --include='*.cs' 'public DomainId Bundle' POC/ \
  | xargs -0 sed -i 's/public DomainId Bundle /public DomainId Id /g'

# `exp.Bundle` (local var `exp` of type IDomain) → `exp.Id`
# Safer: find and review each before applying. Candidate files:
#   IDomainTests.cs (in POC/test/Ino.Core.Tests/)
#   ExperiencesSiloConfigurator.cs (line 63)
#   ExperienceRegistrar.cs (lines 30, 41)
#   WithExperienceExtensions.cs (lines 12, 14, 19)
#   MarketplaceController.cs (various)
#   InoBuilder.cs (if any)
grep -rn --include='*.cs' '\.Bundle\b' POC/
# Review the results; rewrite each `IDomain`-scoped `.Bundle` access to `.Id`.
```

Specific rewrites you must apply (all uses of `Bundle` as an IDomain property accessor, verbatim):

| File | Line (approx) | Before | After |
|---|---|---|---|
| `POC/test/Ino.Core.Tests/IDomainTests.cs` | ~22, ~49, ~56 | `exp.Bundle.Should().Be(...)` and `public DomainId Bundle => DomainId.From("…")` | `exp.Id.Should().Be(...)` and `public DomainId Id => DomainId.From("…")` |
| `POC/experiences/travel/Ino.Experiences.Travel/Travel.cs` | ~15 | `public DomainId Bundle => DomainId.From("Ino.Experiences.Travel");` | `public DomainId Id => DomainId.From("Ino.Experiences.Travel");` |
| `POC/experiences/taxi/Ino.Experiences.Taxi/Taxi.cs` | ~21 | `public DomainId Bundle => DomainId.From("Ino.Experiences.Taxi");` | `public DomainId Id => DomainId.From("Ino.Experiences.Taxi");` |
| `POC/experiences/testing/Ino.Testing.Fixture.{Alpha,Beta,Gamma,Delta}/*.cs` | `public DomainId Bundle => DomainId.From("...")` | `public DomainId Id => DomainId.From("...")` |
| `POC/src/Ino.System/ExperienceRegistrar.cs` | ~30, ~41 | `experience.Bundle` | `experience.Id` |
| `POC/src/Ino.Experiences/ExperiencesSiloConfigurator.cs` | ~63 | `.ToDictionary(e => e.Bundle, …)` | `.ToDictionary(e => e.Id, …)` |
| `POC/src/Ino.Aspire.Hosting/WithExperienceExtensions.cs` | ~12, ~14, ~19 | `experience.Bundle` / `experience.Bundle.Value` | `experience.Id` / `experience.Id.Value` |

(Any residual `.Bundle` access on an `IDomain`/`Experience` variable that was missed will surface as a build error — chase those before committing.)

Note: `CanonicalRegistration.Bundle` and `ReactiveRegistration.Bundle` are *separate* properties on those records. They're also renamed to `.Domain` in this slice — see the next step.

- [ ] **Step 2.6 — Rename `CanonicalRegistration.Bundle` / `ReactiveRegistration.Bundle` → `.Domain`**

Edit `POC/src/Ino.System/CanonicalRegistration.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.System;

[GenerateSerializer]
public sealed record CanonicalRegistration(
    [property: Id(0)] Type SynapseType,
    [property: Id(1)] Type GrainType,
    [property: Id(2)] DomainId Domain,
    [property: Id(3)] IReadOnlyList<Capability> RequiredCapabilities);
```

Edit `POC/src/Ino.System/ReactiveRegistration.cs`:

```csharp
using Ino.Core;

namespace Ino.System;

[GenerateSerializer]
public sealed record ReactiveRegistration(
    [property: Id(0)] Type SynapseType,
    [property: Id(1)] Type GrainType,
    [property: Id(2)] DomainId Domain);
```

Update callers in `Discovery.cs`, `ExperienceRegistrar.cs`, `MarketplaceController.cs`:

```bash
grep -rn --include='*.cs' 'CanonicalRegistration\|ReactiveRegistration' POC/src/Ino.System/ POC/test/
# For each hit of `.Bundle` on a CanonicalRegistration/ReactiveRegistration instance,
# or the `Bundle:` named argument, rewrite to `.Domain` / `Domain:`.
```

Specifically, in `POC/src/Ino.System/Discovery.cs`:
- Line ~30: `canonical.Bundle` → `canonical.Domain`
- Line ~37: `new CanonicalRecord(canonical.GrainType, canonical.Bundle, ...)` — keep the positional arg but swap `canonical.Bundle` to `canonical.Domain`
- Line ~45: `reactive.Bundle` → `reactive.Domain`
- Line ~57–76: `rec.Bundle` / `r.Bundle` on the inner `CanonicalRecord`/`ReactiveRecord` — either also rename those internal records' `Bundle` field to `Domain` (recommended for consistency) or leave; these are private. Recommended: rename for consistency.
- Line ~104–105: `private sealed record CanonicalRecord(Type GrainType, DomainId Bundle, …)` → `Domain`; same for `ReactiveRecord`.

After rename, re-run:

```bash
grep -rn --include='*.cs' '\bBundle\b' POC/src/Ino.System/ POC/src/Ino.Core.Hosting/ POC/experiences/ || echo "clean"
```

Expected: `clean`. (Matches in other contexts — commit messages, docs — stay for slice 10.)

- [ ] **Step 2.7 — Rename `RegistrationOptions.BuiltInBundleId`**

Edit `POC/src/Ino.System/RegistrationOptions.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.System;

public sealed class RegistrationOptions
{
    public KernelSilo Silo { get; set; }
    public IReadOnlyList<IDomain> Experiences { get; set; } = [];

    public IReadOnlyList<Type> BuiltInGrainTypes { get; set; } = [];

    public DomainId BuiltInDomainId { get; set; } = DomainId.From("Ino.System.BuiltIns");
}
```

(Note: `Experiences` property is renamed in slice 4. Leave it named `Experiences` for now; only the type and `BuiltInBundleId` change.)

Update callers:

```bash
grep -rn --include='*.cs' 'BuiltInBundleId' POC/
```

Hits appear in `ExperienceRegistrar.cs` and `ExperienceRegistrarTests.cs`. Rename those:

```bash
grep -rlZ --include='*.cs' 'BuiltInBundleId' POC/ \
  | xargs -0 sed -i 's/\bBuiltInBundleId\b/BuiltInDomainId/g'
```

- [ ] **Step 2.8 — Rename `MarketplaceFeedEntry(BundleId Id, …)`**

Edit `POC/src/Ino.Aspire.Hosting/MarketplaceFeed.cs`:

```csharp
using Ino.Core;

namespace Ino.Aspire.Hosting;

public sealed record MarketplaceFeed(IReadOnlyList<MarketplaceFeedEntry> Experiences);
public sealed record MarketplaceFeedEntry(DomainId Id, string Description, string Version);
```

(The `MarketplaceFeed.Experiences` property rename happens in slice 9 — leave it for now.)

- [ ] **Step 2.9 — Rename `Caller.FromBundle` → `Caller.FromDomain` + `Telemetry.Tags.{Source,Target}Bundle` → `{Source,Target}Domain`**

`Caller.FromBundle` is one arm of the `Caller` discriminated union (see `POC/src/Ino.Core/Caller.cs`). Its property is also called `Bundle`. `SystemFirePort` pattern-matches on it as `caller.Source is Caller.FromBundle b ? b.Bundle.Value : null`.

Rename the union arm:

```bash
# Caller.FromBundle → Caller.FromDomain; the .Bundle property on that arm → .Domain.
grep -rlZ --include='*.cs' 'Caller\.FromBundle\|\.Bundle\.Value\b' POC/
```

Edit `POC/src/Ino.Core/Caller.cs` — in the `FromBundle` record arm, rename type name to `FromDomain` and the `BundleId Bundle` property to `DomainId Domain`.

Apply to call sites:

```bash
grep -rlZ --include='*.cs' 'Caller\.FromBundle' POC/ \
  | xargs -0 sed -i 's/\bCaller\.FromBundle\b/Caller.FromDomain/g'
```

Pattern-match sites (rewrite manually — the property access `.Bundle.Value` is too common to sed globally):

| File | Before | After |
|---|---|---|
| `POC/src/Ino.System/SystemFirePort.cs` (2 sites) | `caller.Source is Caller.FromBundle b ? b.Bundle.Value : null` | `caller.Source is Caller.FromDomain d ? d.Domain.Value : null` |
| `POC/src/Ino.System/SystemFirePort.cs` (`PublishEvent`) | `caller.Source is Caller.FromBundle b ? b.Bundle.Value : "gateway"` | `caller.Source is Caller.FromDomain d ? d.Domain.Value : "gateway"` |
| `POC/src/Ino.System/SystemFirePort.cs` (`DeriveChildContext`) | `new Caller.FromBundle(target.Bundle)` | `new Caller.FromDomain(target.Domain)` |
| `POC/src/Ino.Experiences/FirePort.cs` | any `Caller.FromBundle` construction / pattern-match | same transformation |
| `POC/src/Ino.Experiences/AmbientFire.cs` | same | same |

(`target.Bundle` already renamed to `target.Domain` — `CanonicalTarget` / `ReactiveTarget` Bundle → Domain per step 2.6.)

Now rename telemetry tag string constants. Read `POC/src/Ino.Core.Hosting/Telemetry.cs`:

```bash
grep -n 'SourceBundle\|TargetBundle\|source_bundle\|target_bundle' POC/src/Ino.Core.Hosting/Telemetry.cs
```

Rewrite the constant names AND the string values they hold:

| Before | After |
|---|---|
| `public const string SourceBundle = "ino.source_bundle"` | `public const string SourceDomain = "ino.source_domain"` |
| `public const string TargetBundle = "ino.target_bundle"` | `public const string TargetDomain = "ino.target_domain"` |

(The string-value change is wire-visible: any OTel dashboard filtering on `ino.source_bundle` will stop matching. This is acceptable for v0.1 — no dashboards consume it yet. Call it out in the commit message.)

Then fix call sites:

```bash
grep -rlZ --include='*.cs' 'Telemetry\.Tags\.SourceBundle\|Telemetry\.Tags\.TargetBundle' POC/ \
  | xargs -0 sed -i \
      -e 's/\bTelemetry\.Tags\.SourceBundle\b/Telemetry.Tags.SourceDomain/g' \
      -e 's/\bTelemetry\.Tags\.TargetBundle\b/Telemetry.Tags.TargetDomain/g'
```

- [ ] **Step 2.10 — Build + test**

```bash
dotnet build POC/ino.slnx
dotnet test POC/ino.slnx
```

Expected: PASS. If you see `CS0117: 'IDomain' does not contain a definition for 'Bundle'`, a `.Bundle` access on a domain variable was missed — grep and fix.

- [ ] **Step 2.11 — Aspire boot smoke**

```bash
aspire stop
aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated
```

Use `mcp__aspire__list_resources` to confirm all three silos Running + Healthy.

Open `https://localhost:<system-port>` in Chrome (via `mcp__chrome-devtools__new_page`), wait for the onboarding orb, confirm zero console errors. Then `aspire stop`.

- [ ] **Step 2.12 — Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
refactor(poc): rename BundleId → DomainId, Bundle → Id, Caller.FromDomain

Retires the word "bundle" from the runtime types + telemetry.

- BundleId → DomainId (value type)
- BundleIdJsonConverter → DomainIdJsonConverter
- IDomain.Bundle → IDomain.Id (same wire format, same JSON shape)
- CanonicalRegistration.Bundle / ReactiveRegistration.Bundle → .Domain
- RegistrationOptions.BuiltInBundleId → BuiltInDomainId
- Caller.FromBundle → Caller.FromDomain (discriminated-union arm);
  its .Bundle property → .Domain
- Telemetry.Tags.SourceBundle / TargetBundle → SourceDomain /
  TargetDomain (wire-visible: OTel tag names change from
  "ino.source_bundle" → "ino.source_domain"; no dashboards consume
  these yet, so the break is acceptable)

Per docs/superpowers/specs/2026-04-23-domain-experience-vocabulary-design.md
Core contracts + Rename map + Ripple effects/Telemetry sections. No
backward-compat shims.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Slice 3 — Add `ExperienceId` + user-verb `IExperience` + `Experience` record + extend `IDomain.DeclaredExperiences`

**Goal.** Introduce the user-verb type family with TDD. `IDomain` gains `DeclaredExperiences` with a default-empty implementation so no existing domain breaks.

**Files:**

- Create: `POC/src/Ino.Core/ExperienceId.cs`
- Create: `POC/src/Ino.Aspire.Hosting/ExperienceIdJsonConverter.cs`
- Create: `POC/src/Ino.Core.Hosting/IExperience.cs`
- Create: `POC/src/Ino.Core.Hosting/Experience.cs`
- Modify: `POC/src/Ino.Core.Hosting/IDomain.cs`
- Create: `POC/test/Ino.Core.Tests/ExperienceIdTests.cs`
- Create: `POC/test/Ino.Core.Tests/IExperienceTests.cs`
- Modify: `POC/test/Ino.Core.Tests/IDomainTests.cs` (two new test cases for `DeclaredExperiences`)

### Steps

- [ ] **Step 3.1 — Write `ExperienceId` tests (failing)**

Create `POC/test/Ino.Core.Tests/ExperienceIdTests.cs`:

```csharp
using FluentAssertions;
using Ino.Core;
using Xunit;

namespace Ino.Core.Tests;

public class ExperienceIdTests
{
    [Fact]
    public void From_produces_record_with_value()
    {
        var id = ExperienceId.From("travel.plan-trip");

        id.Value.Should().Be("travel.plan-trip");
        id.ToString().Should().Be("travel.plan-trip");
    }

    [Fact]
    public void Two_values_with_same_string_are_equal()
    {
        ExperienceId.From("travel.plan-trip")
            .Should().Be(ExperienceId.From("travel.plan-trip"));
    }

    [Fact]
    public void Two_values_with_different_strings_are_not_equal()
    {
        ExperienceId.From("travel.plan-trip")
            .Should().NotBe(ExperienceId.From("travel.find-flights"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_rejects_empty_or_whitespace(string value)
    {
        var act = () => ExperienceId.From(value);
        act.Should().Throw<ArgumentException>();
    }
}
```

- [ ] **Step 3.2 — Run the failing test**

```bash
dotnet test POC/test/Ino.Core.Tests/Ino.Core.Tests.csproj --filter "FullyQualifiedName~ExperienceIdTests"
```

Expected: FAIL with "ExperienceId not defined" (or CS0246).

- [ ] **Step 3.3 — Implement `ExperienceId`**

Create `POC/src/Ino.Core/ExperienceId.cs`:

```csharp
namespace Ino.Core;

[GenerateSerializer]
public readonly record struct ExperienceId([property: Id(0)] string Value)
{
    public override string ToString() => Value;

    public static ExperienceId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("ExperienceId cannot be empty.", nameof(value));
        return new ExperienceId(value);
    }
}
```

- [ ] **Step 3.4 — Implement `ExperienceIdJsonConverter`**

Create `POC/src/Ino.Aspire.Hosting/ExperienceIdJsonConverter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Ino.Core;

namespace Ino.Aspire.Hosting;

public sealed class ExperienceIdJsonConverter : JsonConverter<ExperienceId>
{
    public override ExperienceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString() ?? throw new JsonException("ExperienceId cannot be null");
        return ExperienceId.From(raw);
    }

    public override void Write(Utf8JsonWriter writer, ExperienceId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
```

- [ ] **Step 3.5 — Verify `ExperienceId` tests pass**

```bash
dotnet test POC/test/Ino.Core.Tests/Ino.Core.Tests.csproj --filter "FullyQualifiedName~ExperienceIdTests"
```

Expected: PASS (4 tests).

- [ ] **Step 3.6 — Write failing tests for user-verb `IExperience` + `Experience` record**

Create `POC/test/Ino.Core.Tests/IExperienceTests.cs`:

```csharp
using System.Collections.Generic;
using FluentAssertions;
using Ino.Core;
using Ino.Core.Hosting;
using Xunit;

namespace Ino.Core.Tests;

// Tests the user-verb IExperience contract (introduced in slice 3 of the
// domain/experience-vocabulary rename) + its Experience record default.
// Separate file from IDomainTests.cs (which covers the bundle contract).
public class IExperienceTests
{
    [Fact]
    public void Experience_record_round_trips_all_five_fields()
    {
        var id = ExperienceId.From("travel.plan-trip");
        var examples = new[] { "plan a trip to bali", "help me plan 5 days in tokyo" };

        IExperience exp = new Experience(
            Id: id,
            DisplayName: "Plan a trip",
            Description: "Build an itinerary with flights, hotels, and things to do.",
            CanonicalSynapseType: typeof(FakeSynapse),
            PromptExamples: examples);

        exp.Id.Should().Be(id);
        exp.DisplayName.Should().Be("Plan a trip");
        exp.Description.Should().Be("Build an itinerary with flights, hotels, and things to do.");
        exp.CanonicalSynapseType.Should().Be<FakeSynapse>();
        exp.PromptExamples.Should().Equal(examples);
    }

    [Fact]
    public void Two_Experience_records_with_same_inputs_are_equal()
    {
        var examples = new[] { "one" };
        var a = new Experience(
            ExperienceId.From("x.y"), "Y", "desc", typeof(FakeSynapse), examples);
        var b = new Experience(
            ExperienceId.From("x.y"), "Y", "desc", typeof(FakeSynapse), examples);

        a.Should().Be(b);
    }

    private sealed record FakeSynapse : ISynapse;
}
```

- [ ] **Step 3.7 — Run the failing test**

```bash
dotnet test POC/test/Ino.Core.Tests/Ino.Core.Tests.csproj --filter "FullyQualifiedName~Ino.Core.Tests.IExperienceTests"
```

Expected: FAIL with "IExperience not defined" / "Experience not defined".

- [ ] **Step 3.8 — Implement the user-verb `IExperience`**

Create `POC/src/Ino.Core.Hosting/IExperience.cs`:

```csharp
using Ino.Core;

namespace Ino.Core.Hosting;

public interface IExperience
{
    ExperienceId Id { get; }
    string DisplayName { get; }
    string Description { get; }
    Type CanonicalSynapseType { get; }
    IReadOnlyList<string> PromptExamples { get; }
}
```

- [ ] **Step 3.9 — Implement the `Experience` record default**

Create `POC/src/Ino.Core.Hosting/Experience.cs`:

```csharp
using Ino.Core;

namespace Ino.Core.Hosting;

public sealed record Experience(
    ExperienceId Id,
    string DisplayName,
    string Description,
    Type CanonicalSynapseType,
    IReadOnlyList<string> PromptExamples) : IExperience;
```

- [ ] **Step 3.10 — Verify user-verb tests pass**

```bash
dotnet test POC/test/Ino.Core.Tests/Ino.Core.Tests.csproj --filter "FullyQualifiedName~Ino.Core.Tests.IExperienceTests"
```

Expected: PASS (2 tests).

- [ ] **Step 3.11 — Write failing tests for `IDomain.DeclaredExperiences`**

Append to `POC/test/Ino.Core.Tests/IDomainTests.cs` (inside the existing `IDomainTests` class, before the closing `}`):

```csharp
    [Fact]
    public void Default_DeclaredExperiences_is_empty()
    {
        IDomain d = new DefaultShapeExperience();
        d.DeclaredExperiences.Should().BeEmpty();
    }

    [Fact]
    public void Domain_can_declare_experiences()
    {
        IDomain d = new DomainWithExperiences();

        d.DeclaredExperiences.Should().ContainSingle()
            .Which.Id.Should().Be(ExperienceId.From("fake.do-thing"));
    }

    private sealed class DomainWithExperiences : IDomain
    {
        public DomainId Id => DomainId.From("Ino.Testing.WithVerbs");
        public string Version => "1.0.0";
        public IReadOnlyList<Capability> DeclaredCapabilities => [];
        public IReadOnlyList<IExperience> DeclaredExperiences =>
        [
            new Experience(
                ExperienceId.From("fake.do-thing"),
                DisplayName: "Do a thing",
                Description: "A test verb.",
                CanonicalSynapseType: typeof(object),
                PromptExamples: ["please do the thing"]),
        ];
    }
```

- [ ] **Step 3.12 — Run the failing test**

```bash
dotnet test POC/test/Ino.Core.Tests/Ino.Core.Tests.csproj --filter "FullyQualifiedName~IDomainTests"
```

Expected: FAIL with "IDomain does not contain a definition for DeclaredExperiences".

- [ ] **Step 3.13 — Extend `IDomain` with `DeclaredExperiences` (default empty)**

Edit `POC/src/Ino.Core.Hosting/IDomain.cs`:

```csharp
using System.Collections.Immutable;
using Ino.Core;

namespace Ino.Core.Hosting;

public interface IDomain
{
    DomainId Id { get; }
    string Version { get; }
    IReadOnlyList<Capability> DeclaredCapabilities { get; }

    IReadOnlyList<IExperience> DeclaredExperiences => Array.Empty<IExperience>();

    IReadOnlyDictionary<Type, IReadOnlyList<Capability>> PerGrainCapabilities
        => ImmutableDictionary<Type, IReadOnlyList<Capability>>.Empty;
}
```

(Using `Array.Empty<IExperience>()` — fewer allocations than `[]`, matches the existing `PerGrainCapabilities` default style.)

- [ ] **Step 3.14 — Verify all tests pass**

```bash
dotnet test POC/ino.slnx
```

Expected: PASS. Slice adds 8 new tests (4 `ExperienceId`, 2 user-verb `IExperience`, 2 `IDomain.DeclaredExperiences`) with no regressions.

- [ ] **Step 3.15 — Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
feat(poc): add ExperienceId, user-verb IExperience, IDomain.DeclaredExperiences

Introduces the first-class user-verb type family:
- ExperienceId (Ino.Core) — "travel.plan-trip"-style stable key
- IExperience (Ino.Core.Hosting) — user-verb contract with Id,
  DisplayName, Description, CanonicalSynapseType, PromptExamples
- Experience record — default-shape implementation for most verbs
- IDomain.DeclaredExperiences — default-empty on the domain contract

Plus ExperienceIdJsonConverter for wire round-trip. No callers yet —
slice 7 populates Travel/Taxi experiences; slice 8 aggregates them
through Discovery; slice 9 reshapes the marketplace feed.

Per spec §Core contracts. 8 new tests.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Slice 4 — Rename infrastructure types

**Goal.** `IExperienceRestartService` → `IDomainRestartService`; `ExperienceRegistrar` → `DomainRegistrar`; `WithExperience<T>` → `WithDomain<T>`; `ExperienceMetadata` → `DomainMetadata`; `ExperiencesSiloConfigurator` → `DomainsSiloConfigurator`; `IInoBuilder.RegisteredExperiences` → `RegisteredDomains`; `RegisterExperience()` → `RegisterDomain()`; `RegistrationOptions.Experiences` → `Domains`. Test-side: `ExperienceRegistrarTests` → `DomainRegistrarTests`; `FakeExperienceRestartService` → `FakeDomainRestartService`; `ExperienceMetadataTests` → `DomainMetadataTests`. Project names and folders stay `Ino.Experiences.*` — those move in slice 5.

**Files:** see rename table in File Structure Overview.

### Steps

- [ ] **Step 4.1 — Rename the service interface + file**

```bash
git mv POC/src/Ino.System/IExperienceRestartService.cs POC/src/Ino.System/IDomainRestartService.cs
git mv POC/src/Ino.System/ExperienceRestartService.cs POC/src/Ino.System/DomainRestartService.cs
```

Edit `POC/src/Ino.System/IDomainRestartService.cs`:

```csharp
namespace Ino.System;

public interface IDomainRestartService
{
    Task<RestartOutcome> RestartDomainsAsync(TimeSpan timeout, CancellationToken ct = default);
}

public enum RestartOutcome
{
    Restarted,
    PendingRestart,
}
```

(Kept the existing two-state enum; the XML-doc paragraphs from the original file are deleted per `CLAUDE.md` — no `/// <summary>` with restated information. The new name `RestartDomainsAsync` is clearer than the two-layered "experiences" vocabulary.)

Edit `POC/src/Ino.System/DomainRestartService.cs` (the file is currently the `NullExperienceRestartService`; rename the class + method):

```csharp
using Microsoft.Extensions.Logging;

namespace Ino.System;

public sealed class NullDomainRestartService(ILogger<NullDomainRestartService> logger)
    : IDomainRestartService
{
    public Task<RestartOutcome> RestartDomainsAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        logger.LogWarning("NullDomainRestartService.RestartDomainsAsync called — no-op. " +
            "Wire an IDomainRestartService backed by Aspire's ResourceCommandService to enable real restarts.");
        return Task.FromResult(RestartOutcome.PendingRestart);
    }
}
```

Then check the "real" `ExperienceRestartService` (the one that actually calls the Aspire command service). Read `POC/src/Ino.System/ExperienceRestartService.cs` — renamed by the git mv to `DomainRestartService.cs` — and swap class name + method name accordingly.

(If the file contains both `NullExperienceRestartService` and `ExperienceRestartService`, keep both but rename inside. If they were in separate files, both were moved — adjust.)

- [ ] **Step 4.2 — Rewrite callers of `IExperienceRestartService` / `RestartExperiencesAsync`**

```bash
grep -rlZ --include='*.cs' 'IExperienceRestartService\|RestartExperiencesAsync\|NullExperienceRestartService\|ExperienceRestartService' POC/ \
  | xargs -0 sed -i \
      -e 's/\bIExperienceRestartService\b/IDomainRestartService/g' \
      -e 's/\bNullExperienceRestartService\b/NullDomainRestartService/g' \
      -e 's/\bExperienceRestartService\b/DomainRestartService/g' \
      -e 's/\bRestartExperiencesAsync\b/RestartDomainsAsync/g'

grep -rn --include='*.cs' 'ExperienceRestartService\|RestartExperiencesAsync' POC/ || echo "clean"
```

Expected: `clean`. Callers in `Ino.System.Host/Program.cs`, `MarketplaceController.cs`, and tests.

- [ ] **Step 4.3 — Rename the test helper `FakeExperienceRestartService`**

```bash
git mv POC/test/Ino.System.Tests/FakeExperienceRestartService.cs POC/test/Ino.System.Tests/FakeDomainRestartService.cs
```

Edit the file:

```csharp
namespace Ino.System.Tests;

internal sealed class FakeDomainRestartService : IDomainRestartService
{
    public int CallCount { get; private set; }
    public Exception? NextError { get; set; }
    public RestartOutcome Outcome { get; set; } = RestartOutcome.Restarted;

    public Task<RestartOutcome> RestartDomainsAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        CallCount++;
        if (NextError is not null) throw NextError;
        return Task.FromResult(Outcome);
    }
}
```

Rewrite callers:

```bash
grep -rlZ --include='*.cs' 'FakeExperienceRestartService' POC/ \
  | xargs -0 sed -i 's/\bFakeExperienceRestartService\b/FakeDomainRestartService/g'
```

- [ ] **Step 4.4 — Rename `ExperienceRegistrar` → `DomainRegistrar`**

```bash
git mv POC/src/Ino.System/ExperienceRegistrar.cs POC/src/Ino.System/DomainRegistrar.cs
git mv POC/test/Ino.System.Tests/ExperienceRegistrarTests.cs POC/test/Ino.System.Tests/DomainRegistrarTests.cs

grep -rlZ --include='*.cs' 'ExperienceRegistrar' POC/ \
  | xargs -0 sed -i 's/\bExperienceRegistrar\b/DomainRegistrar/g'
```

Inside `DomainRegistrarTests.cs`, rename the class: `public class ExperienceRegistrarTests` → `public class DomainRegistrarTests`, and the internal fakes `FakeExperience` / `FakeExperienceWithCaps` → `FakeDomain` / `FakeDomainWithCaps`:

```bash
sed -i \
  -e 's/\bExperienceRegistrarTests\b/DomainRegistrarTests/g' \
  -e 's/\bFakeExperience\b/FakeDomain/g' \
  -e 's/\bFakeExperienceWithCaps\b/FakeDomainWithCaps/g' \
  POC/test/Ino.System.Tests/DomainRegistrarTests.cs
```

Inside `DomainRegistrar.cs`, rename the local loop variable `experience` → `domain` (inside `foreach (var experience in options.Experiences)`), for vocabulary consistency — but NOT `options.Experiences` property itself (renamed in step 4.6).

- [ ] **Step 4.5 — Rename `WithExperience<T>` → `WithDomain<T>`**

```bash
git mv POC/src/Ino.Aspire.Hosting/WithExperienceExtensions.cs POC/src/Ino.Aspire.Hosting/WithDomainExtensions.cs
```

Rewrite `POC/src/Ino.Aspire.Hosting/WithDomainExtensions.cs` body:

```csharp
using Ino.Core.Hosting;

namespace Ino.Aspire.Hosting;

public static class WithDomainExtensions
{
    public static IInoBuilder WithDomain<T>(this IInoBuilder builder)
        where T : class, IDomain, new()
    {
        var installed = InstalledSet.Load();
        var domain = new T();
        if (installed.Contains(domain.Id))
        {
            Console.Out.WriteLine($"[ino] WithDomain: registering '{domain.Id.Value}' (found in installed.json).");
            builder.RegisterDomain(domain);
        }
        else
        {
            Console.Out.WriteLine($"[ino] WithDomain: skipping '{domain.Id.Value}' — not in installed.json. Install via POST /marketplace/install/{{id}} to enable.");
        }
        return builder;
    }
}
```

Rewrite callers:

```bash
grep -rlZ --include='*.cs' 'WithExperience\b\|WithExperienceExtensions' POC/ \
  | xargs -0 sed -i \
      -e 's/\bWithExperience\b/WithDomain/g' \
      -e 's/\bWithExperienceExtensions\b/WithDomainExtensions/g'
```

(Also catches `WithExperience<T>` generic-form uses — the word-boundary covers `<` as boundary.)

- [ ] **Step 4.6 — Rename `IInoBuilder.RegisteredExperiences` / `RegisterExperience()` + implementation**

Edit `POC/src/Ino.Aspire.Hosting/IInoBuilder.cs`:

```csharp
using Ino.Core.Hosting;

namespace Ino.Aspire.Hosting;

public interface IInoBuilder
{
    IReadOnlyList<IDomain> RegisteredDomains { get; }
    void RegisterDomain(IDomain domain);
}
```

Edit `POC/src/Ino.Aspire.Hosting/InoBuilder.cs`:

```csharp
using Ino.Core.Hosting;

namespace Ino.Aspire.Hosting;

internal sealed class InoBuilder : IInoBuilder
{
    private readonly List<IDomain> _domains = [];

    public IReadOnlyList<IDomain> RegisteredDomains => _domains;

    public void RegisterDomain(IDomain domain) => _domains.Add(domain);
}
```

Rewrite any remaining callers:

```bash
grep -rn --include='*.cs' '\bRegisteredExperiences\b\|\bRegisterExperience\b' POC/ || echo "clean"
```

Expected: `clean`. (Any old call sites in `Ino.Core.Tests/IExperienceTests.cs` are gone — that file was moved to `IDomainTests.cs` in slice 1.)

- [ ] **Step 4.7 — Rename `RegistrationOptions.Experiences` → `Domains`**

Edit `POC/src/Ino.System/RegistrationOptions.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.System;

public sealed class RegistrationOptions
{
    public KernelSilo Silo { get; set; }
    public IReadOnlyList<IDomain> Domains { get; set; } = [];

    public IReadOnlyList<Type> BuiltInGrainTypes { get; set; } = [];

    public DomainId BuiltInDomainId { get; set; } = DomainId.From("Ino.System.BuiltIns");
}
```

Rewrite callers:

```bash
# The property is RegistrationOptions.Experiences. Match either a direct
# `.Experiences` access on a RegistrationOptions or an options-initializer key.
grep -rn --include='*.cs' '\bExperiences\s*=\|options\.Experiences\|\.Experiences\s*=' POC/
# Review each hit; rewrite to .Domains / Domains =.
```

Specific sites:
- `POC/src/Ino.System/DomainRegistrar.cs` — `foreach (var domain in options.Experiences)` → `options.Domains`
- `POC/src/Ino.Experiences/ExperiencesSiloConfigurator.cs` — `o.Experiences = installedExperiences;` → `o.Domains = installedDomains;`. (Also rename the parameter `installedExperiences` → `installedDomains` in step 4.8.)
- `POC/test/Ino.System.Tests/DomainRegistrarTests.cs` — all `Experiences = [...]` initializers → `Domains = [...]`

Also rename `RegistrationOptions.BuiltInBundleId` references to `BuiltInDomainId` (caught by slice 2 but re-verify):

```bash
grep -rn --include='*.cs' '\bBuiltInBundleId\b' POC/ || echo "clean"
```

- [ ] **Step 4.8 — Rename `ExperiencesSiloConfigurator` → `DomainsSiloConfigurator`**

```bash
git mv POC/src/Ino.Experiences/ExperiencesSiloConfigurator.cs POC/src/Ino.Experiences/DomainsSiloConfigurator.cs
```

Edit `DomainsSiloConfigurator.cs` — rename class, method, and parameter:

```csharp
using System.Net;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Placement;
using Ino.System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Hosting;
using Orleans.Runtime.MembershipService.SiloMetadata;

namespace Ino.Experiences;

public static class DomainsSiloConfigurator
{
    public static IHostApplicationBuilder AddDomainsSilo(
        this IHostApplicationBuilder builder,
        IReadOnlyList<IDomain> installedDomains)
    {
        builder.UseOrleans(silo =>
        {
            silo.UseLocalhostClustering(
                siloPort: 11113,
                gatewayPort: 30002,
                primarySiloEndpoint: new IPEndPoint(IPAddress.Parse("127.0.0.1"), 11111),
                serviceId: SystemSiloConfigurator.ServiceId,
                clusterId: SystemSiloConfigurator.ClusterId);

            silo.UseSiloMetadata(new Dictionary<string, string>
            {
                [PinToSiloStrategy.SiloMetadataKey] = KernelSilo.Experiences.ToResourceName(),
            });
        });

        builder.Services.AddPinToSiloPlacement();

        foreach (var domain in installedDomains)
        {
            builder.Services.AddSingleton(domain);
            _ = domain.GetType().Assembly;
        }

        builder.Services.Configure<RegistrationOptions>(o =>
        {
            o.Silo = KernelSilo.Experiences;
            o.Domains = installedDomains;
        });
        builder.Services.AddHostedService<RegistrationHostedService>();

        builder.Services.AddSingleton<IDiscoveryClient, DiscoveryClient>();

        builder.Services.AddSingleton<ICapabilityEnforcer>(sp =>
        {
            var declarations = installedDomains
                .ToDictionary(d => d.Id, d => (IReadOnlyList<Capability>)d.DeclaredCapabilities.ToArray());
            return new CapabilityEnforcer(declarations);
        });

        builder.Services.AddSingleton(
            _ => new global::System.Diagnostics.ActivitySource(Telemetry.ActivitySourceName));
        builder.Services.AddSingleton<IFirePort, FirePort>();

        builder.Services.AddSingleton<IAmbientFire>(sp => new AmbientFire(
            sp.GetRequiredService<IFirePort>(),
            KernelSilo.Experiences,
            sp.GetRequiredService<ILogger<AmbientFire>>()));

        return builder;
    }
}
```

(`KernelSilo.Experiences` stays as-is — slice 6 renames it. Same for `namespace Ino.Experiences` — slice 5 renames that.)

Rewrite callers:

```bash
grep -rlZ --include='*.cs' 'ExperiencesSiloConfigurator\|AddExperiencesSilo' POC/ \
  | xargs -0 sed -i \
      -e 's/\bExperiencesSiloConfigurator\b/DomainsSiloConfigurator/g' \
      -e 's/\bAddExperiencesSilo\b/AddDomainsSilo/g'
```

In `POC/src/Ino.Experiences.Host/Program.cs`, rename the variable `installed` → `installed` (stays), but the `IReadOnlyList<IExperience>` type reference is now `IReadOnlyList<IDomain>` (already caught by slice 1), and the method call `builder.AddExperiencesSilo(installed)` → `builder.AddDomainsSilo(installed)` (caught above).

- [ ] **Step 4.9 — Rename `ExperienceMetadata` → `DomainMetadata`**

```bash
git mv POC/src/Ino.Core/ExperienceMetadata.cs POC/src/Ino.Core/DomainMetadata.cs
git mv POC/test/Ino.Core.Tests/ExperienceMetadataTests.cs POC/test/Ino.Core.Tests/DomainMetadataTests.cs
```

Edit `POC/src/Ino.Core/DomainMetadata.cs`:

```csharp
namespace Ino.Core;

// Source-generated domain descriptor. Emitted at compile time by the Phase 3
// source generator and read by AppHost composition. ExperienceId field names
// a user-verb keyword surface (deprecated pre-slice-7 naming — kept until the
// source generator is wired to the new IExperience contract in a follow-up).
public sealed record DomainMetadata(
    string ExperienceId,
    string Version,
    string Description,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<CanonicalNeuronInfo> CanonicalNeurons,
    IReadOnlyList<ReactiveNeuronInfo> ReactiveNeurons,
    IReadOnlyList<string> UserEntrySchemas,
    IReadOnlyList<Capability> RequiredCapabilities,
    string CoreVersion);
```

(Note: the `ExperienceId` field inside the record keeps its name — it's a pre-existing reference to a dev keyword that hasn't been re-purposed to the new `ExperienceId` value type. A follow-up track repurposes this once the source generator catches up. The rename here is only the record type name.)

```bash
grep -rlZ --include='*.cs' '\bExperienceMetadata\b' POC/ \
  | xargs -0 sed -i 's/\bExperienceMetadata\b/DomainMetadata/g'

# Also rename the test-class symbol:
sed -i 's/\bExperienceMetadataTests\b/DomainMetadataTests/g' POC/test/Ino.Core.Tests/DomainMetadataTests.cs
```

- [ ] **Step 4.10 — Build + full test**

```bash
dotnet build POC/ino.slnx
dotnet test POC/ino.slnx
```

Expected: PASS. All existing behaviour unchanged; only type / method names shifted.

- [ ] **Step 4.11 — Aspire boot smoke**

```bash
aspire stop
aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated
# Verify via mcp__aspire__list_resources, then Chrome smoke the system URL,
# then aspire stop.
```

- [ ] **Step 4.12 — Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
refactor(poc): rename infrastructure Experience* → Domain*

Completes the domain/experience-vocabulary rename at the internal
API boundary. No behaviour change, no wire-format change.

- IExperienceRestartService / RestartExperiencesAsync /
  NullExperienceRestartService → IDomainRestartService /
  RestartDomainsAsync / NullDomainRestartService
- ExperienceRegistrar → DomainRegistrar
- WithExperience<T>() → WithDomain<T>()
- ExperiencesSiloConfigurator.AddExperiencesSilo →
  DomainsSiloConfigurator.AddDomainsSilo
- IInoBuilder.RegisteredExperiences / RegisterExperience →
  RegisteredDomains / RegisterDomain
- RegistrationOptions.Experiences → Domains
- ExperienceMetadata → DomainMetadata
- Test helpers renamed to match (FakeExperienceRestartService →
  FakeDomainRestartService, ExperienceRegistrarTests →
  DomainRegistrarTests, ExperienceMetadataTests → DomainMetadataTests)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Slice 5 — Move `POC/experiences/` → `POC/domains/` + rename `Ino.Experiences.*` namespaces → `Ino.Domains.*`

**Goal.** Folder + namespace + assembly-name move. Largest diff of the PR. Done in two git commits inside the same slice to keep `git log --follow` reliable (folder move first, then content rewrite).

**Files:** every csproj, every `using Ino.Experiences.*` statement, every `namespace Ino.Experiences.*` declaration, `ino.slnx`, and string literals `"Ino.Experiences.Travel"` / `"Ino.Experiences.Taxi"` inside `DomainId.From(...)` calls.

### Steps

- [ ] **Step 5.1 — Rename folders with `git mv`**

```bash
git mv POC/experiences POC/domains

git mv POC/src/Ino.Experiences POC/src/Ino.Domains
git mv POC/src/Ino.Experiences.Host POC/src/Ino.Domains.Host

git mv POC/test/Ino.Experiences.Tests POC/test/Ino.Domains.Tests

# Bundle subfolders:
git mv POC/domains/travel/Ino.Experiences.Travel POC/domains/travel/Ino.Domains.Travel
git mv POC/domains/travel/Ino.Experiences.Travel.Contracts POC/domains/travel/Ino.Domains.Travel.Contracts
git mv POC/domains/taxi/Ino.Experiences.Taxi POC/domains/taxi/Ino.Domains.Taxi
git mv POC/domains/taxi/Ino.Experiences.Taxi.Contracts POC/domains/taxi/Ino.Domains.Taxi.Contracts

# csproj files:
git mv POC/src/Ino.Domains/Ino.Experiences.csproj POC/src/Ino.Domains/Ino.Domains.csproj
git mv POC/src/Ino.Domains.Host/Ino.Experiences.Host.csproj POC/src/Ino.Domains.Host/Ino.Domains.Host.csproj
git mv POC/test/Ino.Domains.Tests/Ino.Experiences.Tests.csproj POC/test/Ino.Domains.Tests/Ino.Domains.Tests.csproj
git mv POC/domains/travel/Ino.Domains.Travel/Ino.Experiences.Travel.csproj POC/domains/travel/Ino.Domains.Travel/Ino.Domains.Travel.csproj
git mv POC/domains/travel/Ino.Domains.Travel.Contracts/Ino.Experiences.Travel.Contracts.csproj POC/domains/travel/Ino.Domains.Travel.Contracts/Ino.Domains.Travel.Contracts.csproj
git mv POC/domains/taxi/Ino.Domains.Taxi/Ino.Experiences.Taxi.csproj POC/domains/taxi/Ino.Domains.Taxi/Ino.Domains.Taxi.csproj
git mv POC/domains/taxi/Ino.Domains.Taxi.Contracts/Ino.Experiences.Taxi.Contracts.csproj POC/domains/taxi/Ino.Domains.Taxi.Contracts/Ino.Domains.Taxi.Contracts.csproj
```

Verify the tree:

```bash
ls POC/domains/travel/ POC/domains/taxi/ POC/src/Ino.Domains/ POC/src/Ino.Domains.Host/
```

- [ ] **Step 5.2 — Commit the folder + csproj renames (first of two commits in this slice)**

```bash
git add -A
git commit -m "chore(poc): git-mv experiences/ → domains/ (folder + csproj rename only)"
```

The build is broken at this checkpoint (csproj paths in ino.slnx + `<ProjectReference>`s + `using` directives still point at old names). Next step restores it.

- [ ] **Step 5.3 — Rewrite `Ino.Experiences` → `Ino.Domains` in text files**

```bash
# All .cs, .csproj, .slnx files.
grep -rlZ --include='*.cs' --include='*.csproj' --include='*.slnx' \
    'Ino\.Experiences' POC/ \
  | xargs -0 sed -i 's/Ino\.Experiences/Ino.Domains/g'

# Verify zero remaining literal matches:
grep -rn --include='*.cs' --include='*.csproj' --include='*.slnx' \
    'Ino\.Experiences' POC/ || echo "clean"
```

Expected: `clean`.

- [ ] **Step 5.4 — Sanity check `ino.slnx` paths**

Read `POC/ino.slnx` and confirm every `<Project Path="…">` resolves to a real file:

```bash
awk -F'"' '/<Project Path=/ {print $2}' POC/ino.slnx \
  | while read p; do [ -f "POC/$p" ] && echo "OK $p" || echo "MISSING $p"; done
```

Expected: all lines start with `OK`. If any `MISSING`, revisit the folder moves.

- [ ] **Step 5.5 — Build + full test**

```bash
dotnet build POC/ino.slnx
dotnet test POC/ino.slnx
```

Expected: PASS. Every project compiles under the new namespace / path.

- [ ] **Step 5.6 — Aspire boot smoke**

```bash
aspire stop
aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated
# list_resources, Chrome smoke, aspire stop.
```

- [ ] **Step 5.7 — Commit the content rewrite (second of two commits in this slice)**

```bash
git add -A
git commit -m "$(cat <<'EOF'
refactor(poc): rename Ino.Experiences.* namespaces → Ino.Domains.*

Completes the folder rename (POC/experiences/ → POC/domains/) started
in the previous commit. Every namespace, assembly name, and
ProjectReference is now Ino.Domains.*. DomainId.From(...) arguments
updated to "Ino.Domains.Travel" / "Ino.Domains.Taxi" / testing
fixtures accordingly.

Orleans grain-class names change with the namespace
(Ino.Experiences.Travel.Neurons.FlightSearchNeuron →
Ino.Domains.Travel.Neurons.FlightSearchNeuron). This is safe because
POC clusters are ephemeral, installed.json stores DomainId strings,
and SystemFirePort / FirePort route by interface only
(see PR #14 cold-boot fix).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Slice 6 — `KernelSilo.Experiences` → `KernelSilo.Domains` + Aspire resource rename

**Goal.** The Aspire-visible resource name `experiences` → `domains`. Every dashboard UI label, `mcp__aspire__execute_resource_command(resourceName=…)` invocation, `PinToSiloStrategy` metadata, log string, and E2E selector that matches `experiences` must flip to `domains`.

**Files:**

- Modify: `POC/src/Ino.Core/KernelSilo.cs`
- Modify: every consumer of `KernelSilo.Experiences` (19 files per slice-0 grep)
- Modify: `POC/src/Ino.AppHost/Program.cs` (no explicit `"experiences"` string; uses `KernelSilo.Experiences.ToResourceName()`)
- Modify: `CLAUDE.md` (project-doc — rebuild-commands example) — handled in slice 10 docs sweep; this slice changes only source.

### Steps

- [ ] **Step 6.1 — Rename the enum value + mapping**

Edit `POC/src/Ino.Core/KernelSilo.cs`:

```csharp
namespace Ino.Core;

public enum KernelSilo { System, Identity, Domains }

public static class KernelSiloExtensions
{
    public static string ToResourceName(this KernelSilo silo) => silo switch
    {
        KernelSilo.System => "system",
        KernelSilo.Identity => "identity",
        KernelSilo.Domains => "domains",
        _ => throw new System.Diagnostics.UnreachableException($"Unknown silo: {silo}"),
    };
}
```

- [ ] **Step 6.2 — Rewrite every reference to `KernelSilo.Experiences`**

```bash
grep -rlZ --include='*.cs' 'KernelSilo\.Experiences' POC/ \
  | xargs -0 sed -i 's/KernelSilo\.Experiences/KernelSilo.Domains/g'

grep -rn --include='*.cs' 'KernelSilo\.Experiences' POC/ || echo "clean"
```

Expected: `clean`. Hits in `ExperiencesSiloConfigurator` (already renamed to `DomainsSiloConfigurator` in slice 4), AppHost, SystemSiloConfigurator, tests.

- [ ] **Step 6.3 — Build + full test**

```bash
dotnet build POC/ino.slnx
dotnet test POC/ino.slnx
```

Expected: PASS. If tests assert on `ToResourceName()` == `"experiences"`, fix them (there are a couple in `DiscoveryGrainTests`).

- [ ] **Step 6.4 — Aspire boot smoke — verify resource name change**

```bash
aspire stop
aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated
```

Use `mcp__aspire__list_resources` — confirm one resource has `display_name: "domains"` (not `"experiences"`). Confirm the other two are `"system"` and `"identity"`. The old `experiences` name must NOT appear anywhere in the resource list.

Chrome smoke the system URL; `aspire stop`.

- [ ] **Step 6.5 — E2E smoke**

```bash
INO_E2E_NO_BROWSER=true dotnet test POC/test/Ino.E2E.Tests --filter "Category=E2E"
```

Expected: all E2E tests green. If a test hard-codes `"experiences"` as a resource-name string, fix it to `"domains"`.

- [ ] **Step 6.6 — Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
refactor(poc): KernelSilo.Experiences → KernelSilo.Domains

The Aspire resource that hosts installed domains now renames from
"experiences" to "domains" in the dashboard + CLI commands.

Callers of mcp__aspire__execute_resource_command should use
resourceName="domains" (was "experiences"). CLAUDE.md docs sweep
lands in the next-but-one slice.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Slice 7 — Travel + Taxi declare their user-verb experiences

**Goal.** First real data against the new `IDomain.DeclaredExperiences` surface. Travel lists six experiences from Decision 13 in `product-vision-final.md`; Taxi lists one (scaffold). Inline `string[]` `PromptExamples` — the `BddPromptExamples.From(feature, scenario)` helper is slice 11.

**Files:**

- Modify: `POC/domains/travel/Ino.Domains.Travel/Travel.cs`
- Modify: `POC/domains/taxi/Ino.Domains.Taxi/Taxi.cs`
- Create: `POC/test/Ino.Core.Tests/TravelDeclaredExperiencesTests.cs` (optional — covered by `IDomainTests.Domain_can_declare_experiences` conceptually, but a Travel-specific integration gives observable doc value)
- Create: `POC/test/Ino.Domains.Tests/TaxiDeclaredExperiencesTests.cs` (minimal — one experience only)

### Steps

- [ ] **Step 7.1 — Write the Travel-experiences-exist test (failing)**

Create `POC/test/Ino.Core.Tests/TravelDeclaredExperiencesTests.cs`:

```csharp
using FluentAssertions;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Travel;
using Ino.Domains.Travel.Contracts;
using Xunit;

namespace Ino.Core.Tests;

public class TravelDeclaredExperiencesTests
{
    [Fact]
    public void Travel_declares_six_experiences_per_decision_13()
    {
        IDomain travel = new Travel();

        travel.DeclaredExperiences.Should().HaveCount(6);
    }

    [Fact]
    public void Travel_plan_trip_experience_points_at_PlanTripRequest()
    {
        IDomain travel = new Travel();

        var planTrip = travel.DeclaredExperiences
            .Single(e => e.Id == ExperienceId.From("travel.plan-trip"));

        planTrip.DisplayName.Should().Be("Plan a trip");
        planTrip.CanonicalSynapseType.Should().Be<PlanTripRequest>();
        planTrip.PromptExamples.Should().NotBeEmpty();
    }

    [Fact]
    public void Travel_find_flights_experience_points_at_FindFlightsRequest()
    {
        IDomain travel = new Travel();

        var findFlights = travel.DeclaredExperiences
            .Single(e => e.Id == ExperienceId.From("travel.find-flights"));

        findFlights.CanonicalSynapseType.Should().Be<FindFlightsRequest>();
    }
}
```

(`Ino.Core.Tests` already has a project reference to `Ino.Domains.Travel`; verify via `POC/test/Ino.Core.Tests/Ino.Core.Tests.csproj`. If not, add it:)

```xml
<ProjectReference Include="..\..\domains\travel\Ino.Domains.Travel\Ino.Domains.Travel.csproj" />
```

- [ ] **Step 7.2 — Run the failing test**

```bash
dotnet test POC/test/Ino.Core.Tests/Ino.Core.Tests.csproj --filter "FullyQualifiedName~TravelDeclaredExperiencesTests"
```

Expected: FAIL with `Expected travel.DeclaredExperiences to contain 6 item(s), but found 0.`

- [ ] **Step 7.3 — Populate `Travel.DeclaredExperiences`**

Edit `POC/domains/travel/Ino.Domains.Travel/Travel.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Travel.Contracts;

namespace Ino.Domains.Travel;

public sealed class Travel : IDomain
{
    public DomainId Id => DomainId.From("Ino.Domains.Travel");
    public string Version => "0.1.0";

    public IReadOnlyList<Capability> DeclaredCapabilities =>
    [
        new Capability.Llm(LlmTier.Default),
    ];

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
            CanonicalSynapseType: typeof(FindFlightsRequest),
            PromptExamples: [
                "find flights to bali",
                "cheapest flight from berlin to tokyo"
            ]),
        new Experience(
            ExperienceId.From("travel.find-hotels"),
            DisplayName: "Find hotels",
            Description: "Search hotels by rating, price, and your amenity preferences.",
            CanonicalSynapseType: typeof(FindHotelsRequest),
            PromptExamples: [
                "find a hotel in bali",
                "hotels near shibuya for 3 nights"
            ]),
        new Experience(
            ExperienceId.From("travel.find-places"),
            DisplayName: "Find things to do",
            Description: "Suggest activities and places to visit at your destination.",
            CanonicalSynapseType: typeof(FindPlacesRequest),
            PromptExamples: [
                "things to do in bali",
                "what's good to see in lisbon"
            ]),
        new Experience(
            ExperienceId.From("travel.monitor-flight"),
            DisplayName: "Monitor a flight",
            Description: "Watch for delays or gate changes and notify when they happen.",
            CanonicalSynapseType: typeof(ArmFlightMonitor),
            PromptExamples: [
                "watch my flight",
                "let me know if BA123 is delayed"
            ]),
        new Experience(
            ExperienceId.From("travel.flight-delayed"),
            DisplayName: "Flight-delayed notification",
            Description: "Reactive notification when a monitored flight is delayed.",
            CanonicalSynapseType: typeof(FlightDelayed),
            PromptExamples: [
                // Reactive — user doesn't chat to trigger this; examples are
                // for discoverability in the inspector only.
                "notify me when my flight is delayed"
            ]),
    ];
}
```

(6 experiences. Decision 13 names more — HotelSearch / PlaceSearch / RestaurantSearch / TransportPlanner / preference models / monitors / memory / booking / export — but those neurons are not all implemented yet. List what maps to a real existing canonical-synapse type.)

- [ ] **Step 7.4 — Populate `Taxi.DeclaredExperiences`**

Edit `POC/domains/taxi/Ino.Domains.Taxi/Taxi.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Taxi.Contracts;

namespace Ino.Domains.Taxi;

public sealed class Taxi : IDomain
{
    public DomainId Id => DomainId.From("Ino.Domains.Taxi");
    public string Version => "0.1.0";

    public IReadOnlyList<Capability> DeclaredCapabilities =>
    [
        new Capability.Llm(LlmTier.Default),
    ];

    public IReadOnlyList<IExperience> DeclaredExperiences =>
    [
        new Experience(
            ExperienceId.From("taxi.find-ride"),
            DisplayName: "Find a ride",
            Description: "Hail a ride to a destination (scaffold — Uber MCP integration pending).",
            CanonicalSynapseType: typeof(FindRideRequest),
            PromptExamples: [
                "get me a ride",
                "book a taxi to the airport",
                "call an uber"
            ]),
    ];
}
```

- [ ] **Step 7.5 — Verify Travel + Taxi tests pass**

```bash
dotnet test POC/ino.slnx
```

Expected: PASS. Prior 3 new tests green; no regressions.

- [ ] **Step 7.6 — Aspire boot smoke**

```bash
aspire stop
aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated
# list_resources; Chrome; aspire stop.
```

Travel demo in the browser still routes — `DeclaredExperiences` is metadata only; Cortex still uses keyword routing.

- [ ] **Step 7.7 — Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
feat(poc): Travel + Taxi declare user-verb experiences

Populates IDomain.DeclaredExperiences for the v0.1 domains:
- Travel (6 experiences: plan-trip, find-flights, find-hotels,
  find-places, monitor-flight, flight-delayed) — each points at a
  real canonical synapse type
- Taxi (1 experience: find-ride) — scaffold

PromptExamples are inline string[]. The BddPromptExamples.From(feature,
scenario) helper is deferred to slice 11 (optional).

Cortex routing still uses keyword match — DumpExperiencesAsync
(slice 8) lets the inspector surface the experience catalog but
does not change the routing path yet.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Slice 8 — `IDiscovery.DumpExperiencesAsync` + `DomainRegistrar` aggregation

**Goal.** Surface the union of `DeclaredExperiences` from all installed domains through Discovery so Track 3's Cortex navigation UI (future) has a catalog to read. No behaviour change to routing.

**Files:**

- Create: `POC/src/Ino.Core.Hosting/ExperienceCatalog.cs` (new record)
- Modify: `POC/src/Ino.System/IDiscovery.cs`
- Modify: `POC/src/Ino.System/Discovery.cs`
- Modify: `POC/src/Ino.System/IDiscoveryClient.cs`
- Modify: `POC/src/Ino.System/DiscoveryClient.cs`
- Modify: `POC/src/Ino.System/DomainRegistrar.cs` (aggregates experiences)
- Modify: `POC/src/Ino.System/RegistrationHostedService.cs` (passes aggregated list)
- Modify: `POC/src/Ino.System/SiloRegistration.cs` (carries the experience list)
- Create: `POC/test/Ino.System.Tests/DiscoveryExperienceCatalogTests.cs`

### Steps

- [ ] **Step 8.1 — Write the catalog test (failing)**

Create `POC/test/Ino.System.Tests/DiscoveryExperienceCatalogTests.cs`:

```csharp
using FluentAssertions;
using Ino.Core;
using Ino.Core.Hosting;
using Xunit;

namespace Ino.System.Tests;

public class DiscoveryExperienceCatalogTests
{
    [Fact]
    public void DomainRegistrar_aggregates_DeclaredExperiences_from_all_domains()
    {
        var a = new FakeDomainA();
        var b = new FakeDomainB();

        var reg = DomainRegistrar.Build(new RegistrationOptions
        {
            Silo = KernelSilo.Domains,
            Domains = [a, b],
        });

        reg.Experiences.Select(e => e.Id.Value)
            .Should().BeEquivalentTo(new[] { "a.verb", "b.verb" });
    }

    private sealed class FakeDomainA : IDomain
    {
        public DomainId Id => DomainId.From("Ino.Testing.A");
        public string Version => "1.0.0";
        public IReadOnlyList<Capability> DeclaredCapabilities => [];
        public IReadOnlyList<IExperience> DeclaredExperiences =>
        [
            new Experience(ExperienceId.From("a.verb"), "A verb", "desc",
                typeof(object), ["do a"]),
        ];
    }

    private sealed class FakeDomainB : IDomain
    {
        public DomainId Id => DomainId.From("Ino.Testing.B");
        public string Version => "1.0.0";
        public IReadOnlyList<Capability> DeclaredCapabilities => [];
        public IReadOnlyList<IExperience> DeclaredExperiences =>
        [
            new Experience(ExperienceId.From("b.verb"), "B verb", "desc",
                typeof(object), ["do b"]),
        ];
    }
}
```

- [ ] **Step 8.2 — Run failing test**

```bash
dotnet test POC/test/Ino.System.Tests/Ino.System.Tests.csproj --filter "FullyQualifiedName~DiscoveryExperienceCatalogTests"
```

Expected: FAIL — `reg.Experiences` doesn't exist.

- [ ] **Step 8.3 — Add `Experiences` to `SiloRegistration`**

Read the current `SiloRegistration` record (likely `POC/src/Ino.System/SiloRegistration.cs` — if it lives elsewhere, `grep -rn "record SiloRegistration" POC/`). Append a fourth property:

```csharp
// (exact file may need adjustment — confirm with grep.)
using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.System;

[GenerateSerializer]
public sealed record SiloRegistration(
    [property: Id(0)] KernelSilo Silo,
    [property: Id(1)] IReadOnlyList<CanonicalRegistration> Canonical,
    [property: Id(2)] IReadOnlyList<ReactiveRegistration> Reactive,
    [property: Id(3)] IReadOnlyList<IExperience> Experiences);
```

If adding the fourth parameter breaks the positional constructor callers in tests (they use `new SiloRegistration(silo, canonicals, reactives)` without `Experiences`), add a second constructor:

```csharp
public SiloRegistration(KernelSilo silo, IReadOnlyList<CanonicalRegistration> canonical, IReadOnlyList<ReactiveRegistration> reactive)
    : this(silo, canonical, reactive, Array.Empty<IExperience>()) { }
```

(or update the tests — whichever shorter diff.)

- [ ] **Step 8.4 — Populate `Experiences` in `DomainRegistrar.Build`**

Edit `POC/src/Ino.System/DomainRegistrar.cs` — at the end of `Build`, before returning, aggregate experiences from each domain:

```csharp
var experiences = options.Domains
    .SelectMany(d => d.DeclaredExperiences)
    .ToArray();

return new SiloRegistration(options.Silo, canonicals, reactives, experiences);
```

- [ ] **Step 8.5 — Run failing test again**

```bash
dotnet test POC/test/Ino.System.Tests/Ino.System.Tests.csproj --filter "FullyQualifiedName~DiscoveryExperienceCatalogTests"
```

Expected: PASS.

- [ ] **Step 8.6 — Add `DumpExperiencesAsync` to `IDiscovery`**

Edit `POC/src/Ino.System/IDiscovery.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.System;

public interface IDiscovery : IGrainWithIntegerKey
{
    Task RegisterAsync(SiloRegistration registration, CancellationToken ct = default);
    Task<CanonicalTarget?> LookupCanonicalAsync(Type synapseType, CancellationToken ct = default);
    Task<IReadOnlyList<ReactiveTarget>> LookupReactiveAsync(Type synapseType, CancellationToken ct = default);
    Task<DiscoveryDump> DumpAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IExperience>> DumpExperiencesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 8.7 — Implement `DumpExperiencesAsync` in `Discovery`**

Edit `POC/src/Ino.System/Discovery.cs`:

1. Add a field: `private readonly Dictionary<KernelSilo, IReadOnlyList<IExperience>> _experiencesBySilo = new();`
2. In `RegisterAsync`, after clearing the silo's old entries, store `registration.Experiences`:

   ```csharp
   _experiencesBySilo[registration.Silo] = registration.Experiences;
   ```

3. In `ClearEntriesForSilo`, add: `_experiencesBySilo.Remove(silo);`
4. Add the dump method:

   ```csharp
   public Task<IReadOnlyList<IExperience>> DumpExperiencesAsync(CancellationToken ct = default)
   {
       var aggregate = _experiencesBySilo.Values
           .SelectMany(v => v)
           .ToArray();
       return Task.FromResult<IReadOnlyList<IExperience>>(aggregate);
   }
   ```

- [ ] **Step 8.8 — Mirror on `IDiscoveryClient` + `DiscoveryClient`**

`IDiscoveryClient` is the Orleans-call wrapper (in-silo facade). Add the same method signature, implement it as a thin grain-factory call. (Read `DiscoveryClient.cs` to mirror the surrounding patterns — one-liner per method.)

- [ ] **Step 8.9 — Write an end-to-end Discovery test**

Append to `POC/test/Ino.System.Tests/DiscoveryExperienceCatalogTests.cs`:

```csharp
    [Fact]
    public async Task DumpExperiencesAsync_returns_aggregate_across_silos()
    {
        // Test the Discovery grain directly using the in-process test cluster
        // from Ino.Testing. See DiscoveryGrainTests for the TestCluster pattern.
        // (Exact test harness detail — follow the existing DiscoveryGrainTests
        //  setup precisely; keep the test asserting: two domains with one
        //  experience each produces a 2-item catalog.)
    }
```

(Flesh out against `DiscoveryGrainTests.cs` as the template — it's the only harness that constructs a TestCluster in this project. Keep the sketch honest: add test content that compiles, or skip this step if the TestCluster harness doesn't trivially accept the added registration field.)

- [ ] **Step 8.10 — Extend `SynapseFired` envelope with optional `Experience` field (spec §Ripple effects / Telemetry)**

Edit `POC/src/Ino.System/SystemFirePort.cs` → `PublishEvent` method. The envelope dictionary currently has `SequenceNumber`, `SynapseVerb`, `TargetId`, `CorrelationId`, `Decay`, and conditional `ReasoningSource`/`Scenario`/`Feature`. Add an optional `Experience` key:

```csharp
// Optional — attached when the caller's NeuronContext carries an
// ExperienceId. Track 1 adds the envelope slot but doesn't populate
// it: Cortex still routes by keyword, so ExperienceId remains null in
// v0.1. Populated in Track 3 when Cortex actually matches experiences.
if (caller.ExperienceId is { } expId)
{
    envelope["Experience"] = expId.Value;
}
```

`NeuronContext.ExperienceId` doesn't exist yet — add it as an optional record field:

```csharp
// POC/src/Ino.Core.Hosting/NeuronContext.cs — add field:
public sealed record NeuronContext(
    // … existing fields …
    ExperienceId? ExperienceId = null);
```

(Exact signature depends on the current `NeuronContext` — add the new optional field at the end of the record parameter list so existing `with` expressions don't break.)

Update `NeuronContextSurrogate` (cross-silo serialization surrogate) to carry the new field — if surrogate currently copies fields one-by-one, add `ExperienceId`; if it uses a reflection-style copy, no change needed.

Flutter side: `state/timeline_bloc.dart::_fromInoEvent` currently reads `Scenario`/`Feature`/`ReasoningSource`. Add a read for the new `Experience` key:

```dart
// lib/state/timeline_bloc.dart _fromInoEvent — alongside the existing Scenario / Feature reads:
experienceId: payload['Experience'] as String?,
```

And extend the `TimelineEntry` / inspector's Reasoning panel model with the new field. (Wire-shape change — update both ends together per the file's own warning.)

If the Flutter change is more than trivially small (multiple files, BLoC transitions to extend), split it into slice 8.10b as its own commit. Otherwise keep together.

- [ ] **Step 8.11 — Full build + test**

```bash
dotnet build POC/ino.slnx
dotnet test POC/ino.slnx
```

Expected: PASS.

- [ ] **Step 8.12 — Aspire boot smoke + Chrome**

```bash
aspire stop
aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated
# list_resources; Chrome to system URL, confirm onboarding renders, confirm
# Trace view still works (no envelope-parsing regressions); aspire stop.
```

- [ ] **Step 8.13 — Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
feat(poc): IDiscovery.DumpExperiencesAsync + SynapseFired envelope Experience slot

- SiloRegistration carries the installed domains' DeclaredExperiences;
  Discovery stores them per-silo and exposes the union via
  DumpExperiencesAsync. DomainRegistrar.Build populates the field.
- NeuronContext gains optional ExperienceId; SystemFirePort.PublishEvent
  writes it into the SynapseFired envelope when present. Flutter Trace
  view reads the new key (backward-compatible: null-safe).

Cortex routing unchanged — catalog + envelope field are metadata-only,
consumed by the inspector (future Track 3 nav-UI slice). v0.1 sets
ExperienceId = null on every fire since keyword routing doesn't
resolve to a specific IExperience yet.

Tests added: DomainRegistrar_aggregates_DeclaredExperiences_from_all_domains +
DumpExperiencesAsync_returns_aggregate_across_silos.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Slice 9 — Marketplace feed reshape + `GET /marketplace/installed/{domainId}/experiences`

**Goal.** External JSON reshape: `MarketplaceFeed.Experiences` → `.Domains`, feed entries carry an `experiences: [...]` array. New endpoint `GET /marketplace/installed/{domainId}/experiences` returns the live domain's `DeclaredExperiences`.

**Files:**

- Modify: `POC/src/Ino.Aspire.Hosting/MarketplaceFeed.cs`
- Modify: `POC/src/Ino.System/MarketplaceController.cs`
- Modify: the feed JSON file shipped at `~/.ino/marketplace.json` (or whatever the default path is — `InoPaths.MarketplaceJson`?) — seed fixture
- Modify: `POC/test/Ino.System.Tests/MarketplaceControllerTests.cs`
- Modify: `POC/test/Ino.E2E.Tests/InstallFlowTests.cs` (if it asserts the old shape)

### Steps

- [ ] **Step 9.1 — Reshape `MarketplaceFeed` + `MarketplaceFeedEntry`**

Edit `POC/src/Ino.Aspire.Hosting/MarketplaceFeed.cs`:

```csharp
using Ino.Core;

namespace Ino.Aspire.Hosting;

public sealed record MarketplaceFeed(IReadOnlyList<MarketplaceFeedEntry> Domains);
public sealed record MarketplaceFeedEntry(
    DomainId Id,
    string Description,
    string Version,
    IReadOnlyList<MarketplaceExperienceMetadata> Experiences);

public sealed record MarketplaceExperienceMetadata(
    ExperienceId Id,
    string DisplayName,
    string Description);
```

(`MarketplaceExperienceMetadata` is a wire-only projection — the `Type CanonicalSynapseType` field on `IExperience` is a runtime concept, not serialized.)

- [ ] **Step 9.2 — Update the seed feed JSON**

The JSON feed file path is resolved via `MarketplaceControllerOptions.MarketplaceFeedPath`. Read that file on your machine, then rewrite:

```json
{
  "domains": [
    {
      "id": "Ino.Domains.Travel",
      "version": "0.1.0",
      "description": "Trip planning, flights, hotels, places to visit.",
      "experiences": [
        { "id": "travel.plan-trip", "displayName": "Plan a trip", "description": "Build an itinerary with flights, hotels, and things to do." },
        { "id": "travel.find-flights", "displayName": "Find flights", "description": "Search flights ranked by your learned preferences." },
        { "id": "travel.find-hotels", "displayName": "Find hotels", "description": "Search hotels by rating, price, and your amenity preferences." },
        { "id": "travel.find-places", "displayName": "Find things to do", "description": "Suggest activities and places to visit at your destination." },
        { "id": "travel.monitor-flight", "displayName": "Monitor a flight", "description": "Watch for delays or gate changes and notify when they happen." },
        { "id": "travel.flight-delayed", "displayName": "Flight-delayed notification", "description": "Reactive notification when a monitored flight is delayed." }
      ]
    },
    {
      "id": "Ino.Domains.Taxi",
      "version": "0.1.0",
      "description": "Ride hailing (scaffold — Uber MCP integration pending).",
      "experiences": [
        { "id": "taxi.find-ride", "displayName": "Find a ride", "description": "Hail a ride to a destination (scaffold — Uber MCP integration pending)." }
      ]
    }
  ]
}
```

(Locate the file — look at `MarketplaceControllerOptions` for the path; likely `POC/src/Ino.System/marketplace.json` or similar. If it doesn't ship in the repo today, create one under `POC/src/Ino.System.Host/marketplace.json` and update `Program.cs` / options to point at it.)

- [ ] **Step 9.3 — Update `MarketplaceController` JSON reads + responses**

Edit `POC/src/Ino.System/MarketplaceController.cs`:

- Swap `feed.Experiences.FirstOrDefault(e => e.Id == bundleId)` → `feed.Domains.FirstOrDefault(d => d.Id == domainId)` (and rename local `bundleId` → `domainId`).
- Swap the HTTP route parameter `{id}` behaviour: semantically unchanged, but the variable name must reflect `DomainId` now.
- Register `DomainIdJsonConverter` + `ExperienceIdJsonConverter` in the `JsonSerializerOptions` block.
- Replace `BundleIdJsonConverter` references with `DomainIdJsonConverter` (caught in slice 2, re-verify).
- `GetInstalled()` returns `{ installed: string[] }` — unchanged. Keep.
- Add the new endpoint:

  ```csharp
  [HttpGet("installed/{id}/experiences")]
  public async Task<ActionResult> GetInstalledExperiences(string id, CancellationToken ct)
  {
      var domainId = DomainId.From(id);
      var installed = InstalledSet.Load(options.Value.InstalledStatePath);
      if (!installed.Contains(domainId))
          return NotFound(new { status = "not_installed", id });

      var experiences = await grains.GetDiscovery().DumpExperiencesAsync(ct);
      // Filter to the caller's domain by matching the experience's
      // CanonicalSynapseType against the known canonical-registration bundle.
      // Simpler v0.1: assume every Discovery experience whose ExperienceId
      // starts with the domain's short name belongs to that domain.
      // Cleaner: join to CanonicalRegistration to look up the domain per
      // synapse type. Implement the clean version:
      var dump = await grains.GetDiscovery().DumpAsync(ct);
      var synapseTypesForDomain = dump.Canonical
          .Where(c => c.Domain == domainId)
          .Select(c => c.SynapseType)
          .ToHashSet();
      var scoped = experiences
          .Where(e => synapseTypesForDomain.Contains(e.CanonicalSynapseType))
          .Select(e => new { id = e.Id.Value, displayName = e.DisplayName, description = e.Description })
          .ToArray();

      return Ok(new { domainId = domainId.Value, experiences = scoped });
  }
  ```

  (`grains.GetDiscovery()` is already present — existing `GET /discovery/table` uses it.)

- [ ] **Step 9.4 — Update `MarketplaceControllerTests`**

Find all `feed.Experiences` / `MarketplaceFeed(...)` construction sites in `MarketplaceControllerTests.cs` and flip them to the new shape. Add a new test:

```csharp
[Fact]
public async Task GetInstalledExperiences_returns_declared_experiences_of_installed_domain()
{
    // Arrange: install Travel (or a fake domain) so InstalledSet contains it;
    // Discovery is seeded with Travel's DeclaredExperiences via a Build-and-
    // Register call. (Follow the existing MarketplaceControllerTests arrangement
    // patterns — don't invent a new harness.)
    // Act: HTTP GET /marketplace/installed/Ino.Domains.Travel/experiences
    // Assert: 200 with experiences array whose ids start with "travel."
}
```

(Exact arrangement follows the existing harness — if the test file uses `WebApplicationFactory`, extend; if it uses the minimal `MarketplaceController` ctor directly, thread the needed `IGrainFactory` fake.)

- [ ] **Step 9.5 — Update `InstallFlowTests` if it hard-codes the old shape**

```bash
grep -n '"experiences"\|feed\.Experiences\|MarketplaceFeed(' POC/test/Ino.E2E.Tests/InstallFlowTests.cs
```

For each hit, rewrite to the new shape (`"domains"` key, `.Domains` property).

- [ ] **Step 9.6 — Full build + test**

```bash
dotnet build POC/ino.slnx
dotnet test POC/ino.slnx
```

Expected: PASS.

- [ ] **Step 9.7 — Aspire boot + wire check**

```bash
aspire stop
aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated
```

Hit the new endpoint directly:

```bash
curl -sk https://localhost:<system-port>/marketplace/available | jq .
# Expected: { "domains": [ ... ] }  (NOT { "experiences": [...] })

curl -sk https://localhost:<system-port>/marketplace/installed | jq .
# Expected: { "installed": ["Ino.Domains.Travel", ...] }

curl -sk https://localhost:<system-port>/marketplace/installed/Ino.Domains.Travel/experiences | jq .
# Expected: { "domainId": "Ino.Domains.Travel", "experiences": [ ... 6 items ... ] }
```

- [ ] **Step 9.8 — E2E smoke**

```bash
INO_E2E_NO_BROWSER=true dotnet test POC/test/Ino.E2E.Tests --filter "Category=E2E"
```

Expected: green.

- [ ] **Step 9.9 — Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
feat(poc): marketplace feed reshape + /installed/{id}/experiences endpoint

- MarketplaceFeed.Experiences → .Domains; MarketplaceFeedEntry now
  carries an experiences: [...] array per domain (metadata-only).
- New HTTP endpoint GET /marketplace/installed/{domainId}/experiences
  returns the live DeclaredExperiences of an installed domain, scoped
  via Discovery's canonical registrations (consumer: Track 3 Cortex
  nav UI).
- marketplace.json seed file rewritten to the new shape.
- Integration + E2E tests updated.

Wire-format change: any external consumer of /marketplace/available
must read .domains (was .experiences). Install/uninstall endpoints
and installed.json format are unchanged.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Slice 10 — Docs sweep

**Goal.** `CLAUDE.md`, `POC/README.md`, root `README.md`, `docs/product-vision-final.md`, `docs/plan-poc-phase-3.md` all use the spec's §Vocabulary rule: *domain* = installable bundle; *experience* = user-verb; *bundle* deleted where it meant "domain".

**Files:**

- Modify: `CLAUDE.md` (root)
- Modify: `POC/README.md`
- Modify: root `README.md`
- Modify: `docs/product-vision-final.md`
- Modify: `docs/plan-poc-phase-3.md`
- (Maybe) Modify: `POC/docs/prototypes/02-experience-catalog.html` — creator-facing prototype doc. If it says "Experience bundle", rewrite to "Domain" there too. If it describes user-verbs (IExperience), leave as "Experience".

### Steps

- [ ] **Step 10.1 — `CLAUDE.md` rewrite**

Read `CLAUDE.md`. Rewrite every mention of "experience" that means *bundle* to "domain". Rewrite every path like `POC/experiences/…` to `POC/domains/…`. Specific sections to hit:
- "Primitives" block — reference to `IExperience` bundles → `IDomain` bundles.
- "Project layout" table — `Ino.Experiences` → `Ino.Domains`.
- "Experiences — the v0.1 set" heading — rename to "Domains — the v0.1 set".
- `mcp__aspire__execute_resource_command(resourceName="experiences", …)` example → `resourceName="domains"`.
- "Experience" as a word — if it now means user-verb (rare in CLAUDE.md), keep; otherwise rename.

- [ ] **Step 10.2 — `POC/README.md` rewrite**

Same vocabulary rule. Key sections that mention "IExperience bundles via `WithExperience<T>()`" — rewrite to "IDomain bundles via `WithDomain<T>()`". `IExperienceRestartService` → `IDomainRestartService`.

- [ ] **Step 10.3 — Root `README.md` rewrite**

Likely smaller surface — the vision pitch. Rewrite any reference to "experience bundle" / "experience" where it meant bundle.

- [ ] **Step 10.4 — `docs/product-vision-final.md` rewrite**

Decisions 2, 3, 4, 8, 11, 13, 14 heavily use "experience" for bundle. Under the new rule:
- "Experience" in Decisions 2, 3, 13 that means *bundle* → "Domain".
- "Experience" in Decisions 8, 14 that means *user-verb surface* (e.g., "Travel can: plan trips · find flights · …") → keep — Decision 8 explicitly retained in the spec.

Re-read each decision and apply the rule deliberately. Also rewrite paths in Decision 2 ("Port forward into POC for v0.1" references `domains/travel/Ino.Travel/` → `domains/travel/Ino.Domains.Travel/`).

- [ ] **Step 10.5 — `docs/plan-poc-phase-3.md` rewrite**

Same rule. Slice 14 ("Marketplace tile") references `Ino.Travel` — rewrite to `Ino.Domains.Travel`.

- [ ] **Step 10.6 — Prototype doc**

```bash
grep -n 'experience\|Experience' POC/docs/prototypes/02-experience-catalog.html
```

For each hit, classify (bundle vs verb) and rewrite accordingly. If the file describes the concept of "user-verbs as an experience catalog", rename the file:

```bash
git mv POC/docs/prototypes/02-experience-catalog.html POC/docs/prototypes/02-experience-catalog.html
# (Filename describes user-verb experiences, not bundles — keep as-is.)
```

- [ ] **Step 10.7 — Final grep sanity**

Using the acceptance-criteria grep from the spec:

```bash
grep -rn "BundleId\b\|WithExperience<\|ExperienceRegistrar\b\|IExperienceRestartService\b\|NullExperienceRestartService\b\|FakeExperienceRestartService\b\|ExperiencesSiloConfigurator\b\|RegisteredExperiences\b\|RegisterExperience\b\|IDomain\.Bundle\b" POC/

grep -rn "Ino\.Experiences\." POC/
```

Expected: both empty.

- [ ] **Step 10.8 — Build + test (docs-only change, build shouldn't care but run anyway)**

```bash
dotnet build POC/ino.slnx
dotnet test POC/ino.slnx
```

- [ ] **Step 10.9 — Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
docs: sweep domain/experience vocabulary to spec §1 rule

CLAUDE.md, POC/README.md, README.md, docs/product-vision-final.md,
docs/plan-poc-phase-3.md now use:
- "domain" for an installable bundle (IDomain)
- "experience" for a single user-verb inside a domain (IExperience)
- no "bundle" language where it meant "domain"

Aspire rebuild-command examples in CLAUDE.md switch resourceName
"experiences" → "domains". Path references follow the Ino.Experiences.*
→ Ino.Domains.* rename.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Slice 11 (optional) — `BddPromptExamples.From(feature, scenario)` helper

**Goal.** Single source of truth for prompt examples: the `.feature` scenario steps. Domains use `PromptExamples: BddPromptExamples.From("PlanTripRoute", "user asks for a multi-day itinerary")` instead of inline `string[]`. Only lands if `Ino.Llm` has a scenario-loader surface.

**Skip this slice entirely if:** inspecting `Ino.Core.Hosting/Llm/` (the `Ino.Llm` namespace) shows no ready scenario-loader primitive. In that case, file a follow-up issue: *"Track 1 follow-up: extract BddPromptExamples.From helper once Ino.Llm has a scenario loader"* and move on.

### Steps

- [ ] **Step 11.1 — Check `Ino.Llm` for a scenario loader**

```bash
grep -rn --include='*.cs' 'ScenarioLoader\|BddScenarioLoader\|LoadScenario' POC/src/Ino.Core.Hosting/Llm/
```

If there is a `BddScenarioLoader` or equivalent — continue with 11.2. If not, skip to 11.6.

- [ ] **Step 11.2 — Write the helper test (failing)**

Create `POC/test/Ino.Core.Tests/BddPromptExamplesTests.cs` (or in the test project that already tests `Ino.Llm`):

```csharp
using FluentAssertions;
using Ino.Core.Hosting.Llm;
using Xunit;

namespace Ino.Core.Tests;

public class BddPromptExamplesTests
{
    [Fact]
    public void From_returns_every_Given_user_says_step_from_the_scenario()
    {
        var examples = BddPromptExamples.From(
            feature: "PlanTripRoute",
            scenario: "user asks for a multi-day itinerary");

        examples.Should().NotBeEmpty();
        examples.Should().AllSatisfy(s => s.Should().NotBeNullOrWhiteSpace());
    }
}
```

- [ ] **Step 11.3 — Implement `BddPromptExamples.From`**

Create `POC/src/Ino.Core.Hosting/Llm/BddPromptExamples.cs`:

```csharp
namespace Ino.Core.Hosting.Llm;

public static class BddPromptExamples
{
    public static IReadOnlyList<string> From(string feature, string scenario)
    {
        // Delegate to the existing BddScenarioLoader primitive — thread the
        // feature + scenario through its API and extract the example strings
        // from every `Given a user says "…"` step.
        //
        // Exact API: TBD against the actual scenario-loader surface.
        // Placeholder below is a sketch; replace with real loader call.
        var scenarioContent = BddScenarioLoader.LoadExampleStrings(feature, scenario);
        return scenarioContent.ToArray();
    }
}
```

(If the placeholder doesn't compile against the actual `BddScenarioLoader` shape, inline-fix. This is the one slice where a placeholder is honest — the API of the loader is unknown at the time this plan is being written.)

- [ ] **Step 11.4 — Swap Travel to use the helper**

Only for experiences whose `.feature` scenarios exist. Pass an inline `string[]` fallback for experiences that don't have a scenario yet.

- [ ] **Step 11.5 — Build + test + commit**

```bash
dotnet build POC/ino.slnx
dotnet test POC/ino.slnx

git add -A
git commit -m "$(cat <<'EOF'
feat(poc): BddPromptExamples.From helper — .feature scenarios drive PromptExamples

One source of truth for a user-verb's prompt shape: the BDD scenario.
BddMockChatClient and IExperience.PromptExamples now read from the
same .feature file.

Travel's plan-trip + find-flights + find-hotels switch from inline
string[] to BddPromptExamples.From. Verbs without a .feature stay
inline until a scenario is written.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 11.6 — If skipping (no loader available), file follow-up issue**

```bash
gh issue create --title "Track 1 follow-up: BddPromptExamples.From helper" \
  --body "Per docs/superpowers/specs/2026-04-23-domain-experience-vocabulary-design.md §Ripple effects → BDD scenarios as source of truth for prompt examples, once Ino.Llm exposes a scenario-loader surface, extract BddPromptExamples.From(feature, scenario) and swap Travel / Taxi / future domains off inline string[] PromptExamples."
```

---

## Final verification — PR acceptance criteria

Once slice 10 (and optionally 11) is committed, run the spec's acceptance block:

- [ ] `dotnet build POC/ino.slnx` — green.
- [ ] `dotnet test POC/ino.slnx` — all green. New tests added across slices 3, 7, 8, 9: `ExperienceIdTests` (4), user-verb `IExperienceTests` (2), `IDomainTests.Default_DeclaredExperiences_is_empty` + `Domain_can_declare_experiences` (2), `TravelDeclaredExperiencesTests` (3), `DiscoveryExperienceCatalogTests` (2), `MarketplaceController.GetInstalledExperiences` (1) = 14+ new passing tests.
- [ ] `aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated` — all three silos Healthy (`system`, `identity`, `domains`).
- [ ] System-silo HTTPS URL — Flutter onboarding renders, `Run Travel` demo routes, zero console errors.
- [ ] `curl -sk https://localhost:<system-port>/marketplace/available | jq .` returns `{"domains":[...]}` with the new shape.
- [ ] `curl -sk https://localhost:<system-port>/marketplace/installed/Ino.Domains.Travel/experiences | jq .` returns `{"domainId":"Ino.Domains.Travel","experiences":[...6 items...]}`.
- [ ] `grep -rn "BundleId\b\|WithExperience<\|ExperienceRegistrar\b\|IExperienceRestartService\b\|NullExperienceRestartService\b\|FakeExperienceRestartService\b\|ExperiencesSiloConfigurator\b\|RegisteredExperiences\b\|RegisterExperience\b\|IDomain\.Bundle\b" POC/` returns zero matches.
- [ ] `grep -rn "Ino\.Experiences\." POC/` returns zero matches.
- [ ] The only symbols starting with `Experience` in `POC/` source are `IExperience`, `Experience` (record), `ExperienceId`, `ExperienceIdJsonConverter`, plus test symbols (e.g. `IExperienceTests`, `TravelDeclaredExperiencesTests`).
- [ ] Docs (`CLAUDE.md`, `POC/README.md`, `README.md`, `docs/product-vision-final.md`, `docs/plan-poc-phase-3.md`) use the §1 vocabulary rule exclusively.

No feature regression is acceptable — the PR is a rename + additive contract extension only.

---

## Risks + mitigations

| Risk | Mitigation |
|---|---|
| `sed -i` behaves differently on Windows / macOS / Linux — BSD sed needs `-i ''`, Windows git-bash's sed is usually GNU-compatible | In Windows git-bash, the commands here work verbatim. On macOS, `sed -i '' 's/.../.../g'` or use `perl -pi -e 's/.../.../g'`. |
| Orleans serializer: adding `Experiences` to `SiloRegistration` (new `[Id(3)]`) — cross-silo call between a slice-8 grain and a pre-slice-8 grain would fail | Impossible in practice: the POC rebuilds every silo at every `aspire start`, and slice 8 rebuilds all three. Add a comment on the record clarifying the invariant. |
| A test file with a hard-coded `"experiences"` string (dashboard resource name, path, etc.) fails quietly after slice 6 | Slice 6 mandates E2E smoke; a resource-name mismatch shows up as test failure or aspire resource missing. |
| `MarketplaceController.GetInstalledExperiences` implementation in slice 9 assumes `CanonicalRegistration` has a `Domain` property — it does after slice 2, but if slice 2's `Bundle → Domain` rename was incomplete on that record, slice 9 breaks | Slice 2 step 2.6 explicitly renames `CanonicalRegistration.Bundle` → `.Domain`; re-verify with `grep -n "\.Bundle" POC/src/Ino.System/Discovery.cs` before slice 9 starts. |
| Flutter generated Windows-plugin files regenerate on every build and dirty the working tree | Known, harmless — they're in `.gitignore` on a clean repo, or can be reset with `git checkout -- POC/clients/ino.flutter/windows/flutter/` if they accidentally get staged. Don't commit them. |
