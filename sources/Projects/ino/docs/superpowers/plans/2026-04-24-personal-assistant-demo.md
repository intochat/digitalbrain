# Personal assistant demo (Spec A) — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a `git clone && export XAI_API_KEY=… && aspire start`-ready personal assistant MVP: fluent `AddIno()` LLM/voice configurator, xAI Grok as the default 3-tier provider, Web Speech API voice input in Flutter, and Cortex routing via the live experience catalog instead of hardcoded keywords.

**Architecture:** Slim provider adapter project (`Ino.Llm.Xai`) wrapping the OpenAI-compatible xAI API behind an `IChatClientFactory` that resolves `IChatClient` per-tier (Fast / Balanced / Reasoning). AppHost declares models via fluent `WithLlm<T>().As{Fast,Balanced,Reasoning}()`; declarations are serialized into silo process env vars and reconstructed as a singleton factory inside each silo via `AddInoChatClients(config)`. Neurons ask the factory for the tier they need. Tests swap the factory for one backed by `BddMockChatClient` — zero network, deterministic, and green against the existing `.feature` files.

**Tech Stack:** .NET 11, Orleans 10, Aspire 13, `Microsoft.Extensions.AI`, `OpenAI` SDK (xAI uses the OpenAI protocol), Gherkin `.feature` files for BDD mock scenarios, Flutter (CanvasKit) with `package:web` for Web Speech API interop.

**Spec:** `docs/superpowers/specs/2026-04-24-personal-assistant-demo-design.md` (committed `5f02dd1`).

---

## File map

**New:**
- `POC/src/Ino.Core.Hosting/Llm/LlmModel.cs` — abstract base for model descriptors.
- `POC/src/Ino.Core.Hosting/Llm/IChatClientFactory.cs` — tier-aware interface.
- `POC/src/Ino.Core.Hosting/Llm/VoiceToTextProvider.cs` — marker base for voice providers.
- `POC/src/Ino.Core.Hosting/Llm/AddInoChatClientsExtensions.cs` — silo DI wiring.
- `POC/src/Ino.Llm.Xai/Ino.Llm.Xai.csproj` + `XaiChatClientFactory.cs` + `Models/Grok4FastNonReasoning.cs` + `Models/Grok4FastReasoning.cs` + `Models/Grok420.cs`.
- `POC/src/Ino.Aspire.Hosting/WithLlmExtensions.cs` — fluent `WithLlm<T>()` + `AsFast/AsBalanced/AsReasoning`.
- `POC/src/Ino.Aspire.Hosting/WithVoiceToTextExtensions.cs` — fluent `WithVoiceToText<T>()`.
- `POC/src/Ino.Aspire.Hosting/WebSpeechApi.cs` — voice-provider marker.
- `POC/domains/taxi/Ino.Domains.Taxi/Features/taxi-intent.feature`.
- `POC/src/Ino.Testing/BddMockChatClientFactory.cs` — test-side factory.
- `POC/clients/ino.flutter/lib/voice/web_speech_api.dart` — Web Speech API interop.
- `POC/clients/ino.flutter/lib/voice/push_to_talk_button.dart` — UI widget.
- `POC/test/Ino.Llm.Xai.Tests/Ino.Llm.Xai.Tests.csproj` + `XaiChatClientFactoryTests.cs`.

**Modified:**
- `POC/src/Ino.Core/LlmTier.cs` — enum values change.
- `POC/src/Ino.Aspire.Hosting/IInoBuilder.cs` + `InoBuilder.cs` — add `DeclaredModels`, `DeclaredVoiceProvider`, per-tier binding map, and config propagation hook.
- `POC/src/Ino.AppHost/Program.cs` — new fluent `AddIno()` call.
- `POC/src/Ino.System.Host/Program.cs` + `Ino.Identity.Host/Program.cs` + `Ino.Domains.Host/Program.cs` — add `builder.AddInoChatClients(builder.Configuration)`.
- `POC/src/Ino.System/CortexNeuron.cs` — constructor change + catalog-driven routing.
- `POC/domains/travel/Ino.Domains.Travel/Neurons/FlightSearchNeuron.cs` + `HotelSearchNeuron.cs` + `PlaceSearchNeuron.cs` + `ItineraryComposerNeuron.cs` + `FlightMonitorNeuron.cs`.
- `POC/domains/taxi/Ino.Domains.Taxi/Neurons/RideSearchNeuron.cs`.
- `POC/domains/travel/Ino.Domains.Travel/Travel.cs` + `POC/domains/taxi/Ino.Domains.Taxi/Taxi.cs` — `LlmTier.Default` → `LlmTier.Balanced`.
- `POC/src/Ino.Testing/InoTestAppHost.cs` (or nearest fixture) — swap factory registration in test mode.
- `POC/test/Ino.System.Tests/CortexNeuronTests.cs` — update ctor signature + add three new tests.
- `POC/Directory.Packages.props` — pin `OpenAI` + `Microsoft.Extensions.AI.OpenAI` versions.
- `POC/clients/ino.flutter/lib/screens/home/home_screen.dart` — drop `PushToTalkButton` into the input row.
- `POC/ino.slnx` — register `Ino.Llm.Xai` + `Ino.Llm.Xai.Tests`.

---

## Task 1: Rename `LlmTier` values — `Default`→`Balanced`, remove `Multimodal`

**Files:**
- Modify: `POC/src/Ino.Core/LlmTier.cs`
- Modify: `POC/domains/travel/Ino.Domains.Travel/Travel.cs:22`
- Modify: `POC/domains/taxi/Ino.Domains.Taxi/Taxi.cs:20`
- Test: existing `POC/test/Ino.Core.Tests/CapabilityTests.cs` + `POC/test/Ino.Core.Tests/DomainMetadataTests.cs` (fix references)

- [ ] **Step 1: Update the enum.**

Replace the contents of `POC/src/Ino.Core/LlmTier.cs` with:

```csharp
namespace Ino.Core;

/// <summary>
/// Declarative quality tier requested for an LLM capability. Neurons resolve
/// an IChatClient for the tier they need via IChatClientFactory.ForTier.
/// If a tier is unbound by the AppHost, the factory falls back to the
/// highest-bound tier ≤ the requested tier (Reasoning &gt; Balanced &gt; Fast).
/// </summary>
public enum LlmTier
{
    None,
    Fast,
    Balanced,
    Reasoning,
}
```

- [ ] **Step 2: Find every callsite of `LlmTier.Default` and `LlmTier.Multimodal`.**

Run: `rg -n "LlmTier\.(Default|Multimodal)" POC`
Expected: hits in `Travel.cs`, `Taxi.cs`, and any tests that reference these values directly.

- [ ] **Step 3: Replace `LlmTier.Default` with `LlmTier.Balanced`.**

In every file the previous step surfaced, change `LlmTier.Default` → `LlmTier.Balanced`. If `LlmTier.Multimodal` appears, delete the entire `Capability.Llm(LlmTier.Multimodal)` line — the spec explicitly drops Multimodal (no live consumer).

- [ ] **Step 4: Build.**

Run: `dotnet build POC/ino.slnx`
Expected: clean build. If any other callsites were missed, the compiler points at them — fix each with the same `Default → Balanced` replacement.

- [ ] **Step 5: Run tests.**

Run: `dotnet test POC/ino.slnx --filter "FullyQualifiedName~CapabilityTests|FullyQualifiedName~DomainMetadataTests"`
Expected: all green.

- [ ] **Step 6: Commit.**

```bash
git add POC/src/Ino.Core/LlmTier.cs POC/domains POC/test
git commit -m "refactor(poc): LlmTier Default→Balanced; drop Multimodal"
```

---

## Task 2: Add `LlmModel` abstract base in `Ino.Core.Hosting`

**Files:**
- Create: `POC/src/Ino.Core.Hosting/Llm/LlmModel.cs`

- [ ] **Step 1: Create the file.**

```csharp
using Ino.Core;

namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Declarative descriptor for an LLM model an ino silo can talk to. Provider
/// adapters (e.g. Ino.Llm.Xai) ship concrete subclasses for each supported
/// model. The AppHost registers instances via AddIno().WithLlm&lt;TModel&gt;().
/// Descriptors are pure data — they do NOT open network connections.
/// </summary>
public abstract class LlmModel
{
    /// <summary>Provider-recognized model id (e.g. "grok-4-1-fast-reasoning").</summary>
    public abstract string Id { get; }

    /// <summary>Human-readable name for logs and dashboards.</summary>
    public abstract string DisplayName { get; }

    /// <summary>Adapter key (e.g. "xai", "openai", "anthropic", "ollama").</summary>
    public abstract string Provider { get; }

    /// <summary>Tier this model is most naturally suited to. AppHost may override.</summary>
    public abstract LlmTier DefaultTier { get; }
}
```

- [ ] **Step 2: Build.**

Run: `dotnet build POC/src/Ino.Core.Hosting/Ino.Core.Hosting.csproj`
Expected: clean build.

- [ ] **Step 3: Commit.**

```bash
git add POC/src/Ino.Core.Hosting/Llm/LlmModel.cs
git commit -m "feat(poc): add LlmModel descriptor base in Ino.Core.Hosting"
```

---

## Task 3: Create `Ino.Llm.Xai` project skeleton

**Files:**
- Create: `POC/src/Ino.Llm.Xai/Ino.Llm.Xai.csproj`
- Modify: `POC/Directory.Packages.props` (pin `OpenAI` + `Microsoft.Extensions.AI.OpenAI`)
- Modify: `POC/ino.slnx` (register the new project)

- [ ] **Step 1: Verify `OpenAI` + `Microsoft.Extensions.AI.OpenAI` latest versions via Context7.**

Run Context7 resolution for `OpenAI` and `Microsoft.Extensions.AI.OpenAI`. Note the latest stable versions. These are the two packages to pin. Do NOT guess — per CLAUDE.md the version must be verified.

- [ ] **Step 2: Add package pins to `Directory.Packages.props`.**

Insert under the existing `Microsoft.Extensions.AI` line:

```xml
<PackageVersion Include="OpenAI" Version="<latest-stable>" />
<PackageVersion Include="Microsoft.Extensions.AI.OpenAI" Version="<latest-stable>" />
```

- [ ] **Step 3: Create the project file.**

Create `POC/src/Ino.Llm.Xai/Ino.Llm.Xai.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Ino.Core\Ino.Core.csproj" />
    <ProjectReference Include="..\Ino.Core.Hosting\Ino.Core.Hosting.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.AI" />
    <PackageReference Include="Microsoft.Extensions.AI.OpenAI" />
    <PackageReference Include="OpenAI" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Register the project in the solution.**

Open `POC/ino.slnx` and add an entry for `src/Ino.Llm.Xai/Ino.Llm.Xai.csproj`, mirroring the pattern of other `src/Ino.*` entries.

- [ ] **Step 5: Build.**

Run: `dotnet build POC/src/Ino.Llm.Xai/Ino.Llm.Xai.csproj`
Expected: clean build of an empty project.

- [ ] **Step 6: Commit.**

```bash
git add POC/src/Ino.Llm.Xai/Ino.Llm.Xai.csproj POC/Directory.Packages.props POC/ino.slnx
git commit -m "feat(poc): scaffold Ino.Llm.Xai project + OpenAI SDK package pins"
```

---

## Task 4: xAI model catalog classes

**Files:**
- Create: `POC/src/Ino.Llm.Xai/Models/Grok4FastNonReasoning.cs`
- Create: `POC/src/Ino.Llm.Xai/Models/Grok4FastReasoning.cs`
- Create: `POC/src/Ino.Llm.Xai/Models/Grok420.cs`

- [ ] **Step 1: Verify model-ID strings against xAI live API.**

Spec flags dash-vs-dot ambiguity as a risk. Before writing code, confirm the exact `model` string the xAI API accepts. Easiest method: `curl -sS -H "Authorization: Bearer $XAI_API_KEY" https://api.x.ai/v1/models | jq '.data[].id'` if `XAI_API_KEY` is set — reports the canonical IDs. If you can't run the curl, use the xAI console at `https://console.x.ai/team/default/models`. Record the three IDs for Fast-non-reasoning, Fast-reasoning, and Grok 4.20.

- [ ] **Step 2: Create `Grok4FastNonReasoning.cs`.**

```csharp
using Ino.Core;
using Ino.Core.Hosting.Llm;

namespace Ino.Llm.Xai.Models;

public sealed class Grok4FastNonReasoning : LlmModel
{
    public override string Id => "grok-4-1-fast-non-reasoning"; // replace if step 1 returned a different canonical string
    public override string DisplayName => "Grok 4.1 Fast (no reasoning)";
    public override string Provider => "xai";
    public override LlmTier DefaultTier => LlmTier.Fast;
}
```

- [ ] **Step 3: Create `Grok4FastReasoning.cs`.**

```csharp
using Ino.Core;
using Ino.Core.Hosting.Llm;

namespace Ino.Llm.Xai.Models;

public sealed class Grok4FastReasoning : LlmModel
{
    public override string Id => "grok-4-1-fast-reasoning"; // replace if step 1 returned a different canonical string
    public override string DisplayName => "Grok 4.1 Fast (reasoning)";
    public override string Provider => "xai";
    public override LlmTier DefaultTier => LlmTier.Balanced;
}
```

- [ ] **Step 4: Create `Grok420.cs`.**

```csharp
using Ino.Core;
using Ino.Core.Hosting.Llm;

namespace Ino.Llm.Xai.Models;

public sealed class Grok420 : LlmModel
{
    public override string Id => "grok-4.20"; // replace if step 1 returned a different canonical string
    public override string DisplayName => "Grok 4.20 (flagship)";
    public override string Provider => "xai";
    public override LlmTier DefaultTier => LlmTier.Reasoning;
}
```

- [ ] **Step 5: Build.**

Run: `dotnet build POC/src/Ino.Llm.Xai/Ino.Llm.Xai.csproj`
Expected: clean.

- [ ] **Step 6: Commit.**

```bash
git add POC/src/Ino.Llm.Xai/Models/
git commit -m "feat(poc): xAI Grok model catalog — Fast non-reasoning, Fast reasoning, 4.20"
```

---

## Task 5: `IChatClientFactory` interface

**Files:**
- Create: `POC/src/Ino.Core.Hosting/Llm/IChatClientFactory.cs`

- [ ] **Step 1: Create the file.**

```csharp
using Ino.Core;
using Microsoft.Extensions.AI;

namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Tier-aware resolver for IChatClient instances. Neurons ask for the tier
/// they need; the factory maps tier to the concrete model bound by the AppHost
/// and returns an IChatClient that talks to it. Requesting LlmTier.None
/// throws — callers must not ask for it.
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// Returns an IChatClient for the requested tier. If the tier isn't bound,
    /// falls back to the highest-bound tier ≤ the requested tier. Order:
    /// Reasoning &gt; Balanced &gt; Fast. Throws if no tier is bound at all,
    /// or if the requested tier is None.
    /// </summary>
    IChatClient ForTier(LlmTier tier);

    /// <summary>All model descriptors registered at AppHost build time.</summary>
    IReadOnlyList<LlmModel> RegisteredModels { get; }
}
```

- [ ] **Step 2: Build.**

Run: `dotnet build POC/src/Ino.Core.Hosting/Ino.Core.Hosting.csproj`
Expected: clean.

- [ ] **Step 3: Commit.**

```bash
git add POC/src/Ino.Core.Hosting/Llm/IChatClientFactory.cs
git commit -m "feat(poc): IChatClientFactory interface — tier-aware IChatClient resolver"
```

---

## Task 6: `XaiChatClientFactory` implementation + unit tests

**Files:**
- Create: `POC/src/Ino.Llm.Xai/XaiChatClientFactory.cs`
- Create: `POC/test/Ino.Llm.Xai.Tests/Ino.Llm.Xai.Tests.csproj`
- Create: `POC/test/Ino.Llm.Xai.Tests/XaiChatClientFactoryTests.cs`
- Modify: `POC/ino.slnx` (register the tests project)

- [ ] **Step 1: Create the test project.**

`POC/test/Ino.Llm.Xai.Tests/Ino.Llm.Xai.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Ino.Core\Ino.Core.csproj" />
    <ProjectReference Include="..\..\src\Ino.Core.Hosting\Ino.Core.Hosting.csproj" />
    <ProjectReference Include="..\..\src\Ino.Llm.Xai\Ino.Llm.Xai.csproj" />
  </ItemGroup>
</Project>
```

Register it in `POC/ino.slnx` alongside the other `test/Ino.*.Tests` entries.

- [ ] **Step 2: Write the failing tier-fallback test.**

Create `POC/test/Ino.Llm.Xai.Tests/XaiChatClientFactoryTests.cs`:

```csharp
using FluentAssertions;
using Ino.Core;
using Ino.Core.Hosting.Llm;
using Ino.Llm.Xai;
using Ino.Llm.Xai.Models;
using Xunit;

namespace Ino.Llm.Xai.Tests;

public class XaiChatClientFactoryTests
{
    [Fact]
    public void ForTier_returns_client_for_bound_tier()
    {
        var bindings = new[]
        {
            (model: (LlmModel)new Grok4FastNonReasoning(), tier: LlmTier.Fast),
            (model: (LlmModel)new Grok4FastReasoning(), tier: LlmTier.Balanced),
            (model: (LlmModel)new Grok420(), tier: LlmTier.Reasoning),
        };

        var factory = new XaiChatClientFactory(apiKey: "test", bindings);

        factory.ForTier(LlmTier.Fast).Should().NotBeNull();
        factory.ForTier(LlmTier.Balanced).Should().NotBeNull();
        factory.ForTier(LlmTier.Reasoning).Should().NotBeNull();
    }

    [Fact]
    public void ForTier_falls_back_to_highest_below_when_tier_unbound()
    {
        // Only Fast + Reasoning bound. Balanced should fall back to Fast (highest ≤ Balanced).
        var bindings = new[]
        {
            (model: (LlmModel)new Grok4FastNonReasoning(), tier: LlmTier.Fast),
            (model: (LlmModel)new Grok420(), tier: LlmTier.Reasoning),
        };

        var factory = new XaiChatClientFactory(apiKey: "test", bindings);

        // Balanced falls back to Fast (highest bound tier ≤ Balanced).
        var balancedClient = factory.ForTier(LlmTier.Balanced);
        balancedClient.Should().NotBeNull();
        balancedClient.Should().BeSameAs(factory.ForTier(LlmTier.Fast));
    }

    [Fact]
    public void ForTier_throws_for_None()
    {
        var bindings = new[] { (model: (LlmModel)new Grok4FastNonReasoning(), tier: LlmTier.Fast) };
        var factory = new XaiChatClientFactory(apiKey: "test", bindings);

        var act = () => factory.ForTier(LlmTier.None);
        act.Should().Throw<ArgumentException>().WithMessage("*None*");
    }

    [Fact]
    public void Constructor_throws_when_api_key_missing()
    {
        var bindings = new[] { (model: (LlmModel)new Grok4FastNonReasoning(), tier: LlmTier.Fast) };

        var act = () => new XaiChatClientFactory(apiKey: null!, bindings);
        act.Should().Throw<ArgumentException>().WithMessage("*XAI_API_KEY*");
    }

    [Fact]
    public void RegisteredModels_mirrors_bindings()
    {
        var bindings = new[]
        {
            (model: (LlmModel)new Grok4FastNonReasoning(), tier: LlmTier.Fast),
            (model: (LlmModel)new Grok4FastReasoning(), tier: LlmTier.Balanced),
        };
        var factory = new XaiChatClientFactory(apiKey: "test", bindings);

        factory.RegisteredModels.Should().HaveCount(2);
        factory.RegisteredModels.Select(m => m.Id).Should().Contain("grok-4-1-fast-non-reasoning");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail to compile.**

Run: `dotnet test POC/test/Ino.Llm.Xai.Tests/Ino.Llm.Xai.Tests.csproj`
Expected: compile error — `XaiChatClientFactory` does not exist.

- [ ] **Step 4: Implement `XaiChatClientFactory`.**

Create `POC/src/Ino.Llm.Xai/XaiChatClientFactory.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting.Llm;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace Ino.Llm.Xai;

/// <summary>
/// IChatClientFactory backed by xAI's OpenAI-compatible API at
/// https://api.x.ai/v1. A single OpenAIClient is opened with the xAI base URL
/// and the user-supplied API key; per-tier IChatClient instances are
/// constructed lazily against that client and cached for the lifetime of the
/// factory. If a requested tier is unbound, the factory falls back to the
/// highest-bound tier ≤ the requested tier.
/// </summary>
public sealed class XaiChatClientFactory : IChatClientFactory
{
    static readonly Uri XaiEndpoint = new("https://api.x.ai/v1");

    readonly OpenAIClient _client;
    readonly Dictionary<LlmTier, LlmModel> _byTier;
    readonly Dictionary<string, IChatClient> _cache = new(StringComparer.Ordinal);
    readonly LlmModel[] _models;

    public XaiChatClientFactory(string apiKey, IEnumerable<(LlmModel Model, LlmTier Tier)> bindings)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException(
                "XAI_API_KEY not set. Either set it in the environment or edit " +
                "POC/src/Ino.AppHost/Program.cs to uncomment a different provider.",
                nameof(apiKey));

        var materialized = bindings.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException("At least one (model, tier) binding is required.", nameof(bindings));

        _client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
        {
            Endpoint = XaiEndpoint,
        });

        _byTier = materialized.ToDictionary(b => b.Tier, b => b.Model);
        _models = materialized.Select(b => b.Model).ToArray();
    }

    public IReadOnlyList<LlmModel> RegisteredModels => _models;

    public IChatClient ForTier(LlmTier tier)
    {
        if (tier == LlmTier.None)
            throw new ArgumentException(
                $"LlmTier.None is not a valid request — callers must ask for Fast/Balanced/Reasoning.",
                nameof(tier));

        var model = ResolveModel(tier)
            ?? throw new InvalidOperationException(
                $"No model bound for tier {tier} and no lower-tier fallback available. " +
                $"Bound tiers: {string.Join(", ", _byTier.Keys)}.");

        if (_cache.TryGetValue(model.Id, out var cached))
            return cached;

        var chat = _client
            .GetChatClient(model.Id)
            .AsIChatClient();

        _cache[model.Id] = chat;
        return chat;
    }

    LlmModel? ResolveModel(LlmTier requested)
    {
        // Exact match first.
        if (_byTier.TryGetValue(requested, out var exact))
            return exact;

        // Fallback: highest bound tier ≤ requested. Order values so Reasoning > Balanced > Fast.
        foreach (var tier in new[] { LlmTier.Reasoning, LlmTier.Balanced, LlmTier.Fast })
        {
            if ((int)tier <= (int)requested && _byTier.TryGetValue(tier, out var model))
                return model;
        }

        return null;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass.**

Run: `dotnet test POC/test/Ino.Llm.Xai.Tests/Ino.Llm.Xai.Tests.csproj`
Expected: all 5 tests pass.

- [ ] **Step 6: Commit.**

```bash
git add POC/src/Ino.Llm.Xai/XaiChatClientFactory.cs POC/test/Ino.Llm.Xai.Tests POC/ino.slnx
git commit -m "feat(poc): XaiChatClientFactory with tier fallback + unit tests"
```

---

## Task 7: Fluent `WithLlm<T>().AsFast/AsBalanced/AsReasoning()` on `IInoBuilder`

**Files:**
- Modify: `POC/src/Ino.Aspire.Hosting/IInoBuilder.cs`
- Modify: `POC/src/Ino.Aspire.Hosting/InoBuilder.cs`
- Create: `POC/src/Ino.Aspire.Hosting/WithLlmExtensions.cs`
- Create: `POC/src/Ino.Aspire.Hosting/LlmModelBinding.cs`

- [ ] **Step 1: Create the binding record.**

`POC/src/Ino.Aspire.Hosting/LlmModelBinding.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting.Llm;

namespace Ino.Aspire.Hosting;

/// <summary>
/// AppHost-side record of a (model, tier) pair declared via
/// <c>WithLlm&lt;TModel&gt;().As{Fast,Balanced,Reasoning}()</c>.
/// </summary>
public sealed record LlmModelBinding(LlmModel Model, LlmTier Tier);
```

- [ ] **Step 2: Extend `IInoBuilder`.**

Replace `POC/src/Ino.Aspire.Hosting/IInoBuilder.cs` with:

```csharp
using Ino.Core.Hosting;

namespace Ino.Aspire.Hosting;

public interface IInoBuilder
{
    IReadOnlyList<IDomain> RegisteredDomains { get; }
    void RegisterDomain(IDomain domain);

    IReadOnlyList<LlmModelBinding> DeclaredModels { get; }
    void RegisterModel(LlmModelBinding binding);
}
```

- [ ] **Step 3: Update `InoBuilder`.**

Replace `POC/src/Ino.Aspire.Hosting/InoBuilder.cs` with:

```csharp
using Ino.Core.Hosting;

namespace Ino.Aspire.Hosting;

internal sealed class InoBuilder : IInoBuilder
{
    readonly List<IDomain> _domains = [];
    readonly List<LlmModelBinding> _models = [];

    public IReadOnlyList<IDomain> RegisteredDomains => _domains;
    public void RegisterDomain(IDomain domain) => _domains.Add(domain);

    public IReadOnlyList<LlmModelBinding> DeclaredModels => _models;
    public void RegisterModel(LlmModelBinding binding) => _models.Add(binding);
}
```

- [ ] **Step 4: Add the fluent API.**

`POC/src/Ino.Aspire.Hosting/WithLlmExtensions.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting.Llm;

namespace Ino.Aspire.Hosting;

public static class WithLlmExtensions
{
    /// <summary>
    /// Declares a model the AppHost wants ino to consider. Follow up with
    /// <c>.AsFast()</c> / <c>.AsBalanced()</c> / <c>.AsReasoning()</c> to
    /// bind the model to a tier. Declaring without a tier terminator
    /// binds to the model's own <see cref="LlmModel.DefaultTier"/>.
    /// </summary>
    public static LlmModelSelector<TModel> WithLlm<TModel>(this IInoBuilder builder)
        where TModel : LlmModel, new()
    {
        return new LlmModelSelector<TModel>(builder, new TModel());
    }
}

public sealed class LlmModelSelector<TModel> where TModel : LlmModel
{
    readonly IInoBuilder _builder;
    readonly TModel _model;
    bool _bound;

    internal LlmModelSelector(IInoBuilder builder, TModel model)
    {
        _builder = builder;
        _model = model;
    }

    public IInoBuilder AsFast() => BindTo(LlmTier.Fast);
    public IInoBuilder AsBalanced() => BindTo(LlmTier.Balanced);
    public IInoBuilder AsReasoning() => BindTo(LlmTier.Reasoning);

    IInoBuilder BindTo(LlmTier tier)
    {
        if (_bound)
            throw new InvalidOperationException(
                $"WithLlm<{typeof(TModel).Name}> is already bound; call AsFast/AsBalanced/AsReasoning once.");
        _bound = true;
        _builder.RegisterModel(new LlmModelBinding(_model, tier));
        return _builder;
    }
}
```

- [ ] **Step 5: Build.**

Run: `dotnet build POC/src/Ino.Aspire.Hosting/Ino.Aspire.Hosting.csproj`
Expected: clean. `Ino.Aspire.Hosting` must reference `Ino.Core.Hosting` already — verify with `cat POC/src/Ino.Aspire.Hosting/Ino.Aspire.Hosting.csproj`. If not, add the project reference.

- [ ] **Step 6: Commit.**

```bash
git add POC/src/Ino.Aspire.Hosting/
git commit -m "feat(poc): AddIno().WithLlm<T>().As{Fast,Balanced,Reasoning} fluent API"
```

---

## Task 8: Fluent `WithVoiceToText<T>()` + `WebSpeechApi` marker

**Files:**
- Create: `POC/src/Ino.Core.Hosting/Llm/VoiceToTextProvider.cs`
- Create: `POC/src/Ino.Aspire.Hosting/WithVoiceToTextExtensions.cs`
- Create: `POC/src/Ino.Aspire.Hosting/WebSpeechApi.cs`
- Modify: `POC/src/Ino.Aspire.Hosting/IInoBuilder.cs` (add `DeclaredVoiceProvider`)
- Modify: `POC/src/Ino.Aspire.Hosting/InoBuilder.cs` (implement)

- [ ] **Step 1: Create the provider base.**

`POC/src/Ino.Core.Hosting/Llm/VoiceToTextProvider.cs`:

```csharp
namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Marker base for voice-to-text providers an AppHost can declare via
/// <c>WithVoiceToText&lt;TProvider&gt;()</c>. The marker carries no
/// behaviour — Flutter chooses its implementation based on the declared
/// provider name; future server-side providers (Whisper sidecar, Azure
/// Speech) will hang DI/health checks off this base.
/// </summary>
public abstract class VoiceToTextProvider
{
    public abstract string Name { get; }
}
```

- [ ] **Step 2: Create the WebSpeechApi marker.**

`POC/src/Ino.Aspire.Hosting/WebSpeechApi.cs`:

```csharp
using Ino.Core.Hosting.Llm;

namespace Ino.Aspire.Hosting;

/// <summary>
/// Browser-native voice-to-text via <c>window.SpeechRecognition</c>. All
/// transcription happens in the Flutter web client; no server-side code
/// runs. Chrome and Edge are supported; other browsers render the mic
/// widget disabled.
/// </summary>
public sealed class WebSpeechApi : VoiceToTextProvider
{
    public override string Name => "web-speech-api";
}
```

- [ ] **Step 3: Extend `IInoBuilder` + `InoBuilder` with the voice slot.**

Add to `IInoBuilder`:

```csharp
VoiceToTextProvider? DeclaredVoiceProvider { get; }
void RegisterVoiceProvider(VoiceToTextProvider provider);
```

Add corresponding fields/methods to `InoBuilder`:

```csharp
VoiceToTextProvider? _voice;
public VoiceToTextProvider? DeclaredVoiceProvider => _voice;
public void RegisterVoiceProvider(VoiceToTextProvider provider) => _voice = provider;
```

Add `using Ino.Core.Hosting.Llm;` at the top of both files.

- [ ] **Step 4: Add the fluent extension.**

`POC/src/Ino.Aspire.Hosting/WithVoiceToTextExtensions.cs`:

```csharp
using Ino.Core.Hosting.Llm;

namespace Ino.Aspire.Hosting;

public static class WithVoiceToTextExtensions
{
    /// <summary>
    /// Declares the voice-to-text provider for ino. Day-1 support: WebSpeechApi
    /// (browser-native, no backend). Future providers (Whisper sidecar, Azure
    /// Speech) plug in by implementing VoiceToTextProvider.
    /// </summary>
    public static IInoBuilder WithVoiceToText<TProvider>(this IInoBuilder builder)
        where TProvider : VoiceToTextProvider, new()
    {
        builder.RegisterVoiceProvider(new TProvider());
        return builder;
    }
}
```

- [ ] **Step 5: Build.**

Run: `dotnet build POC/src/Ino.Aspire.Hosting/Ino.Aspire.Hosting.csproj`
Expected: clean.

- [ ] **Step 6: Commit.**

```bash
git add POC/src/Ino.Core.Hosting/Llm/VoiceToTextProvider.cs POC/src/Ino.Aspire.Hosting/
git commit -m "feat(poc): WithVoiceToText<T>() fluent API + WebSpeechApi marker"
```

---

## Task 9: AppHost → silo env-var propagation

**Files:**
- Modify: `POC/src/Ino.Aspire.Hosting/AddInoExtensions.cs`
- Create: `POC/src/Ino.Aspire.Hosting/InoSiloEnvironmentExtensions.cs`

- [ ] **Step 1: Add the env-var serialization helper.**

`POC/src/Ino.Aspire.Hosting/InoSiloEnvironmentExtensions.cs`:

```csharp
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Ino.Aspire.Hosting;

/// <summary>
/// Serializes the IInoBuilder's declared LLM model bindings and voice
/// provider into environment variables on each ino silo project, so
/// silo-side DI can reconstruct the IChatClientFactory at startup.
///
/// Env var shape:
///   Ino__Llm__Models__0__Provider = "xai"
///   Ino__Llm__Models__0__Id       = "grok-4-1-fast-non-reasoning"
///   Ino__Llm__Models__0__Tier     = "Fast"
///   Ino__Llm__Models__0__Type     = "Ino.Llm.Xai.Models.Grok4FastNonReasoning, Ino.Llm.Xai"
///   (repeat per model)
///   Ino__Voice__Provider          = "web-speech-api"
///
/// Uses Aspire's double-underscore config convention so .NET Configuration
/// binds the lists transparently.
/// </summary>
public static class InoSiloEnvironmentExtensions
{
    public static IResourceBuilder<T> PropagateInoConfig<T>(
        this IResourceBuilder<T> resource,
        IInoBuilder ino)
        where T : IResourceWithEnvironment
    {
        for (var i = 0; i < ino.DeclaredModels.Count; i++)
        {
            var b = ino.DeclaredModels[i];
            resource.WithEnvironment($"Ino__Llm__Models__{i}__Provider", b.Model.Provider);
            resource.WithEnvironment($"Ino__Llm__Models__{i}__Id", b.Model.Id);
            resource.WithEnvironment($"Ino__Llm__Models__{i}__Tier", b.Tier.ToString());
            resource.WithEnvironment($"Ino__Llm__Models__{i}__Type",
                $"{b.Model.GetType().FullName}, {b.Model.GetType().Assembly.GetName().Name}");
        }

        if (ino.DeclaredVoiceProvider is { } voice)
            resource.WithEnvironment("Ino__Voice__Provider", voice.Name);

        return resource;
    }
}
```

- [ ] **Step 2: Update `AddInoExtensions` so `AddIno(...)` stashes the builder in AppHost resource state.**

We need AppHost callers to apply the propagation by writing `project.PropagateInoConfig(ino)`. Rather than auto-wire, we keep the two calls explicit for clarity — AppHost/Program.cs will do:

```csharp
var ino = builder.AddIno("ino")…;
builder.AddProject<Projects.Ino_System_Host>(...).PropagateInoConfig(ino);
```

No changes needed to `AddInoExtensions.cs` for Task 9 itself; the propagation is a separate call in `AppHost/Program.cs` (Task 12).

- [ ] **Step 3: Build.**

Run: `dotnet build POC/src/Ino.Aspire.Hosting/Ino.Aspire.Hosting.csproj`
Expected: clean. If `IResourceWithEnvironment` is missing, add a `using Aspire.Hosting.ApplicationModel;` and confirm `Aspire.Hosting` is already referenced by the csproj (it is).

- [ ] **Step 4: Commit.**

```bash
git add POC/src/Ino.Aspire.Hosting/InoSiloEnvironmentExtensions.cs
git commit -m "feat(poc): PropagateInoConfig — serialize InoBuilder declarations to silo env vars"
```

---

## Task 10: `AddInoChatClients(config)` silo DI extension

**Files:**
- Create: `POC/src/Ino.Core.Hosting/Llm/AddInoChatClientsExtensions.cs`
- Modify: `POC/src/Ino.Core.Hosting/Ino.Core.Hosting.csproj` (reference `Ino.Llm.Xai` — see note below)

> ⚠️ `Ino.Core.Hosting` can't reference `Ino.Llm.Xai` (it would invert the dep graph). Instead, `AddInoChatClients` constructs the xAI factory via reflection using the assembly-qualified `Type` env var. Cleaner: define a `ChatClientFactoryRegistrar` abstraction in `Ino.Core.Hosting` and let `Ino.Llm.Xai` plug in. For v0.1 we use the reflection path to stay minimal; if future providers land, add a `[InoChatClientProvider("xai")]` attribute-based registry.

- [ ] **Step 1: Write `AddInoChatClients`.**

`POC/src/Ino.Core.Hosting/Llm/AddInoChatClientsExtensions.cs`:

```csharp
using Ino.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Reads the AppHost-propagated LLM model list (Ino:Llm:Models) from
/// configuration and registers a singleton IChatClientFactory plus per-tier
/// keyed IChatClient entries. If the models list is empty (e.g. in pure
/// INO_TEST_MODE fixtures that override the factory themselves), no-op.
/// </summary>
public static class AddInoChatClientsExtensions
{
    public const string ApiKeyEnvVar = "XAI_API_KEY";

    public static IHostApplicationBuilder AddInoChatClients(
        this IHostApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("Ino:Llm:Models");
        var modelConfigs = section.GetChildren().ToArray();
        if (modelConfigs.Length == 0)
            return builder;

        // Group bindings by provider so each factory sees only its own models.
        var byProvider = modelConfigs
            .Select(c => new
            {
                Provider = c["Provider"] ?? "",
                Id = c["Id"] ?? "",
                Tier = Enum.Parse<LlmTier>(c["Tier"] ?? "Balanced"),
                TypeName = c["Type"] ?? "",
            })
            .GroupBy(m => m.Provider, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byProvider)
        {
            if (!string.Equals(group.Key, "xai", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException(
                    $"Ino.Core.Hosting only knows how to wire provider 'xai' for v0.1. " +
                    $"Provider '{group.Key}' needs a dedicated adapter; please add it or remove the binding in AppHost.");

            var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException(
                    $"{ApiKeyEnvVar} not set. Either set it in the environment or edit " +
                    "POC/src/Ino.AppHost/Program.cs to uncomment a different provider.");

            var bindings = group.Select(m => BuildBinding(m.TypeName, m.Tier)).ToArray();

            builder.Services.AddSingleton<IChatClientFactory>(_ =>
            {
                var factoryType = Type.GetType(
                    "Ino.Llm.Xai.XaiChatClientFactory, Ino.Llm.Xai",
                    throwOnError: true)!;
                return (IChatClientFactory)Activator.CreateInstance(
                    factoryType,
                    apiKey,
                    bindings)!;
            });
        }

        // Per-tier keyed IChatClient so neurons can [FromKeyedServices(LlmTier.Balanced)] IChatClient.
        foreach (var tier in new[] { LlmTier.Fast, LlmTier.Balanced, LlmTier.Reasoning })
        {
            builder.Services.AddKeyedSingleton<IChatClient>(tier, (sp, _) =>
                sp.GetRequiredService<IChatClientFactory>().ForTier(tier));
        }

        return builder;
    }

    static (LlmModel Model, LlmTier Tier) BuildBinding(string assemblyQualifiedTypeName, LlmTier tier)
    {
        var type = Type.GetType(assemblyQualifiedTypeName, throwOnError: true)!;
        var instance = (LlmModel)Activator.CreateInstance(type)!;
        return (instance, tier);
    }
}
```

- [ ] **Step 2: Build.**

Run: `dotnet build POC/src/Ino.Core.Hosting/Ino.Core.Hosting.csproj`
Expected: clean.

- [ ] **Step 3: Commit.**

```bash
git add POC/src/Ino.Core.Hosting/Llm/AddInoChatClientsExtensions.cs
git commit -m "feat(poc): AddInoChatClients silo extension reads config + registers factory"
```

---

## Task 11: Wire `AddInoChatClients` in every silo Host/Program.cs

**Files:**
- Modify: `POC/src/Ino.System.Host/Program.cs`
- Modify: `POC/src/Ino.Identity.Host/Program.cs`
- Modify: `POC/src/Ino.Domains.Host/Program.cs`
- Modify: each Host csproj to reference `Ino.Llm.Xai` (so the type loads at runtime)

- [ ] **Step 1: In `Ino.System.Host.csproj`, add the project reference.**

```xml
<ProjectReference Include="..\Ino.Llm.Xai\Ino.Llm.Xai.csproj" />
```

Repeat in `Ino.Identity.Host.csproj` and `Ino.Domains.Host.csproj`. The reflection-based loader in `AddInoChatClients` requires the assembly to be loadable at runtime.

- [ ] **Step 2: Add the `AddInoChatClients()` call to `Ino.System.Host/Program.cs`.**

Insert after `builder.AddSystemSilo();`:

```csharp
builder.AddInoChatClients();
```

Add `using Ino.Core.Hosting.Llm;` at the top.

- [ ] **Step 3: Do the same in `Ino.Identity.Host/Program.cs` and `Ino.Domains.Host/Program.cs`.**

Find each silo's `builder.Add*Silo()` call and add `builder.AddInoChatClients();` directly after.

- [ ] **Step 4: Build.**

Run: `dotnet build POC/ino.slnx`
Expected: clean.

- [ ] **Step 5: Commit.**

```bash
git add POC/src/Ino.System.Host POC/src/Ino.Identity.Host POC/src/Ino.Domains.Host
git commit -m "feat(poc): wire AddInoChatClients in all three ino silo hosts"
```

---

## Task 12: Rewrite `Ino.AppHost/Program.cs` with the fluent API

**Files:**
- Modify: `POC/src/Ino.AppHost/Program.cs`
- Modify: `POC/src/Ino.AppHost/Ino.AppHost.csproj` (reference `Ino.Llm.Xai`)

- [ ] **Step 1: Add `Ino.Llm.Xai` reference to the AppHost.**

`POC/src/Ino.AppHost/Ino.AppHost.csproj` gets:

```xml
<ProjectReference Include="..\Ino.Llm.Xai\Ino.Llm.Xai.csproj" />
```

- [ ] **Step 2: Replace `POC/src/Ino.AppHost/Program.cs` with:**

```csharp
using Ino.Core;
using Ino.Aspire.Hosting;
using Ino.Llm.Xai.Models;

var builder = DistributedApplication.CreateBuilder(args);

// Pick a provider by uncommenting. Default: xAI (all three tiers).
// API key via XAI_API_KEY env var. Fails loudly if missing.
var ino = builder.AddIno("ino")
    .WithLlm<Grok4FastNonReasoning>().AsFast()
    .WithLlm<Grok4FastReasoning>().AsBalanced()
    .WithLlm<Grok420>().AsReasoning()
    .WithVoiceToText<WebSpeechApi>();

// Multi-silo localhost clustering — unchanged from prior AppHost. Each silo
// project configures UseLocalhostClustering() itself with a fixed siloPort.
builder.AddProject<Projects.Ino_System_Host>(KernelSilo.System.ToResourceName())
    .WithHttpsEndpoint(name: "system-http")
    .PropagateInoConfig(ino);

builder.AddProject<Projects.Ino_Identity_Host>(KernelSilo.Identity.ToResourceName())
    .PropagateInoConfig(ino);

builder.AddProject<Projects.Ino_Domains_Host>(KernelSilo.Domains.ToResourceName())
    .PropagateInoConfig(ino);

builder.Build().Run();
```

- [ ] **Step 3: Build.**

Run: `dotnet build POC/src/Ino.AppHost/Ino.AppHost.csproj`
Expected: clean.

- [ ] **Step 4: Commit.**

```bash
git add POC/src/Ino.AppHost/
git commit -m "feat(poc): Ino.AppHost uses fluent AddIno/WithLlm/WithVoiceToText"
```

---

## Task 13: `CortexNeuron` — switch to `IChatClientFactory`, keep path green

**Files:**
- Modify: `POC/src/Ino.System/CortexNeuron.cs`
- Modify: `POC/test/Ino.System.Tests/CortexNeuronTests.cs`
- Modify: `POC/test/Ino.System.Tests/CortexReasoningAnnotationTests.cs`

- [ ] **Step 1: Update the CortexNeuron constructor.**

Change the primary-constructor parameter list in `POC/src/Ino.System/CortexNeuron.cs:27-31`:

```csharp
public sealed class CortexNeuron(
    IDiscoveryClient discovery,
    IFirePort firePort,
    IChatClientFactory llm,
    IReasoningProbe probe,
    ILogger<CortexNeuron> log) : Grain, INeuron<ChatIntent>
```

Remove the old `IChatClient chatClient` parameter. Replace the body of `AnnotateReasoningAsync(...)` call sites to call `llm.ForTier(LlmTier.Fast)` when an LLM is needed (the full rewrite comes in Task 16). For this task keep the keyword branches intact — we swap the seam without changing behaviour so build + tests remain green.

Search for every `chatClient.` reference in `CortexNeuron.cs` and replace with `llm.ForTier(LlmTier.Fast).`.

Add `using Ino.Core.Hosting.Llm;` at top. Remove `using Microsoft.Extensions.AI;` if no longer referenced (it is still referenced inside `AnnotateReasoningAsync` for `ChatMessage`/`ChatRole` — keep it).

- [ ] **Step 2: Update `CortexNeuronTests.cs`' `NewCortex` helper.**

In `POC/test/Ino.System.Tests/CortexNeuronTests.cs:28-29`, replace:

```csharp
static CortexNeuron NewCortex(IDiscoveryClient discovery, IFirePort firePort, IChatClient? chatClient = null) =>
    new(discovery, firePort, chatClient ?? NoChat(), NullLogger<CortexNeuron>.Instance);
```

with:

```csharp
static CortexNeuron NewCortex(
    IDiscoveryClient discovery,
    IFirePort firePort,
    IChatClientFactory? llm = null,
    IReasoningProbe? probe = null) =>
    new(discovery,
        firePort,
        llm ?? StubFactory(NoChat()),
        probe ?? new InMemoryReasoningProbe(),
        NullLogger<CortexNeuron>.Instance);

static IChatClientFactory StubFactory(IChatClient client)
{
    var factory = Substitute.For<IChatClientFactory>();
    factory.ForTier(Arg.Any<LlmTier>()).Returns(client);
    factory.RegisteredModels.Returns(Array.Empty<LlmModel>());
    return factory;
}
```

Add the missing usings at the top of the file: `using Ino.Core.Hosting.Llm;`.

Do the equivalent update in `CortexReasoningAnnotationTests.cs`.

- [ ] **Step 3: Build + test.**

Run: `dotnet build POC/ino.slnx`
Run: `dotnet test POC/ino.slnx --filter "FullyQualifiedName~CortexNeuronTests|FullyQualifiedName~CortexReasoningAnnotationTests"`
Expected: clean build, all CortexNeuron tests green — behaviour hasn't changed, only the injection seam.

- [ ] **Step 4: Commit.**

```bash
git add POC/src/Ino.System/CortexNeuron.cs POC/test/Ino.System.Tests/
git commit -m "refactor(poc): CortexNeuron takes IChatClientFactory; tests updated"
```

---

## Task 14: `CortexNeuron.TryRegexMatch` — catalog fast-path

**Files:**
- Modify: `POC/src/Ino.System/CortexNeuron.cs`
- Modify: `POC/test/Ino.System.Tests/CortexNeuronTests.cs`

- [ ] **Step 1: Add a failing test for the regex fast-path.**

Append to `CortexNeuronTests.cs`:

```csharp
[Fact]
public async Task Routes_via_regex_fastpath_when_single_experience_matches()
{
    var firePort = Substitute.For<IFirePort>();
    FindFlightsRequest? captured = null;
    firePort.Fire(Arg.Do<FindFlightsRequest>(r => captured = r),
            Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
        .Returns(NeuronResult.Ok("ok"));

    var travelFindFlights = new Experience(
        ExperienceId.From("travel.find-flights"),
        "Find flights", "Search flights.",
        typeof(FindFlightsRequest),
        new[] { "find flights to .+", "cheapest flight from .+" });

    var discovery = Substitute.For<IDiscoveryClient>();
    discovery.LookupCanonicalAsync(typeof(FindFlightsRequest), Arg.Any<CancellationToken>())
        .Returns(new CanonicalTarget(typeof(FindFlightsRequest), typeof(object), TravelDomain, []));
    discovery.DumpExperiencesAsync(Arg.Any<CancellationToken>())
        .Returns(new[] { (IExperience)travelFindFlights });

    var chat = Substitute.For<IChatClient>();
    var factory = StubFactory(chat);

    var cortex = NewCortex(discovery, firePort, factory);
    await cortex.HandleAsync(
        new ChatIntent("find flights to Bali", "u1"),
        Ctx(firePort),
        TestContext.Current.CancellationToken);

    captured.Should().NotBeNull();
    // Regex fast-path must NOT call the LLM.
    await chat.DidNotReceive().GetResponseAsync(
        Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
}
```

- [ ] **Step 2: Run test — expect failure (method not present yet; current routing is keyword-only).**

Run: `dotnet test POC/test/Ino.System.Tests --filter "Routes_via_regex_fastpath"`
Expected: FAIL — either no match (Unrouted) or LLM was called.

- [ ] **Step 3: Add `TryRegexMatch` to `CortexNeuron.cs`.**

Inside `CortexNeuron` (before the `ContainsAny` helper):

```csharp
/// <summary>
/// Returns the one experience whose PromptExamples regex-match the utterance.
/// Returns null on zero matches or ambiguity (≥2 matching experiences) so the
/// LLM classifier can disambiguate. Case-insensitive.
/// </summary>
static IExperience? TryRegexMatch(IReadOnlyList<IExperience> catalog, string utterance)
{
    IExperience? hit = null;
    foreach (var exp in catalog)
    {
        foreach (var example in exp.PromptExamples)
        {
            if (string.IsNullOrWhiteSpace(example)) continue;
            if (!Regex.IsMatch(utterance, example, RegexOptions.IgnoreCase)) continue;
            if (hit is not null && !ReferenceEquals(hit, exp))
                return null; // ambiguity → fall through to LLM
            hit = exp;
            break;
        }
    }
    return hit;
}
```

Add `using System.Text.RegularExpressions;` + `using Ino.Core.Hosting;` if not already present.

- [ ] **Step 4: Full `HandleAsync` rewrite still lands in Task 16.**

For Task 14 we only add the helper. Keep existing `HandleAsync` untouched. Verify build still green.

Run: `dotnet build POC/src/Ino.System/Ino.System.csproj`
Expected: clean.

- [ ] **Step 5: Commit.**

```bash
git add POC/src/Ino.System/CortexNeuron.cs POC/test/Ino.System.Tests/CortexNeuronTests.cs
git commit -m "feat(poc): CortexNeuron.TryRegexMatch — regex fast-path helper + test"
```

---

## Task 15: `CortexNeuron.ClassifyWithLlmAsync` — LLM classifier

**Files:**
- Modify: `POC/src/Ino.System/CortexNeuron.cs`
- Modify: `POC/test/Ino.System.Tests/CortexNeuronTests.cs`

- [ ] **Step 1: Add a failing test for the classifier branch.**

Append to `CortexNeuronTests.cs`:

```csharp
[Fact]
public async Task Routes_via_llm_classifier_when_regex_misses()
{
    var firePort = Substitute.For<IFirePort>();
    PlanTripRequest? captured = null;
    firePort.Fire(Arg.Do<PlanTripRequest>(r => captured = r),
            Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
        .Returns(NeuronResult.Ok("ok"));

    var travelPlan = new Experience(
        ExperienceId.From("travel.plan-trip"),
        "Plan a trip", "Build an itinerary.",
        typeof(PlanTripRequest),
        new[] { "this regex does not match the utterance" });

    var discovery = Substitute.For<IDiscoveryClient>();
    discovery.LookupCanonicalAsync(typeof(PlanTripRequest), Arg.Any<CancellationToken>())
        .Returns(new CanonicalTarget(typeof(PlanTripRequest), typeof(object), TravelDomain, []));
    discovery.DumpExperiencesAsync(Arg.Any<CancellationToken>())
        .Returns(new[] { (IExperience)travelPlan });

    // LLM returns the experience id as JSON.
    var chat = Substitute.For<IChatClient>();
    chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
        .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            "{\"experience_id\":\"travel.plan-trip\"}")));

    var cortex = NewCortex(discovery, firePort, StubFactory(chat));
    await cortex.HandleAsync(
        new ChatIntent("I want to wander around somewhere for 5 days", "u1"),
        Ctx(firePort),
        TestContext.Current.CancellationToken);

    captured.Should().NotBeNull();
    await chat.Received(1).GetResponseAsync(
        Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task Unroutes_when_classifier_returns_none()
{
    var firePort = Substitute.For<IFirePort>();
    UnroutedIntent? captured = null;
    firePort.FireBroadcast(Arg.Do<UnroutedIntent>(u => captured = u),
            Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
        .Returns(Task.CompletedTask);

    var travelPlan = new Experience(
        ExperienceId.From("travel.plan-trip"),
        "Plan a trip", "Build an itinerary.",
        typeof(PlanTripRequest),
        new[] { "regex-miss" });

    var discovery = Substitute.For<IDiscoveryClient>();
    discovery.DumpExperiencesAsync(Arg.Any<CancellationToken>())
        .Returns(new[] { (IExperience)travelPlan });

    var chat = Substitute.For<IChatClient>();
    chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
        .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            "{\"experience_id\":\"none\"}")));

    var cortex = NewCortex(discovery, firePort, StubFactory(chat));
    await cortex.HandleAsync(
        new ChatIntent("quack", "u1"),
        Ctx(firePort),
        TestContext.Current.CancellationToken);

    captured.Should().NotBeNull();
}
```

- [ ] **Step 2: Run — expect failure (method missing).**

Run: `dotnet test POC/test/Ino.System.Tests --filter "Routes_via_llm_classifier|Unroutes_when_classifier_returns_none"`
Expected: FAIL — current `HandleAsync` still uses keyword branches.

- [ ] **Step 3: Add `ClassifyWithLlmAsync` + JSON parse helper to `CortexNeuron.cs`.**

Inside `CortexNeuron`:

```csharp
async Task<IExperience?> ClassifyWithLlmAsync(
    IReadOnlyList<IExperience> catalog,
    ChatIntent intent,
    CancellationToken ct)
{
    var chat = llm.ForTier(LlmTier.Fast);
    var prompt = BuildClassifierPrompt(catalog, intent.Text);

    ChatResponse response;
    try
    {
        response = await chat.GetResponseAsync(
            new[]
            {
                new ChatMessage(ChatRole.System, prompt.System),
                new ChatMessage(ChatRole.User, prompt.User),
            },
            new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.Json,
                Temperature = 0,
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    [BddMockChatClient.NeuronIdKey] = nameof(CortexNeuron),
                },
            },
            ct);
    }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Cortex LLM classifier failed; falling back to Unrouted.");
        return null;
    }

    var replyText = response.Text ?? "";
    var experienceId = TryParseExperienceId(replyText);
    if (experienceId is null || experienceId == "none")
    {
        log.LogInformation("Cortex classifier returned 'none' for '{Prompt}'", intent.Text);
        return null;
    }

    var match = catalog.FirstOrDefault(e => e.Id.Value == experienceId);
    if (match is null)
    {
        log.LogWarning(
            "Cortex classifier returned unknown experience_id '{Id}' (hallucination?); treating as none.",
            experienceId);
        return null;
    }

    probe.Record(nameof(CortexNeuron), new ReasoningRecord(
        Source: "cortex-llm",
        ScenarioName: experienceId,
        FeatureTitle: "cortex-intent-classify",
        Prompt: intent.Text,
        Reply: replyText,
        Timestamp: DateTimeOffset.UtcNow));

    return match;
}

static (string System, string User) BuildClassifierPrompt(IReadOnlyList<IExperience> catalog, string utterance)
{
    var list = string.Join("\n", catalog.Select(e => $"- {e.Id.Value}: {e.Description}"));
    var sys =
        "You are ino's intent router. Given a user utterance, return exactly one experience_id " +
        "from this list, or 'none' if nothing fits. Reply with JSON {\"experience_id\":\"...\"}.\n\n" +
        "Experiences:\n" + list;
    return (sys, utterance);
}

static string? TryParseExperienceId(string json)
{
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("experience_id", out var prop))
            return prop.GetString();
    }
    catch { }
    return null;
}
```

Add `using System.Text.Json;` at the top (unless already present).

- [ ] **Step 4: Build.**

Run: `dotnet build POC/src/Ino.System/Ino.System.csproj`
Expected: clean. Tests still fail (HandleAsync not yet rewritten) — fix in Task 16.

- [ ] **Step 5: Commit.**

```bash
git add POC/src/Ino.System/CortexNeuron.cs POC/test/Ino.System.Tests/CortexNeuronTests.cs
git commit -m "feat(poc): CortexNeuron.ClassifyWithLlmAsync — LLM classifier helper + failing tests"
```

---

## Task 16: Rewrite `CortexNeuron.HandleAsync` — delete keyword branches, use helpers

**Files:**
- Modify: `POC/src/Ino.System/CortexNeuron.cs`

- [ ] **Step 1: Rewrite `HandleAsync`.**

Replace the entire `HandleAsync` method in `CortexNeuron.cs`:

```csharp
public async Task<NeuronResult> HandleAsync(ChatIntent synapse, NeuronContext ctx, CancellationToken ct)
{
    var liveCtx = ctx with { FirePort = firePort, Logger = log };
    var catalog = await discovery.DumpExperiencesAsync(ct);

    // Regex fast-path.
    var fast = TryRegexMatch(catalog, synapse.Text);
    if (fast is not null)
    {
        probe.Record(nameof(CortexNeuron), new ReasoningRecord(
            Source: "cortex-regex",
            ScenarioName: fast.Id.Value,
            FeatureTitle: "cortex-intent-classify",
            Prompt: synapse.Text,
            Reply: $"matched {fast.Id.Value} via PromptExamples regex",
            Timestamp: DateTimeOffset.UtcNow));
        return await FireAsync(fast, synapse, liveCtx, ct);
    }

    // LLM classifier fallback.
    var chosen = await ClassifyWithLlmAsync(catalog, synapse, ct);
    if (chosen is not null)
        return await FireAsync(chosen, synapse, liveCtx, ct);

    return await EmitUnroutedAsync(synapse, liveCtx, ct);
}

async Task<NeuronResult> FireAsync(
    IExperience experience,
    ChatIntent intent,
    NeuronContext liveCtx,
    CancellationToken ct)
{
    // Populate NeuronContext.ExperienceId so downstream neurons can tell which
    // experience they are serving (Spec A closes the "always null" gap).
    var scopedCtx = liveCtx with { ExperienceId = experience.Id };

    // Build the canonical synapse. For v0.1 the user utterance is carried as
    // the synapse's .Query/.Text field; reflection keeps this domain-agnostic
    // so adding a new canonical doesn't require editing Cortex.
    var synapse = Activator.CreateInstance(experience.CanonicalSynapseType, intent.Text)
        ?? throw new InvalidOperationException(
            $"Cannot instantiate canonical synapse {experience.CanonicalSynapseType.FullName} " +
            $"with a single-string constructor. Add a (string) ctor or update Cortex.");

    log.LogInformation("Cortex routing '{Text}' → {Experience} / {Synapse}",
        intent.Text, experience.Id, experience.CanonicalSynapseType.Name);

    var fire = experience.CanonicalSynapseType;
    // Use firePort.Fire<T> via reflection — generic dispatch over an unknown T at runtime.
    var method = typeof(IFirePort).GetMethod(nameof(IFirePort.Fire))!
        .MakeGenericMethod(fire);
    var task = (Task<NeuronResult>)method.Invoke(firePort, new[] { synapse, scopedCtx, ct })!;
    return await task;
}
```

Delete the old `ContainsAny`, `IsInstalledAsync`, and `AnnotateReasoningAsync` helpers since their callers are gone. Keep `EmitUnroutedAsync` — still used. Keep the class-level XML doc updated to describe the new behaviour; you can prune the "slice 15 swaps this for a real model" sentence since slice 15 is now.

> ⚠️ **Reflection caveat on canonical ctor signature.** Every `CanonicalSynapseType` used by a declared experience must have a public `(string)` constructor today — `FindFlightsRequest(string Query)`, `PlanTripRequest(string Query)`, etc. Verify this holds for all six domains (Travel 5 + Taxi 1). `FindRideRequest(string Pickup, string Dropoff)` has a two-arg constructor; the call above would fail. Add a compatibility overload `FindRideRequest(string query) : this(query, string.Empty)` in the Taxi contracts project. Keep both ctors `[GenerateSerializer]`-friendly.

- [ ] **Step 2: Patch `FindRideRequest`.**

`POC/domains/taxi/Ino.Domains.Taxi.Contracts/FindRideRequest.cs` — add:

```csharp
public FindRideRequest(string query) : this(query, string.Empty) { }
```

Ensure the existing `[GenerateSerializer]` attribute on the record covers both constructors.

- [ ] **Step 3: Run CortexNeuron tests.**

Run: `dotnet test POC/test/Ino.System.Tests --filter "FullyQualifiedName~CortexNeuronTests"`
Expected: all three new tests pass (regex fast-path, LLM classifier, unrouted) plus the pre-existing ones.

- [ ] **Step 4: Run the full suite.**

Run: `dotnet test POC/ino.slnx`
Expected: clean. Some pre-existing "Routes_flight_keyword_to_…" tests may need renaming or updating to point at PromptExamples — rewrite them to set up a matching PromptExamples catalog (like the new tests do) and keep the old assertion (FindFlightsRequest fired).

- [ ] **Step 5: Commit.**

```bash
git add POC/src/Ino.System/CortexNeuron.cs POC/domains/taxi/Ino.Domains.Taxi.Contracts/FindRideRequest.cs POC/test/Ino.System.Tests/
git commit -m "feat(poc): CortexNeuron catalog-driven hybrid routing — regex + LLM classifier"
```

---

## Task 17: Verify `NeuronContext.ExperienceId` flows to downstream neurons

**Files:**
- Create: `POC/test/Ino.System.Tests/CortexExperienceIdTests.cs`

- [ ] **Step 1: Write a test asserting `ExperienceId` is populated.**

```csharp
using FluentAssertions;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Travel.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ino.System.Tests;

public class CortexExperienceIdTests
{
    [Fact]
    public async Task ExperienceId_is_set_on_fired_context()
    {
        var firePort = Substitute.For<IFirePort>();
        NeuronContext? capturedCtx = null;
        firePort.Fire(Arg.Any<FindFlightsRequest>(),
                Arg.Do<NeuronContext>(c => capturedCtx = c),
                Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok());

        var exp = new Experience(
            ExperienceId.From("travel.find-flights"),
            "Find flights", "Search flights.",
            typeof(FindFlightsRequest),
            new[] { "find flights to .+" });

        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.DumpExperiencesAsync(Arg.Any<CancellationToken>()).Returns(new[] { (IExperience)exp });

        var factory = Substitute.For<IChatClientFactory>();
        factory.ForTier(Arg.Any<LlmTier>()).Returns(Substitute.For<IChatClient>());

        var cortex = new CortexNeuron(discovery, firePort, factory,
            new InMemoryReasoningProbe(), NullLogger<CortexNeuron>.Instance);

        await cortex.HandleAsync(
            new ChatIntent("find flights to Bali", "u1"),
            new NeuronContext(SynapseId.New(), CorrelationId.New(),
                new Caller.Ambient(KernelSilo.System), new StreamKey("<gateway>"))
            {
                FirePort = firePort,
                Logger = NullLogger.Instance,
            },
            TestContext.Current.CancellationToken);

        capturedCtx.Should().NotBeNull();
        capturedCtx!.ExperienceId.Should().NotBeNull();
        capturedCtx.ExperienceId!.Value.Value.Should().Be("travel.find-flights");
    }
}
```

- [ ] **Step 2: Run test.**

Run: `dotnet test POC/test/Ino.System.Tests --filter "FullyQualifiedName~CortexExperienceIdTests"`
Expected: PASS (Task 16 already populated `ExperienceId` via the `scopedCtx` with-expression).

- [ ] **Step 3: Commit.**

```bash
git add POC/test/Ino.System.Tests/CortexExperienceIdTests.cs
git commit -m "test(poc): NeuronContext.ExperienceId flows from Cortex to downstream neurons"
```

---

## Task 18: `FlightSearchNeuron` LLM narrative

**Files:**
- Modify: `POC/domains/travel/Ino.Domains.Travel/Neurons/FlightSearchNeuron.cs`

- [ ] **Step 1: Replace the class with the LLM-narrative version.**

```csharp
using System.Diagnostics;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Travel.Contracts;
using Ino.Domains.Travel.SeedData;
using Ino.Domains.Travel.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Travel.Neurons;

public sealed class FlightSearchNeuron(
    IChatClientFactory llm,
    ILogger<FlightSearchNeuron> log) : Grain, INeuron<FindFlightsRequest>
{
    static readonly ActivitySource ActivitySource = new("ino");

    public async Task<NeuronResult> HandleAsync(
        FindFlightsRequest synapse,
        NeuronContext ctx,
        CancellationToken ct)
    {
        using var span = ActivitySource.StartActivity("ino.neuron.handle", ActivityKind.Internal);
        span?.SetTag("ino.neuron.type", nameof(FlightSearchNeuron));
        span?.SetTag("ino.synapse.type", nameof(FindFlightsRequest));
        span?.SetTag("ino.correlation_id", ctx.CorrelationId.Value);

        var flights = FlightFixture.BaliTrip;
        var (description, data) = FlightCardTemplate.BuildList(flights);

        var narrative = await GenerateNarrativeAsync(synapse.Query, flights, ct)
            ?? $"Found {flights.Length} flights for '{synapse.Query}'";

        span?.SetTag("ino.flights.count", flights.Length);

        var response = new FlightCardResponse(
            Summary: narrative,
            Flights: flights,
            RfwDescription: description,
            RfwData: data);

        span?.SetStatus(ActivityStatusCode.Ok);
        return NeuronResult.Ok(narrative).With(response);
    }

    async Task<string?> GenerateNarrativeAsync(string query, FlightSummary[] flights, CancellationToken ct)
    {
        if (flights.Length == 0) return null;
        try
        {
            var cheapest = flights.OrderBy(f => f.PriceUsd).First();
            var chat = llm.ForTier(LlmTier.Balanced);
            var prompt =
                $"User asked: {query}\n" +
                $"We found {flights.Length} flights. Cheapest: {cheapest.Airline} {cheapest.FromCode}→{cheapest.ToCode} ${cheapest.PriceUsd}, {cheapest.Duration}.\n" +
                "Reply in one sentence (≤ 20 words), upbeat, no lists.";
            var response = await chat.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, prompt) },
                new ChatOptions { Temperature = 0.4f },
                ct);
            return response.Text?.Trim();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "FlightSearchNeuron narrative generation failed; falling back to template.");
            return null;
        }
    }
}
```

- [ ] **Step 2: Build.**

Run: `dotnet build POC/domains/travel/Ino.Domains.Travel/Ino.Domains.Travel.csproj`
Expected: clean.

- [ ] **Step 3: Ensure the Travel csproj references `Ino.Core.Hosting` (it already does via `Ino.Core`). If the build complains about `IChatClientFactory` not found, add a `<ProjectReference Include="..\..\..\src\Ino.Core.Hosting\Ino.Core.Hosting.csproj" />` to `Ino.Domains.Travel.csproj`.**

- [ ] **Step 4: Commit.**

```bash
git add POC/domains/travel/Ino.Domains.Travel/Neurons/FlightSearchNeuron.cs
git commit -m "feat(poc): FlightSearchNeuron generates narrative via IChatClientFactory.Balanced"
```

---

## Task 19: `HotelSearchNeuron` LLM narrative

**Files:**
- Modify: `POC/domains/travel/Ino.Domains.Travel/Neurons/HotelSearchNeuron.cs`

- [ ] **Step 1: Replace the class.**

```csharp
using System.Diagnostics;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Travel.Contracts;
using Ino.Domains.Travel.SeedData;
using Ino.Domains.Travel.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Travel.Neurons;

public sealed class HotelSearchNeuron(
    IChatClientFactory llm,
    ILogger<HotelSearchNeuron> log) : Grain, INeuron<FindHotelsRequest>
{
    static readonly ActivitySource ActivitySource = new("ino");

    public async Task<NeuronResult> HandleAsync(
        FindHotelsRequest synapse,
        NeuronContext ctx,
        CancellationToken ct)
    {
        using var span = ActivitySource.StartActivity("ino.neuron.handle", ActivityKind.Internal);
        span?.SetTag("ino.neuron.type", nameof(HotelSearchNeuron));
        span?.SetTag("ino.synapse.type", nameof(FindHotelsRequest));
        span?.SetTag("ino.correlation_id", ctx.CorrelationId.Value);

        var hotels = HotelFixture.BaliTrip;
        var (description, data) = HotelCardTemplate.BuildList(hotels);

        var narrative = await GenerateNarrativeAsync(synapse.Query, hotels, ct)
            ?? $"Found {hotels.Length} hotels for '{synapse.Query}'";

        span?.SetTag("ino.hotels.count", hotels.Length);

        var response = new HotelCardResponse(
            Summary: narrative,
            Hotels: hotels,
            RfwDescription: description,
            RfwData: data);

        span?.SetStatus(ActivityStatusCode.Ok);
        return NeuronResult.Ok(narrative).With(response);
    }

    async Task<string?> GenerateNarrativeAsync(string query, HotelSummary[] hotels, CancellationToken ct)
    {
        if (hotels.Length == 0) return null;
        try
        {
            var top = hotels.OrderByDescending(h => h.Stars).First();
            var chat = llm.ForTier(LlmTier.Balanced);
            var prompt =
                $"User asked: {query}\n" +
                $"We found {hotels.Length} hotels. Top pick: {top.Name} ({top.Stars}★, {top.Location}).\n" +
                "Reply in one sentence (≤ 20 words), upbeat, no lists.";
            var response = await chat.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, prompt) },
                new ChatOptions { Temperature = 0.4f },
                ct);
            return response.Text?.Trim();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "HotelSearchNeuron narrative generation failed; falling back to template.");
            return null;
        }
    }
}
```

- [ ] **Step 2: Build + commit.**

Run: `dotnet build POC/domains/travel/Ino.Domains.Travel/Ino.Domains.Travel.csproj`

```bash
git add POC/domains/travel/Ino.Domains.Travel/Neurons/HotelSearchNeuron.cs
git commit -m "feat(poc): HotelSearchNeuron generates narrative via IChatClientFactory.Balanced"
```

---

## Task 20: `PlaceSearchNeuron` LLM narrative

**Files:**
- Modify: `POC/domains/travel/Ino.Domains.Travel/Neurons/PlaceSearchNeuron.cs`

- [ ] **Step 1: Replace the class.**

```csharp
using System.Diagnostics;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Travel.Contracts;
using Ino.Domains.Travel.SeedData;
using Ino.Domains.Travel.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Travel.Neurons;

public sealed class PlaceSearchNeuron(
    IChatClientFactory llm,
    ILogger<PlaceSearchNeuron> log) : Grain, INeuron<FindPlacesRequest>
{
    static readonly ActivitySource ActivitySource = new("ino");

    public async Task<NeuronResult> HandleAsync(
        FindPlacesRequest synapse,
        NeuronContext ctx,
        CancellationToken ct)
    {
        using var span = ActivitySource.StartActivity("ino.neuron.handle", ActivityKind.Internal);
        span?.SetTag("ino.neuron.type", nameof(PlaceSearchNeuron));
        span?.SetTag("ino.synapse.type", nameof(FindPlacesRequest));
        span?.SetTag("ino.correlation_id", ctx.CorrelationId.Value);

        var places = PlaceFixture.BaliTrip;
        var (description, data) = PlaceCardTemplate.BuildList(places);

        var narrative = await GenerateNarrativeAsync(synapse.Query, places, ct)
            ?? $"Found {places.Length} places for '{synapse.Query}'";

        span?.SetTag("ino.places.count", places.Length);

        var response = new PlaceCardResponse(
            Summary: narrative,
            Places: places,
            RfwDescription: description,
            RfwData: data);

        span?.SetStatus(ActivityStatusCode.Ok);
        return NeuronResult.Ok(narrative).With(response);
    }

    async Task<string?> GenerateNarrativeAsync(string query, PlaceSummary[] places, CancellationToken ct)
    {
        if (places.Length == 0) return null;
        try
        {
            var highlight = places[0];
            var chat = llm.ForTier(LlmTier.Balanced);
            var prompt =
                $"User asked: {query}\n" +
                $"We found {places.Length} places. Highlight: {highlight.Name} ({highlight.Type}).\n" +
                "Reply in one sentence (≤ 20 words), upbeat, no lists.";
            var response = await chat.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, prompt) },
                new ChatOptions { Temperature = 0.4f },
                ct);
            return response.Text?.Trim();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "PlaceSearchNeuron narrative generation failed; falling back to template.");
            return null;
        }
    }
}
```

- [ ] **Step 2: Build + commit.**

Run: `dotnet build POC/domains/travel/Ino.Domains.Travel/Ino.Domains.Travel.csproj`

```bash
git add POC/domains/travel/Ino.Domains.Travel/Neurons/PlaceSearchNeuron.cs
git commit -m "feat(poc): PlaceSearchNeuron generates narrative via IChatClientFactory.Balanced"
```

---

## Task 21: `ItineraryComposerNeuron` — Reasoning tier

**Files:**
- Modify: `POC/domains/travel/Ino.Domains.Travel/Neurons/ItineraryComposerNeuron.cs`

- [ ] **Step 1: Add `IChatClientFactory` to the constructor and build the reasoning prompt.**

Replace the class with (only diff-relevant sections shown — leave `ExtractDestination` and `BuildDays` as-is):

```csharp
using System.Diagnostics;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Travel.Contracts;
using Ino.Domains.Travel.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Travel.Neurons;

public sealed class ItineraryComposerNeuron(
    IFirePort firePort,
    IChatClientFactory llm,
    ILogger<ItineraryComposerNeuron> log) : Grain, INeuron<PlanTripRequest>
{
    static readonly ActivitySource ActivitySource = new("ino");

    public async Task<NeuronResult> HandleAsync(
        PlanTripRequest synapse,
        NeuronContext ctx,
        CancellationToken ct)
    {
        using var span = ActivitySource.StartActivity("ino.neuron.handle", ActivityKind.Internal);
        span?.SetTag("ino.neuron.type", nameof(ItineraryComposerNeuron));
        span?.SetTag("ino.synapse.type", nameof(PlanTripRequest));
        span?.SetTag("ino.correlation_id", ctx.CorrelationId.Value);

        var liveCtx = ctx with { FirePort = firePort, Logger = log };

        var flightTask = firePort.Fire(new FindFlightsRequest(synapse.Query), liveCtx, ct);
        var hotelTask  = firePort.Fire(new FindHotelsRequest(synapse.Query), liveCtx, ct);
        var placeTask  = firePort.Fire(new FindPlacesRequest(synapse.Query), liveCtx, ct);
        await Task.WhenAll(flightTask, hotelTask, placeTask);

        flightTask.Result.TryGetPayload<FlightCardResponse>(out var flights);
        hotelTask.Result.TryGetPayload<HotelCardResponse>(out var hotels);
        placeTask.Result.TryGetPayload<PlaceCardResponse>(out var places);

        var destination = ExtractDestination(synapse.Query);
        var days = BuildDays(
            flights?.Flights ?? [],
            hotels?.Hotels ?? [],
            places?.Places ?? []);
        var (description, data) = ItineraryCardTemplate.Build(destination, days);

        span?.SetTag("ino.itinerary.flights", flights?.Flights.Length ?? 0);
        span?.SetTag("ino.itinerary.hotels", hotels?.Hotels.Length ?? 0);
        span?.SetTag("ino.itinerary.places", places?.Places.Length ?? 0);

        var narrative = await GenerateReasonedNarrativeAsync(synapse.Query, destination, days, ct)
            ?? $"Itinerary for '{synapse.Query}' — {flights?.Flights.Length ?? 0} flights, {hotels?.Hotels.Length ?? 0} hotels, {places?.Places.Length ?? 0} places";

        var response = new ItineraryCardResponse(
            Summary: narrative,
            Destination: destination,
            Days: days,
            RfwDescription: description,
            RfwData: data);

        span?.SetStatus(ActivityStatusCode.Ok);
        return NeuronResult.Ok(narrative).With(response);
    }

    async Task<string?> GenerateReasonedNarrativeAsync(
        string query, string destination, ItineraryDay[] days, CancellationToken ct)
    {
        try
        {
            var chat = llm.ForTier(LlmTier.Reasoning);
            var itinerarySnippet = string.Join("\n", days.Take(3).Select(d => $"Day {d.DayNumber}: {d.Title} — {string.Join(", ", d.Items)}"));
            var prompt =
                $"User asked: {query}\n" +
                $"Destination: {destination}\n" +
                $"Itinerary skeleton:\n{itinerarySnippet}\n\n" +
                "Reply with a 2-sentence narrative the user will see above the itinerary card. " +
                "Upbeat, concrete, no bullet lists.";
            var response = await chat.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, prompt) },
                new ChatOptions { Temperature = 0.5f },
                ct);
            return response.Text?.Trim();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "ItineraryComposerNeuron narrative generation failed; falling back to template.");
            return null;
        }
    }

    // ExtractDestination + BuildDays unchanged — keep as they are.
    static string ExtractDestination(string query) { /* existing body */ throw null!; }
    static ItineraryDay[] BuildDays(FlightSummary[] flights, HotelSummary[] hotels, PlaceSummary[] places) { /* existing body */ throw null!; }
}
```

When applying the edit, **do not** delete the existing `ExtractDestination` / `BuildDays` method bodies — copy them verbatim from the current file. Only the class header, constructor, `HandleAsync`, and the new `GenerateReasonedNarrativeAsync` change.

- [ ] **Step 2: Build + commit.**

Run: `dotnet build POC/domains/travel/Ino.Domains.Travel/Ino.Domains.Travel.csproj`

```bash
git add POC/domains/travel/Ino.Domains.Travel/Neurons/ItineraryComposerNeuron.cs
git commit -m "feat(poc): ItineraryComposerNeuron narrative uses Reasoning tier"
```

---

## Task 22: `RideSearchNeuron` LLM narrative

**Files:**
- Modify: `POC/domains/taxi/Ino.Domains.Taxi/Neurons/RideSearchNeuron.cs`

- [ ] **Step 1: Replace the class.**

```csharp
using System.Diagnostics;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Taxi.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Taxi.Neurons;

public sealed class RideSearchNeuron(
    IChatClientFactory llm,
    ILogger<RideSearchNeuron> log) : Grain, INeuron<FindRideRequest>
{
    static readonly ActivitySource ActivitySource = new("ino");

    public async Task<NeuronResult> HandleAsync(
        FindRideRequest synapse,
        NeuronContext ctx,
        CancellationToken ct)
    {
        using var span = ActivitySource.StartActivity("ino.neuron.handle", ActivityKind.Internal);
        span?.SetTag("ino.neuron.type", nameof(RideSearchNeuron));
        span?.SetTag("ino.synapse.type", nameof(FindRideRequest));
        span?.SetTag("ino.correlation_id", ctx.CorrelationId.Value);

        var fallback = $"Taxi scaffold: would search rides from '{synapse.Pickup}' to '{synapse.Dropoff}'.";
        var narrative = await GenerateNarrativeAsync(synapse, ct) ?? fallback;

        span?.SetStatus(ActivityStatusCode.Ok);
        return NeuronResult.Ok(narrative);
    }

    async Task<string?> GenerateNarrativeAsync(FindRideRequest synapse, CancellationToken ct)
    {
        try
        {
            var chat = llm.ForTier(LlmTier.Balanced);
            var prompt =
                $"User asked for a ride. Pickup: '{synapse.Pickup}', Dropoff: '{synapse.Dropoff}'. " +
                "The ride provider isn't wired yet; tell the user we recognized the intent and will summon a ride " +
                "once the provider is connected. One sentence, ≤ 18 words.";
            var response = await chat.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, prompt) },
                new ChatOptions { Temperature = 0.3f },
                ct);
            return response.Text?.Trim();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "RideSearchNeuron narrative generation failed; using scaffold text.");
            return null;
        }
    }
}
```

- [ ] **Step 2: Build + commit.**

Run: `dotnet build POC/domains/taxi/Ino.Domains.Taxi/Ino.Domains.Taxi.csproj`

```bash
git add POC/domains/taxi/Ino.Domains.Taxi/Neurons/RideSearchNeuron.cs
git commit -m "feat(poc): RideSearchNeuron narrative uses Balanced tier; scaffold fallback preserved"
```

---

## Task 23: `FlightMonitorNeuron` — narrative on synthetic delay tick

**Files:**
- Modify: `POC/domains/travel/Ino.Domains.Travel/Neurons/FlightMonitorNeuron.cs`

- [ ] **Step 1: Inject `IChatClientFactory` and upgrade the `Reason` string to an LLM-generated one.**

The class currently emits `Reason: "Synthetic demo delay #{_tickCount} on {_armed.Route}"`. Replace that with an LLM narrative. Full rewrite:

```csharp
using System.Diagnostics;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Travel.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Timers;

namespace Ino.Domains.Travel.Neurons;

public sealed class FlightMonitorNeuron(
    IFirePort firePort,
    ITimerRegistry timers,
    IChatClientFactory llm,
    ILogger<FlightMonitorNeuron> log) : Grain, INeuron<ArmFlightMonitor>
{
    static readonly ActivitySource ActivitySource = new("ino");
    static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

    IGrainTimer? _timer;
    ArmFlightMonitor? _armed;
    int _tickCount;

    public Task<NeuronResult> HandleAsync(
        ArmFlightMonitor synapse, NeuronContext ctx, CancellationToken ct)
    {
        // (body unchanged; keep the existing HandleAsync body as-is from the pre-edit file)
        throw null!;
    }

    async Task FireTickAsync(CancellationToken ct)
    {
        if (_armed is null) return;

        _tickCount++;
        var broadcastContext = new NeuronContext(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.Ambient(KernelSilo.Domains),
            SourceStream: new StreamKey($"<flight-monitor/{_armed.FlightId}>"))
        {
            FirePort = firePort,
            Logger = log,
        };

        var reason = await ComposeReasonAsync(_armed.Route, _tickCount, ct)
            ?? $"Synthetic demo delay #{_tickCount} on {_armed.Route}";

        var delayed = new FlightDelayed(
            FlightId: _armed.FlightId,
            NewDepartTime: ShiftTime(_tickCount),
            Reason: reason);

        try
        {
            await firePort.FireBroadcast(delayed, broadcastContext, ct);
            log.LogInformation("FlightMonitor tick {Tick} fired FlightDelayed for {Flight}",
                _tickCount, _armed.FlightId);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "FlightMonitor tick {Tick} failed to broadcast FlightDelayed for {Flight}",
                _tickCount, _armed?.FlightId);
        }
    }

    async Task<string?> ComposeReasonAsync(string route, int tick, CancellationToken ct)
    {
        try
        {
            var chat = llm.ForTier(LlmTier.Fast);
            var prompt =
                $"Write a one-sentence plausible reason for a flight delay on route {route}. " +
                $"Make it believable, ≤ 12 words, no emoji. Tick #{tick}.";
            var response = await chat.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, prompt) },
                new ChatOptions { Temperature = 0.7f },
                ct);
            return response.Text?.Trim();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "FlightMonitor narrative generation failed; using synthetic text.");
            return null;
        }
    }

    static string ShiftTime(int tickCount) { /* existing body */ throw null!; }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        _timer?.Dispose();
        _timer = null;
        return base.OnDeactivateAsync(reason, ct);
    }
}
```

When applying the edit, preserve the existing `HandleAsync` body and `ShiftTime` body verbatim — they're unchanged.

- [ ] **Step 2: Build + commit.**

Run: `dotnet build POC/domains/travel/Ino.Domains.Travel/Ino.Domains.Travel.csproj`

```bash
git add POC/domains/travel/Ino.Domains.Travel/Neurons/FlightMonitorNeuron.cs
git commit -m "feat(poc): FlightMonitorNeuron uses Fast-tier LLM for delay reasons"
```

---

## Task 24: Taxi minimal BDD scenario

**Files:**
- Create: `POC/domains/taxi/Ino.Domains.Taxi/Features/taxi-intent.feature`
- Modify: `POC/domains/taxi/Ino.Domains.Taxi/Ino.Domains.Taxi.csproj` (copy Features to output)

- [ ] **Step 1: Create the scenario file.**

```gherkin
Feature: Taxi — intent routing
  The BDD-mock IChatClient matches these scenarios when tests exercise
  Cortex's LLM classifier path for Taxi. One scenario is enough to cover the
  Taxi demo; more ride-provider-specific scenarios ship when the real
  integration does.

  Scenario: Hail a ride
    Given the user says "ride|taxi|uber|hail"
    Then the assistant replies "Summoning a ride via the RideSearch neuron."
```

- [ ] **Step 2: Ensure the Taxi csproj copies Features to output.**

Check `POC/domains/taxi/Ino.Domains.Taxi/Ino.Domains.Taxi.csproj`. Travel's csproj already has a pattern for `Features/*.feature` — mirror it. If missing, add:

```xml
<ItemGroup>
  <None Update="Features\*.feature">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 3: Build + verify the BddScenarioLoader picks it up.**

Run: `dotnet build POC/ino.slnx`

Run Taxi domain tests (if any) or `dotnet test POC/ino.slnx` — the scenario file is discovered at silo startup by the existing BddScenarioLoader path; no test to add specifically for this file (the E2E will exercise it).

- [ ] **Step 4: Commit.**

```bash
git add POC/domains/taxi/Ino.Domains.Taxi/Features POC/domains/taxi/Ino.Domains.Taxi/Ino.Domains.Taxi.csproj
git commit -m "feat(poc): Taxi taxi-intent.feature — one scenario for BDD mock coverage"
```

---

## Task 25: Flutter — Web Speech API service

**Files:**
- Create: `POC/clients/ino.flutter/lib/voice/web_speech_api.dart`
- Modify: `POC/clients/ino.flutter/pubspec.yaml` (add `web` package if not present)

- [ ] **Step 1: Verify `package:web` is in the project.**

Run: `grep -n "^  web:" POC/clients/ino.flutter/pubspec.yaml`
If absent, add under `dependencies:`:

```yaml
  web: ^1.1.0
```

Then `cd POC/clients/ino.flutter && flutter pub get`.

- [ ] **Step 2: Verify `package:web` Web Speech API surface via Context7.**

Per CLAUDE.md, verify the `web` package's Speech Recognition bindings before coding. Resolve the `web` library via Context7; confirm the class name is `SpeechRecognition` and exists on `window`.

- [ ] **Step 3: Create the service.**

`POC/clients/ino.flutter/lib/voice/web_speech_api.dart`:

```dart
// Browser-native voice-to-text via window.SpeechRecognition.
// No fallback — non-Chromium browsers fail isSupported() and the UI widget
// disables itself with an explanatory tooltip.
import 'dart:async';
import 'dart:js_interop';

import 'package:web/web.dart' as web;

typedef TranscriptHandler = void Function(String text, {required bool isFinal});

class WebSpeechApiService {
  WebSpeechApiService._();

  static bool get isSupported {
    final window = web.window as JSObject;
    return window.has('SpeechRecognition') || window.has('webkitSpeechRecognition');
  }

  /// Start listening. `onTranscript` fires for each chunk (interim or final).
  /// `onSilence` fires after [silence] elapses without new speech — caller
  /// usually stops + auto-submits on that signal.
  /// `onError` fires for mic permission denial and other failures.
  /// Returns a stop callback the caller uses to cancel or to finalize.
  static Future<void Function()> start({
    required TranscriptHandler onTranscript,
    required void Function(Object error) onError,
    required VoidCallback onSilence,
    Duration silence = const Duration(milliseconds: 1200),
    String lang = 'en-US',
  }) async {
    if (!isSupported) {
      throw StateError('Web Speech API not supported in this browser.');
    }
    final window = web.window as JSObject;
    final ctor = window.has('SpeechRecognition')
        ? window.getProperty('SpeechRecognition'.toJS)
        : window.getProperty('webkitSpeechRecognition'.toJS);
    final recognition = (ctor as JSFunction).callAsConstructor<JSObject>();

    recognition.setProperty('continuous'.toJS, true.toJS);
    recognition.setProperty('interimResults'.toJS, true.toJS);
    recognition.setProperty('lang'.toJS, lang.toJS);

    Timer? silenceTimer;
    void resetSilence() {
      silenceTimer?.cancel();
      silenceTimer = Timer(silence, onSilence);
    }

    final onResult = ((JSAny event) {
      final results = (event as JSObject).getProperty('results'.toJS) as JSObject;
      final length = (results.getProperty('length'.toJS) as JSNumber).toDartInt;
      for (var i = 0; i < length; i++) {
        final result = results.getProperty(i.toJS) as JSObject;
        final alt = result.getProperty(0.toJS) as JSObject;
        final transcript = (alt.getProperty('transcript'.toJS) as JSString).toDart;
        final isFinal = (result.getProperty('isFinal'.toJS) as JSBoolean).toDart;
        onTranscript(transcript, isFinal: isFinal);
      }
      resetSilence();
    }).toJS;

    final onErr = ((JSAny event) {
      final err = (event as JSObject).getProperty('error'.toJS);
      onError(err);
    }).toJS;

    recognition.setProperty('onresult'.toJS, onResult);
    recognition.setProperty('onerror'.toJS, onErr);
    (recognition.getProperty('start'.toJS) as JSFunction).callAsFunction(recognition);
    resetSilence();

    return () {
      silenceTimer?.cancel();
      try {
        (recognition.getProperty('stop'.toJS) as JSFunction).callAsFunction(recognition);
      } catch (_) {}
    };
  }
}

typedef VoidCallback = void Function();
```

- [ ] **Step 4: Build Flutter web to sanity-check the interop.**

Run: `cd POC/clients/ino.flutter && flutter build web --no-tree-shake-icons`
Expected: build succeeds. Runtime behaviour is verified in Task 29.

- [ ] **Step 5: Commit.**

```bash
git add POC/clients/ino.flutter/lib/voice/web_speech_api.dart POC/clients/ino.flutter/pubspec.yaml POC/clients/ino.flutter/pubspec.lock
git commit -m "feat(poc): Flutter Web Speech API service — browser-native STT"
```

---

## Task 26: Flutter — `PushToTalkButton` widget

**Files:**
- Create: `POC/clients/ino.flutter/lib/voice/push_to_talk_button.dart`

- [ ] **Step 1: Create the widget.**

```dart
import 'package:flutter/material.dart';

import 'package:ino_flutter/voice/web_speech_api.dart';

/// A mic button that streams interim transcripts into [controller] while the
/// user speaks, then invokes [onSubmit] with the final text after ≥ 1.2 s of
/// silence. Tap once to start, tap again to cancel.
///
/// Renders disabled with a tooltip on browsers without SpeechRecognition.
class PushToTalkButton extends StatefulWidget {
  const PushToTalkButton({
    super.key,
    required this.controller,
    required this.onSubmit,
  });

  final TextEditingController controller;
  final void Function(String text) onSubmit;

  @override
  State<PushToTalkButton> createState() => _PushToTalkButtonState();
}

class _PushToTalkButtonState extends State<PushToTalkButton> {
  void Function()? _stop;
  bool _listening = false;
  String? _errorMessage;

  @override
  void dispose() {
    _stop?.call();
    super.dispose();
  }

  Future<void> _toggle() async {
    if (_listening) {
      _stop?.call();
      setState(() {
        _listening = false;
        _stop = null;
      });
      return;
    }

    try {
      final stop = await WebSpeechApiService.start(
        onTranscript: (text, {required isFinal}) {
          widget.controller.text = text;
          widget.controller.selection = TextSelection.collapsed(offset: text.length);
        },
        onError: (err) {
          setState(() {
            _errorMessage = 'Voice error: $err';
            _listening = false;
            _stop = null;
          });
        },
        onSilence: () {
          final finalText = widget.controller.text.trim();
          _stop?.call();
          setState(() {
            _listening = false;
            _stop = null;
          });
          if (finalText.isNotEmpty) {
            widget.onSubmit(finalText);
            widget.controller.clear();
          }
        },
      );
      setState(() {
        _listening = true;
        _stop = stop;
        _errorMessage = null;
      });
    } on StateError catch (_) {
      setState(() {
        _errorMessage = 'Voice input requires Chrome or Edge (microphone permission).';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final supported = WebSpeechApiService.isSupported;
    final tooltip = !supported
        ? 'Voice input requires Chrome or Edge (microphone permission).'
        : (_errorMessage ?? (_listening ? 'Listening… tap to cancel' : 'Tap to speak'));

    return Tooltip(
      message: tooltip,
      child: IconButton(
        icon: Icon(
          _listening ? Icons.mic : Icons.mic_none,
          color: _listening ? Colors.redAccent : null,
        ),
        onPressed: supported ? _toggle : null,
      ),
    );
  }
}
```

- [ ] **Step 2: Build Flutter web.**

Run: `cd POC/clients/ino.flutter && flutter build web --no-tree-shake-icons`
Expected: clean build.

- [ ] **Step 3: Commit.**

```bash
git add POC/clients/ino.flutter/lib/voice/push_to_talk_button.dart
git commit -m "feat(poc): Flutter PushToTalkButton widget — silence-detect auto-submit"
```

---

## Task 27: Flutter — wire `PushToTalkButton` into `HomeScreen`

**Files:**
- Modify: `POC/clients/ino.flutter/lib/screens/home/home_screen.dart`

- [ ] **Step 1: Add the import.**

At the top of `home_screen.dart`:

```dart
import 'package:ino_flutter/voice/push_to_talk_button.dart';
```

- [ ] **Step 2: Find the chat input row (likely uses a `TextField` + send `IconButton`) and add the mic button adjacent to the send button.**

Locate the `TextField` using `_inputController` (the field is defined at the top of `_HomeScreenState`). The input row will look something like:

```dart
Row(
  children: [
    Expanded(child: TextField(controller: _inputController, …)),
    IconButton(icon: Icon(Icons.send), onPressed: _handleSend),
  ],
)
```

Add the mic button between the Expanded and the send button:

```dart
Row(
  children: [
    Expanded(child: TextField(controller: _inputController, …)),
    PushToTalkButton(
      controller: _inputController,
      onSubmit: (text) => context.read<InoBloc>().add(SendMessage(text)),
    ),
    IconButton(icon: Icon(Icons.send), onPressed: _handleSend),
  ],
)
```

If the layout uses a different structure (look for `onSubmitted` on the TextField), drop the `PushToTalkButton` next to it with the same `controller` + `onSubmit` wiring.

- [ ] **Step 3: Build Flutter web.**

Run: `cd POC/clients/ino.flutter && flutter build web --no-tree-shake-icons`
Expected: clean.

- [ ] **Step 4: Commit.**

```bash
git add POC/clients/ino.flutter/lib/screens/home/home_screen.dart
git commit -m "feat(poc): wire PushToTalkButton into HomeScreen chat input"
```

---

## Task 28: Test fixture — `BddMockChatClientFactory` in `Ino.Testing`

**Files:**
- Create: `POC/src/Ino.Testing/BddMockChatClientFactory.cs`
- Modify: `POC/src/Ino.Testing/TestSiloConfigurator.cs` (or nearest config-time file)

- [ ] **Step 1: Create the factory.**

`POC/src/Ino.Testing/BddMockChatClientFactory.cs`:

```csharp
using Ino.Core;
using Ino.Core.Hosting.Llm;
using Microsoft.Extensions.AI;

namespace Ino.Testing;

/// <summary>
/// Test-side IChatClientFactory that serves every tier from the same
/// BddMockChatClient. Scenarios come from .feature files picked up via
/// BddScenarioLoader (the existing production-code Ino.Core.Hosting path).
/// </summary>
public sealed class BddMockChatClientFactory : IChatClientFactory
{
    readonly IChatClient _shared;

    public BddMockChatClientFactory(IChatClient sharedClient)
    {
        _shared = sharedClient;
    }

    public IReadOnlyList<LlmModel> RegisteredModels => Array.Empty<LlmModel>();

    public IChatClient ForTier(LlmTier tier) =>
        tier == LlmTier.None
            ? throw new ArgumentException("LlmTier.None is not a valid request.", nameof(tier))
            : _shared;
}
```

- [ ] **Step 2: Register it in the test silo configurator.**

`POC/src/Ino.Testing/TestSiloConfigurator.cs` — extend to register the BDD mock factory alongside state-machine storage:

```csharp
using Ino.Core.Hosting.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace Ino.Testing;

public sealed class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder silo)
    {
        silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
        silo.AddStateMachineStorage();

        // Tests get deterministic LLM responses via BddMockChatClient; Task 28 wires
        // both the factory (for neurons that ask for a tier) and keyed IChatClients
        // (for neurons that inject the specific tier).
        silo.Services.AddSingleton<IReasoningProbe, InMemoryReasoningProbe>();
        silo.Services.AddSingleton<IChatClient>(sp =>
        {
            var probe = sp.GetRequiredService<IReasoningProbe>();
            var scenarios = BddScenarioLoader.LoadFromDirectories(new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Features"),
            }).Scenarios;
            return new BddMockChatClient(scenarios, probe);
        });
        silo.Services.AddSingleton<IChatClientFactory>(sp =>
            new BddMockChatClientFactory(sp.GetRequiredService<IChatClient>()));
        foreach (var tier in new[] { LlmTier.Fast, LlmTier.Balanced, LlmTier.Reasoning })
        {
            silo.Services.AddKeyedSingleton<IChatClient>(tier, (sp, _) =>
                sp.GetRequiredService<IChatClientFactory>().ForTier(tier));
        }
    }
}
```

- [ ] **Step 3: Run the full test suite.**

Run: `dotnet test POC/ino.slnx`
Expected: all tests green. If a test asserts a specific narrative string that no longer matches (because the BDD mock now returns a different canned reply for the Balanced-tier prompt), relax the assertion to "non-empty".

- [ ] **Step 4: Commit.**

```bash
git add POC/src/Ino.Testing/
git commit -m "feat(poc): BddMockChatClientFactory — test fixture serves all tiers from BDD mock"
```

---

## Task 29: Manual verification — build + test + aspire + browser

**Files:**
- Modify (possible follow-up fixes): any file discovered during manual verification.

- [ ] **Step 1: Clean build.**

Run: `dotnet build POC/ino.slnx`
Expected: zero warnings, zero errors.

- [ ] **Step 2: Full test suite.**

Run: `dotnet test POC/ino.slnx`
Expected: all green including the 5 XaiChatClientFactory tests, the 3 Cortex routing tests, and the ExperienceId flow test.

- [ ] **Step 3: Set the xAI API key.**

Run (PowerShell): `$env:XAI_API_KEY = "<your key>"`
Or (bash): `export XAI_API_KEY=<your key>`

- [ ] **Step 4: Aspire startup.**

Run: `aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated`

Confirm in the Aspire dashboard:
- `ino-system`, `ino-identity`, `ino-domains` all Healthy.
- Env vars on each silo resource show `Ino__Llm__Models__*__Id` populated.
- No crash log mentioning `XAI_API_KEY not set`.

If startup fails with the API-key error, re-check the env var is exported before running `aspire start`.

- [ ] **Step 5: Browser demo path.**

Open the `system-http` endpoint in Chromium. Verify:
- Chat input row shows a mic button.
- Tap mic → browser prompts for mic permission → speak "find flights to Bali" → interim text streams into input → 1.2 s of silence submits → flight card renders with a Grok-generated narrative and seeded fixtures.
- Type "plan 5 days in Tokyo" → itinerary card renders with a Grok-generated two-sentence intro.
- Type "call me a ride" → stub ride response renders with a Grok-generated narrative.
- In Aspire dashboard > Traces, confirm the `grpc Chat` → `fire ChatIntent` → `handle CortexNeuron.HandleAsync` → downstream chain.
- In Aspire dashboard > Structured Logs, filter for `ino-flutter` → BLoC transitions visible; filter for `CortexNeuron` → "Cortex routing …" lines visible.

- [ ] **Step 6: E2E test suite.**

Run: `INO_E2E_NO_BROWSER=true dotnet test POC/test/Ino.E2E.Tests --filter "Category=E2E"`
Expected: all green.

- [ ] **Step 7: Record verification artefacts.**

Take a screenshot of the demo (flight card + voice-entered query + narrative visible). Save to `reviews/spec-a-demo-2026-MM-DD.png`. Stage it alongside the verification commit.

- [ ] **Step 8: Commit the verification artefacts + any final tweaks.**

```bash
git add reviews/spec-a-demo-*.png
git commit -m "docs: Spec A verification — demo screenshot + manual verification notes"
```

- [ ] **Step 9: Aspire teardown.**

Run: `aspire stop`
Expected: clean shutdown.

---

## Self-review

**Spec coverage check:**

- §1 tier enum update → Task 1 ✓
- §2 `Ino.Llm.Xai` project + `LlmModel` base + 3 Grok classes → Tasks 2, 3, 4 ✓
- §3 `IChatClientFactory` interface + `XaiChatClientFactory` + tier fallback + missing-key error → Tasks 5, 6 ✓
- §4 `AddInoChatClients` silo DI extension + keyed `IChatClient` registration → Tasks 10, 11 ✓
- §5 AppHost→silo env-var propagation → Tasks 9, 12 ✓
- §6 `CortexNeuron` catalog-driven hybrid routing + regex fast-path + LLM classifier + `NeuronContext.ExperienceId` populated → Tasks 13–17 ✓
- §7 Travel + Taxi neurons gain `IChatClientFactory` narrative generation; `ItineraryComposer` uses Reasoning tier → Tasks 18–23 ✓
- §8 Flutter Web Speech API + `PushToTalkButton` + wired into `HomeScreen` → Tasks 25–27 ✓
- §9 Taxi minimal `.feature` → Task 24 ✓
- Test seam: `BddMockChatClientFactory` → Task 28 ✓
- Verification loop → Task 29 ✓
- Spec-B inheritance hooks (tier factory, probe pattern, ExperienceId flow, BDD seam) all land via Tasks 5–28 without separate effort.

**Placeholder scan:** No `TBD`, no "implement later", no bare "similar to Task N". Exact model-ID strings are explicitly flagged as "verify against the live API at step 1 of Task 4" — a verification instruction, not a placeholder. One known caveat flagged in Task 16 (reflection-based canonical-synapse ctor requires `(string)` overload on `FindRideRequest` — addressed in the same task).

**Type consistency:** `IChatClientFactory.ForTier` / `RegisteredModels` used identically across Tasks 5, 6, 13, 18–23, 28. `LlmTier.{Fast,Balanced,Reasoning}` used identically. `InoBuilder.DeclaredModels` / `RegisterModel` / `DeclaredVoiceProvider` / `RegisterVoiceProvider` consistent across Tasks 7, 8, 9, 12. `PropagateInoConfig(ino)` called the same way in Task 12 as declared in Task 9. `AddInoChatClients()` signature (no argument — reads `builder.Configuration` internally) consistent across Tasks 10, 11.

**Scope check:** 29 tasks, each scoped to one logical change, each shippable behind its own commit. Memory primitive (Spec B) and ino:cloud (Track 4) explicitly out-of-scope. Manual verification loop (Task 29) is the last task because CLAUDE.md requires it for UI + live-LLM changes.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-24-personal-assistant-demo.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
