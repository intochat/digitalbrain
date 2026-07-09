# Multi-Provider LLM/Voice Registry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the exact gap that caused Ino to hallucinate instead of using real tool-calling (the model registry already exists but role/capability info dead-ends before the kernel runtime), add a capability axis orthogonal to the existing Fast/Balanced/Reasoning quality tiers, give grains the `[Llm<TModel>] IChatClient` / `[Voice2Text<TModel>] IVoiceTranscriber` dependency-injection ergonomic (ported from a verified sibling project's pattern, built on Orleans' own constructor-facet extensibility — not a bespoke DI hack), and add real Anthropic/xAI providers plus a genuinely tool-capable Ollama model.

**Architecture:** DigitalBrain already has ~80% of this (`DigitalBrainBuilderExtensions.cs`'s `WithLLM<T>()/.AsFast()/.AsBalanced()/.AsReasoning()` fluent builder, `DigitalBrainModelRegistry`, per-registration env export). This plan extends it in five phases:
1. **Capability foundation** — add `DigitalBrainModelCapabilities` (SupportsTools/Vision/Streaming/StructuredOutput) and `ServiceKey` to the model layer.
2. **Generalized registry reader** — consolidate the two already-duplicated ad hoc "read the full registry from config" readers (one in `DigitalBrainLlmRuntimeOptions`, one in `DigitalBrainVoiceRuntimeOptions`) into one shared reader that can filter by kind/provider/role/capability.
3. **Keyed DI + attribute-facet ergonomic** — register one keyed `IChatClient`/voice-equivalent per model at kernel startup; add `LlmAttribute<TModel>`/`Voice2TextAttribute<TModel>` implementing Orleans' `IFacetMetadata`/`IAttributeToFactoryMapper<T>` (the same extensibility point `[PersistentState]` itself uses — verified against Orleans' actual source, not guessed), giving grain constructors `[Llm<TModel>] IChatClient chatClient` / `[Voice2Text<TModel>] IVoiceTranscriber transcriber` directly.
4. **Real providers** — Anthropic (official `Anthropic` NuGet package, has first-party `AsIChatClient()`), xAI (no dedicated SDK needed — the official `OpenAI` SDK pointed at `https://api.x.ai/v1`, exactly how the one real reference-repo xAI integration already does it), and a genuinely tool-capable Ollama model.
5. **Wire the actual fix + refresh** — Ino's generic tool-calling path resolves the tool-capable registered model instead of the single flat global default; AppHost gets the full multi-provider example.

**Tech Stack:** .NET (net11.0), Orleans grains (`IFacetMetadata`/`IAttributeToFactoryMapper<T>` constructor-facet extensibility), Aspire hosting (`DigitalBrainBuilderExtensions.cs`), `Microsoft.Extensions.AI` (`IChatClient`, `AsIChatClient()`), xunit 2.9.3.

## Global Constraints

- Run tests with `dotnet test --logger "console;verbosity=minimal"` from the repo root only. **Never use `--filter`.**
- Add packages with `dotnet add package <Id>` (no pinned version) so central package management resolves the current latest into `Directory.Packages.props`.
- Use Context7 (or the actual package/framework source on GitHub, when Context7's indexed docs don't cover the specific API) before writing code touching a new package API. This plan's Orleans facet signatures (`IFacetMetadata`, `IAttributeToFactoryMapper<TMetadata>`, `GrainConstructorArgumentFactory`) were verified against the real `dotnet/orleans` source, not guessed. The Anthropic SDK's `AsIChatClient()` shape was verified against `anthropics/anthropic-sdk-csharp`'s real source via Context7.
- No vacuous `/// <summary>` comments on NEW code — this repo's existing files (`LlmModels.cs`, `DigitalBrainModelCatalog.cs`) already use brief `///` summaries pervasively; match that existing convention for new types in those same files rather than introducing a second style. Small inline `//` comments only for non-obvious constraints elsewhere.
- Relative paths only — never reference `C:\Users\`.
- Follow existing patterns in the file being modified; don't restructure beyond each task's stated intent.
- Run `/code-review` before declaring any task's work done, per this repo's CLAUDE.md.

---

## Explicitly out of scope for this plan

- **GitHub Models provider** — not requested; the OpenAI-compatible-endpoint pattern (Task 7) generalizes to it easily later if wanted.
- **Restructuring `DigitalBrainOptions.AsFast()/.AsBalanced()/.AsReasoning()`'s "last registration" tracking** to be kind-aware (today calling `.AsFast()` after `.WithEmbedding<T>()` would incorrectly tag the embedding registration with an LLM-only role concept) — a real, narrow, pre-existing quirk, not something this plan's scope touches.
- **Foundry Local voice-to-text** — the current local Whisper pipeline (a `speaches` container + `OpenAICompatibleVoiceTranscriber`) already works end-to-end; "Foundry" already names an unrelated self-evolution subsystem in this repo, and introducing Foundry Local would collide with that name for no functional gain right now.

---

## Task 1: `DigitalBrainModelCapabilities` + `ServiceKey` on the model layer

**Files:**
- Modify: `src/DigitalBrain.Core/Models/DigitalBrainModelCatalog.cs` (add `DigitalBrainModelCapabilities` record + presets; add `Capabilities`/`ServiceKey` to `DigitalBrainModelDescriptor`)
- Modify: `src/DigitalBrain.Aspire/LlmModels.cs` (`DigitalBrainModel.Describe()` passes capabilities through; existing 4 models get an explicit `Capabilities` override)
- Test: `tests/DigitalBrain.Tests/Aspire/DigitalBrainModelCapabilitiesTests.cs`

**Interfaces:**
- Produces: `DigitalBrainModelCapabilities(bool SupportsTools, bool SupportsVision, bool SupportsStreaming, bool SupportsStructuredOutput)` with static presets `FullyCapable`, `ChatOnly`, `ToolCapable`; `DigitalBrainModelDescriptor` gains `Capabilities` and `ServiceKey` members; `DigitalBrainModel` gains `virtual DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.FullyCapable`.

- [ ] **Step 1: Write the failing test**

This task is self-contained — it uses a private test-local model rather than referencing the real tool-capable Ollama model Task 9 adds later, so this task doesn't depend on later ones landing first:

```csharp
using DigitalBrain.Aspire;
using DigitalBrain.Core.Models;
using Xunit;

namespace DigitalBrain.Tests.Aspire;

public class DigitalBrainModelCapabilitiesTests
{
    [Fact]
    public void ToolCapableModelDescribesItselfWithToolsCapabilityAndStableServiceKey()
    {
        var model = new ToolCapableTestModel();

        var descriptor = model.Describe();

        Assert.True(descriptor.Capabilities.SupportsTools);
        Assert.Equal("ollama-tool-capable-test", descriptor.ServiceKey);
    }

    [Fact]
    public void DefaultModelCapabilitiesIsFullyCapable()
    {
        var model = new DefaultTestModel();

        Assert.Equal(DigitalBrainModelCapabilities.FullyCapable, model.Describe().Capabilities);
    }

    [Fact]
    public void ServiceKeyNormalizesColonsAndDotsForUseAsADotnetKeyedServiceKey()
    {
        var descriptor = new DigitalBrainModelDescriptor(
            DigitalBrainCapabilityKind.LargeLanguageModel,
            "ollama",
            "qwen2.5-coder:1.5b",
            "Qwen 2.5 Coder 1.5B",
            DigitalBrainModelCapabilities.ChatOnly);

        Assert.Equal("ollama-qwen2-5-coder-1-5b", descriptor.ServiceKey);
    }

    private sealed class ToolCapableTestModel : LlmModel
    {
        public override string Provider => DigitalBrainProviderIds.Ollama;
        public override string Id => "tool-capable-test";
        public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ToolCapable;
    }

    private sealed class DefaultTestModel : LlmModel
    {
        public override string Provider => DigitalBrainProviderIds.Ollama;
        public override string Id => "default-test";
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: FAIL to build — `DigitalBrainModelCapabilities` does not exist yet, `DigitalBrainModelDescriptor` has no `ServiceKey`/`Capabilities`, `DigitalBrainModel` has no `Capabilities` member.

- [ ] **Step 3: Write minimal implementation**

In `src/DigitalBrain.Core/Models/DigitalBrainModelCatalog.cs`, add (near the other records, after `DigitalBrainModelRole`):

```csharp
/// <summary>
/// Capability flags for a provider/model registration, orthogonal to <see cref="DigitalBrainModelRole"/>
/// (a quality/cost tier). A Fast-tier model and a Reasoning-tier model can each independently support or
/// lack tool-calling — role says "how good," capabilities say "what it can actually do."
/// </summary>
public sealed record DigitalBrainModelCapabilities(
    bool SupportsTools,
    bool SupportsVision,
    bool SupportsStreaming,
    bool SupportsStructuredOutput)
{
    public static readonly DigitalBrainModelCapabilities FullyCapable = new(true, true, true, true);
    public static readonly DigitalBrainModelCapabilities ChatOnly = new(false, false, true, false);
    public static readonly DigitalBrainModelCapabilities ToolCapable = new(true, false, true, true);
}
```

Replace the `DigitalBrainModelDescriptor` record:

```csharp
/// <summary>
/// Provider/model metadata shared between Aspire configuration and kernel runtime.
/// </summary>
public sealed record DigitalBrainModelDescriptor(
    DigitalBrainCapabilityKind Kind,
    string Provider,
    string Id,
    string DisplayName,
    DigitalBrainModelCapabilities Capabilities)
{
    /// <summary>
    /// Stable identifier for this provider/model pair, safe to use as a .NET keyed-service key
    /// (colons and dots — common in Ollama tags like "qwen2.5-coder:1.5b" — are normalized to hyphens).
    /// </summary>
    public string ServiceKey { get; } = Normalize($"{Provider}-{Id}");

    private static string Normalize(string value) =>
        value.Replace(':', '-').Replace('.', '-').ToLowerInvariant();
}
```

In `src/DigitalBrain.Aspire/LlmModels.cs`, add a `Capabilities` member to `DigitalBrainModel` and update `Describe()`:

```csharp
public abstract class DigitalBrainModel
{
    public abstract DigitalBrainCapabilityKind Kind { get; }
    public abstract string Provider { get; }
    public abstract string Id { get; }

    public virtual string DisplayName => Id;
    public virtual DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.FullyCapable;

    internal DigitalBrainModelDescriptor Describe() => new(Kind, Provider, Id, DisplayName, Capabilities);
}
```

Add an explicit `Capabilities` override to the two existing chat-only models (they're small local/legacy models, not tool-tuned):

```diff
 public sealed class Qwen25Coder1_5B : LlmModel
 {
     public override string Provider => DigitalBrainProviderIds.Ollama;
     public override string Id => "qwen2.5-coder:1.5b";
+    public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ChatOnly;
 }
```
(`Gpt4oMini`, `Whisper1Local`, `NomicEmbedText` keep the `FullyCapable` default — GPT-4o-mini genuinely supports tools; embedding/voice models don't have a meaningful "supports tools" axis, `FullyCapable` is just the harmless default there.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: PASS — all three new tests green, and `DigitalBrainModelRegistryTests.cs`'s existing tests still pass (they construct `DigitalBrainModelDescriptor` only indirectly via `.Describe()`, so the added constructor parameter doesn't break them — confirm this by reading that file's tests don't construct the record positionally anywhere before you run; if any test does construct it positionally, update those call sites to pass `Capabilities` too).

- [ ] **Step 5: Commit**

```bash
git add src/DigitalBrain.Core/Models/DigitalBrainModelCatalog.cs src/DigitalBrain.Aspire/LlmModels.cs tests/DigitalBrain.Tests/Aspire/DigitalBrainModelCapabilitiesTests.cs
git commit -m "feat: add DigitalBrainModelCapabilities and stable ServiceKey to the model registry"
```

---

## Task 2: Export `ServiceKey`/capabilities in the registry env dump

**Files:**
- Modify: `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` (`WithModelRegistry` private method)
- Test: extend `tests/DigitalBrain.Tests/Aspire/DigitalBrainModelRegistryTests.cs`

**Interfaces:**
- Consumes: Task 1's `DigitalBrainModelDescriptor.ServiceKey`/`.Capabilities`.

- [ ] **Step 1: Write the failing test**

Add to `tests/DigitalBrain.Tests/Aspire/DigitalBrainModelRegistryTests.cs` (this test targets `DigitalBrainModelRegistry`'s data, which is what `WithModelRegistry` reads from — there's no existing test harness in this file for the Aspire `IResourceBuilder<ProjectResource>.WithEnvironment` calls themselves, since that requires a real Aspire distributed-application builder; the registry snapshot content is the right level to test at, matching this file's existing style):

```csharp
[Fact]
public void RegistrationsCarryServiceKeyAndCapabilitiesReadyForEnvExport()
{
    var options = new DigitalBrainOptions();

    options.WithLLM<Qwen25Coder1_5B>().AsBalanced();

    var registration = Assert.Single(options.ModelRegistry.Registrations);
    Assert.Equal("ollama-qwen2-5-coder-1-5b", registration.Model.ServiceKey);
    Assert.False(registration.Model.Capabilities.SupportsTools);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: PASS already if Task 1 landed correctly (this test only exercises Task 1's already-shipped `ServiceKey`/`Capabilities` plumbing) — this step is really a confirmation, not a red step. The actual red/green cycle for this task is the env-var export itself, which isn't unit-testable without a live Aspire distributed-application builder (out of scope to stand one up here); proceed straight to Step 3 and verify by reading the diff + running the full suite to confirm no regression.

- [ ] **Step 3: Write minimal implementation**

In `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs`, extend `WithModelRegistry`'s per-registration loop:

```diff
         for (var i = 0; i < ctx.ModelRegistry.Registrations.Count; i++)
         {
             var registration = ctx.ModelRegistry.Registrations[i];
             var prefix = $"DigitalBrain__ModelRegistry__Registrations__{i}";
             kernel.WithEnvironment($"{prefix}__Kind", registration.Model.Kind.ToString());
             kernel.WithEnvironment($"{prefix}__Provider", registration.Model.Provider);
             kernel.WithEnvironment($"{prefix}__Id", registration.Model.Id);
             kernel.WithEnvironment($"{prefix}__DisplayName", registration.Model.DisplayName);
             kernel.WithEnvironment($"{prefix}__Role", registration.Role.ToString());
+            kernel.WithEnvironment($"{prefix}__ServiceKey", registration.Model.ServiceKey);
+            kernel.WithEnvironment($"{prefix}__SupportsTools", registration.Model.Capabilities.SupportsTools.ToString());
+            kernel.WithEnvironment($"{prefix}__SupportsVision", registration.Model.Capabilities.SupportsVision.ToString());
+            kernel.WithEnvironment($"{prefix}__SupportsStreaming", registration.Model.Capabilities.SupportsStreaming.ToString());
+            kernel.WithEnvironment($"{prefix}__SupportsStructuredOutput", registration.Model.Capabilities.SupportsStructuredOutput.ToString());
         }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: PASS — the new test plus the full existing suite, no regressions.

- [ ] **Step 5: Commit**

```bash
git add src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs tests/DigitalBrain.Tests/Aspire/DigitalBrainModelRegistryTests.cs
git commit -m "feat: export ServiceKey and capability flags for every model registration"
```

---

## Task 3: Generalized kernel-side registry reader (consolidates two duplicated ad hoc readers)

**Files:**
- Create: `src/DigitalBrain.Core/Models/DigitalBrainModelRegistrySnapshot.cs`
- Modify: `src/DigitalBrain.Kernel/Llm/DigitalBrainLlmRuntimeOptions.cs` (replace `FindRegisteredLlmModel` with the shared reader)
- Modify: `src/DigitalBrain.Kernel/Voice/VoiceTranscription.cs` (replace `FindRegisteredVoiceToText` with the shared reader)
- Test: `tests/DigitalBrain.Tests/Kernel/DigitalBrainModelRegistrySnapshotTests.cs`

**Why:** `DigitalBrainLlmRuntimeOptions.FindRegisteredLlmModel` and `DigitalBrainVoiceRuntimeOptions.FindRegisteredVoiceToText` are already near-identical: both walk `config.GetSection("DigitalBrain:ModelRegistry:Registrations").GetChildren())` filtering by `Kind`. Task 4/6 both need one more capability, "find the registration for kind X with capability Y (e.g. `SupportsTools`) or role Z" — adding that as a third copy-pasted reader would be the DRY violation Elon's-5-steps calls out; consolidating first is the "simplify" step.

**Interfaces:**
- Produces: `DigitalBrainModelRegistrySnapshot.Read(IConfiguration config) : IReadOnlyList<DigitalBrainModelDescriptor>` (each carrying `Kind`/`Provider`/`Id`/`DisplayName`/`ServiceKey`/`Capabilities`/read separately, `Role` alongside via a small paired record) and `DigitalBrainModelRegistrySnapshot.FirstOrDefault(IReadOnlyList<DigitalBrainRegistryEntry>, DigitalBrainCapabilityKind kind, Func<DigitalBrainRegistryEntry, bool>? predicate = null)`.

- [ ] **Step 1: Write the failing test**

```csharp
using DigitalBrain.Core.Models;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class DigitalBrainModelRegistrySnapshotTests
{
    [Fact]
    public void ReadsFullRegistrationsIncludingServiceKeyAndCapabilities()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "ollama",
            ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "qwen2.5-coder:1.5b",
            ["DigitalBrain:ModelRegistry:Registrations:0:DisplayName"] = "Qwen 2.5 Coder 1.5B",
            ["DigitalBrain:ModelRegistry:Registrations:0:Role"] = "Balanced",
            ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "ollama-qwen2-5-coder-1-5b",
            ["DigitalBrain:ModelRegistry:Registrations:0:SupportsTools"] = "false",
            ["DigitalBrain:ModelRegistry:Registrations:1:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:1:Provider"] = "ollama",
            ["DigitalBrain:ModelRegistry:Registrations:1:Id"] = "llama3.1:8b",
            ["DigitalBrain:ModelRegistry:Registrations:1:DisplayName"] = "Llama 3.1 8B",
            ["DigitalBrain:ModelRegistry:Registrations:1:Role"] = "Reasoning",
            ["DigitalBrain:ModelRegistry:Registrations:1:ServiceKey"] = "ollama-llama3-1-8b",
            ["DigitalBrain:ModelRegistry:Registrations:1:SupportsTools"] = "true",
        });

        var entries = DigitalBrainModelRegistrySnapshot.Read(config);

        Assert.Equal(2, entries.Count);
        var toolCapable = DigitalBrainModelRegistrySnapshot.FirstOrDefault(
            entries, DigitalBrainCapabilityKind.LargeLanguageModel, e => e.Capabilities.SupportsTools);
        Assert.NotNull(toolCapable);
        Assert.Equal("ollama-llama3-1-8b", toolCapable!.ServiceKey);
    }

    [Fact]
    public void FirstOrDefaultReturnsNullWhenNoRegistrationMatches()
    {
        var entries = DigitalBrainModelRegistrySnapshot.Read(BuildConfig(new Dictionary<string, string?>()));

        var result = DigitalBrainModelRegistrySnapshot.FirstOrDefault(entries, DigitalBrainCapabilityKind.VoiceToText);

        Assert.Null(result);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: FAIL to build — `DigitalBrainModelRegistrySnapshot` does not exist yet.

- [ ] **Step 3: Write minimal implementation**

`src/DigitalBrain.Core/Models/DigitalBrainModelRegistrySnapshot.cs`:

```csharp
namespace DigitalBrain.Core.Models;

using Microsoft.Extensions.Configuration;

/// <summary>
/// One provider/model registration as read back from the "DigitalBrain:ModelRegistry:Registrations"
/// config section the Aspire host exports (see DigitalBrainBuilderExtensions.WithModelRegistry).
/// </summary>
public sealed record DigitalBrainRegistryEntry(
    DigitalBrainCapabilityKind Kind,
    string Provider,
    string Id,
    string DisplayName,
    DigitalBrainModelRole Role,
    string ServiceKey,
    DigitalBrainModelCapabilities Capabilities);

/// <summary>
/// Reads the full indexed model registry the Aspire host exports into kernel configuration. Both LLM and
/// voice-to-text runtime options previously duplicated this exact "walk the indexed config section" logic
/// for their own single narrow purpose (finding one specific provider's model id) — this is the shared,
/// general-purpose reader both now use, extended with ServiceKey/Capabilities filtering.
/// </summary>
public static class DigitalBrainModelRegistrySnapshot
{
    public static IReadOnlyList<DigitalBrainRegistryEntry> Read(IConfiguration config)
    {
        var entries = new List<DigitalBrainRegistryEntry>();
        foreach (var child in config.GetSection("DigitalBrain:ModelRegistry:Registrations").GetChildren())
        {
            if (!Enum.TryParse<DigitalBrainCapabilityKind>(child["Kind"], out var kind))
            {
                continue;
            }

            _ = Enum.TryParse<DigitalBrainModelRole>(child["Role"], out var role);

            entries.Add(new DigitalBrainRegistryEntry(
                kind,
                child["Provider"] ?? string.Empty,
                child["Id"] ?? string.Empty,
                child["DisplayName"] ?? string.Empty,
                role,
                child["ServiceKey"] ?? string.Empty,
                new DigitalBrainModelCapabilities(
                    ParseBool(child["SupportsTools"]),
                    ParseBool(child["SupportsVision"]),
                    ParseBool(child["SupportsStreaming"]),
                    ParseBool(child["SupportsStructuredOutput"]))));
        }

        return entries;
    }

    public static DigitalBrainRegistryEntry? FirstOrDefault(
        IReadOnlyList<DigitalBrainRegistryEntry> entries,
        DigitalBrainCapabilityKind kind,
        Func<DigitalBrainRegistryEntry, bool>? predicate = null)
    {
        foreach (var entry in entries)
        {
            if (entry.Kind == kind && (predicate is null || predicate(entry)))
            {
                return entry;
            }
        }

        return null;
    }

    private static bool ParseBool(string? value) => bool.TryParse(value, out var parsed) && parsed;
}
```

In `src/DigitalBrain.Kernel/Llm/DigitalBrainLlmRuntimeOptions.cs`, replace `FindRegisteredLlmModel` with a call into the shared reader:

```diff
     private static string? FindRegisteredLlmModel(IConfiguration config, string provider)
     {
-        foreach (var child in config.GetSection("DigitalBrain:ModelRegistry:Registrations").GetChildren())
-        {
-            var kind = child["Kind"];
-            var registrationProvider = child["Provider"];
-            if (!string.Equals(kind, DigitalBrainCapabilityKind.LargeLanguageModel.ToString(), StringComparison.OrdinalIgnoreCase) ||
-                !string.Equals(registrationProvider, provider, StringComparison.OrdinalIgnoreCase))
-            {
-                continue;
-            }
-
-            var model = child["Id"];
-            if (!string.IsNullOrWhiteSpace(model))
-            {
-                return model;
-            }
-        }
-
-        return null;
+        var entries = DigitalBrainModelRegistrySnapshot.Read(config);
+        var match = DigitalBrainModelRegistrySnapshot.FirstOrDefault(
+            entries,
+            DigitalBrainCapabilityKind.LargeLanguageModel,
+            entry => string.Equals(entry.Provider, provider, StringComparison.OrdinalIgnoreCase));
+        return string.IsNullOrWhiteSpace(match?.Id) ? null : match.Id;
     }
```

In `src/DigitalBrain.Kernel/Voice/VoiceTranscription.cs`, replace `FindRegisteredVoiceToText` similarly:

```diff
     private static (string? Provider, string? Model) FindRegisteredVoiceToText(IConfiguration config)
     {
-        foreach (var child in config.GetSection("DigitalBrain:ModelRegistry:Registrations").GetChildren())
-        {
-            var kind = child["Kind"];
-            if (!string.Equals(kind, DigitalBrainCapabilityKind.VoiceToText.ToString(), StringComparison.OrdinalIgnoreCase))
-            {
-                continue;
-            }
-
-            return (child["Provider"], child["Id"]);
-        }
-
-        return (null, null);
+        var entries = DigitalBrainModelRegistrySnapshot.Read(config);
+        var match = DigitalBrainModelRegistrySnapshot.FirstOrDefault(entries, DigitalBrainCapabilityKind.VoiceToText);
+        return (match?.Provider, match?.Id);
     }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: PASS — both new tests green, and every existing `DigitalBrainLlmRuntimeOptions`/`DigitalBrainVoiceRuntimeOptions`/`ScopedChatClientFactory` test still passes (the two replaced private methods keep the exact same observable behavior — same lookup semantics, just delegated to the shared reader).

- [ ] **Step 5: Commit**

```bash
git add src/DigitalBrain.Core/Models/DigitalBrainModelRegistrySnapshot.cs src/DigitalBrain.Kernel/Llm/DigitalBrainLlmRuntimeOptions.cs src/DigitalBrain.Kernel/Voice/VoiceTranscription.cs tests/DigitalBrain.Tests/Kernel/DigitalBrainModelRegistrySnapshotTests.cs
git commit -m "refactor: consolidate the two duplicated model-registry config readers into one shared, capability-aware reader"
```

---

## Task 4: Keyed `IChatClient` per registered LLM model

**Files:**
- Modify: `src/DigitalBrain.Kernel/Llm/ScopedChatClientFactory.cs` (extract a provider-agnostic-by-id builder)
- Create: `src/DigitalBrain.Kernel/Llm/DigitalBrainChatClientRegistration.cs`
- Test: `tests/DigitalBrain.Tests/Kernel/DigitalBrainChatClientRegistrationTests.cs`

**Interfaces:**
- Consumes: Task 3's `DigitalBrainModelRegistrySnapshot.Read`.
- Produces: `DigitalBrainChatClientRegistration.AddDigitalBrainChatClients(this IServiceCollection services, IConfiguration config) : IServiceCollection` — registers one `AddKeyedSingleton<IChatClient>(entry.ServiceKey, ...)` per LLM registration found in the full registry snapshot.

- [ ] **Step 1: Write the failing test**

```csharp
using DigitalBrain.Core.Models;
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class DigitalBrainChatClientRegistrationTests
{
    [Fact]
    public void RegistersOneKeyedChatClientPerLlmRegistration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "ollama",
            ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "qwen2.5-coder:1.5b",
            ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "ollama-qwen2-5-coder-1-5b",
            ["DigitalBrain:ModelRegistry:Registrations:0:Role"] = "Fast",
            ["DigitalBrain:ModelRegistry:Registrations:1:Kind"] = "LargeLanguageModel",
            ["DigitalBrain:ModelRegistry:Registrations:1:Provider"] = "ollama",
            ["DigitalBrain:ModelRegistry:Registrations:1:Id"] = "llama3.1:8b",
            ["DigitalBrain:ModelRegistry:Registrations:1:ServiceKey"] = "ollama-llama3-1-8b",
            ["DigitalBrain:ModelRegistry:Registrations:1:Role"] = "Reasoning",
            ["DigitalBrain:ModelRegistry:Registrations:1:SupportsTools"] = "true",
            ["DigitalBrain:Llm:OllamaEndpoint"] = "http://localhost:11434",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddDigitalBrainChatClients(config);
        var provider = services.BuildServiceProvider();

        var fast = provider.GetKeyedService<IChatClient>("ollama-qwen2-5-coder-1-5b");
        var reasoning = provider.GetKeyedService<IChatClient>("ollama-llama3-1-8b");

        Assert.NotNull(fast);
        Assert.NotNull(reasoning);
        Assert.NotSame(fast, reasoning);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: FAIL to build — `AddDigitalBrainChatClients` does not exist yet.

- [ ] **Step 3: Write minimal implementation**

In `src/DigitalBrain.Kernel/Llm/ScopedChatClientFactory.cs`, extract the provider-branching-by-explicit-model-id logic into a small internal static builder both the existing per-scope factory and the new keyed-registration path can share:

```diff
 // Builds per-scope chat clients. Ollama mirrors DigitalBrainChat (endpoint/model from kernel config);
 // OpenAI is constructed from the caller-supplied key. The key is never logged.
 public sealed class ScopedChatClientFactory(IConfiguration config, ILogger<ScopedChatClientFactory> logger) : IScopedChatClientFactory
 {
     public IChatClient? Create(string provider, string? apiKey)
     {
         var options = DigitalBrainLlmRuntimeOptions.FromConfiguration(config);

         if (string.Equals(provider, DigitalBrainProviderIds.OpenAI, StringComparison.OrdinalIgnoreCase))
         {
             if (string.IsNullOrWhiteSpace(apiKey))
             {
                 logger.LogWarning("openai provider requested but no API key is configured — falling back to global client.");
                 return null;
             }

-            var openAiClient = new OpenAI.Chat.ChatClient(options.OpenAIModel, apiKey).AsIChatClient();
-            return new ChatClientBuilder(openAiClient).UseOpenTelemetry(sourceName: "DigitalBrain.Neuron").Build();
+            return DigitalBrainChatClients.BuildOpenAi(options.OpenAIModel, apiKey);
         }

-        // Default / "ollama": mirror DigitalBrainChat's Ollama wiring.
-        var ollamaClient = new OllamaSharp.OllamaApiClient(new Uri(options.OllamaEndpoint), options.Model);
-        return new ChatClientBuilder(ollamaClient).UseOpenTelemetry(sourceName: "DigitalBrain.Neuron").Build();
+        // Default / "ollama": mirror DigitalBrainChat's Ollama wiring.
+        return DigitalBrainChatClients.BuildOllama(options.OllamaEndpoint, options.Model);
     }
 }
+
+/// <summary>
+/// Shared, provider-id-driven IChatClient construction, used by both the per-request scoped factory above
+/// and the startup-time keyed registration in DigitalBrainChatClientRegistration.
+/// </summary>
+internal static class DigitalBrainChatClients
+{
+    public static IChatClient BuildOllama(string endpoint, string model) =>
+        new ChatClientBuilder(new OllamaSharp.OllamaApiClient(new Uri(endpoint), model))
+            .UseOpenTelemetry(sourceName: "DigitalBrain.Neuron")
+            .Build();
+
+    public static IChatClient BuildOpenAi(string model, string apiKey) =>
+        new ChatClientBuilder(new OpenAI.Chat.ChatClient(model, apiKey).AsIChatClient())
+            .UseOpenTelemetry(sourceName: "DigitalBrain.Neuron")
+            .Build();
+}
```

`src/DigitalBrain.Kernel/Llm/DigitalBrainChatClientRegistration.cs`:

```csharp
namespace DigitalBrain.Kernel.Llm;

using DigitalBrain.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers one keyed IChatClient per LLM model the Aspire host declared (see
/// DigitalBrainBuilderExtensions.WithModelRegistry), so grains can resolve a *specific* registered model's
/// client via GetRequiredKeyedService (directly, or through the [Llm&lt;TModel&gt;] facet in
/// DigitalBrainLlmAttribute.cs) instead of only ever getting the single flat unkeyed default.
/// </summary>
public static class DigitalBrainChatClientRegistration
{
    public static IServiceCollection AddDigitalBrainChatClients(this IServiceCollection services, IConfiguration config)
    {
        var runtimeOptions = DigitalBrainLlmRuntimeOptions.FromConfiguration(config);
        var entries = DigitalBrainModelRegistrySnapshot.Read(config);

        foreach (var entry in entries)
        {
            if (entry.Kind != DigitalBrainCapabilityKind.LargeLanguageModel || string.IsNullOrWhiteSpace(entry.ServiceKey))
            {
                continue;
            }

            services.AddKeyedSingleton<IChatClient>(entry.ServiceKey, (_, _) =>
                string.Equals(entry.Provider, DigitalBrainProviderIds.AzureOpenAI, StringComparison.OrdinalIgnoreCase)
                    ? BuildAzureOpenAi(runtimeOptions, entry.Id)
                    : DigitalBrainChatClients.BuildOllama(runtimeOptions.OllamaEndpoint, entry.Id));
        }

        return services;
    }

    private static IChatClient BuildAzureOpenAi(DigitalBrainLlmRuntimeOptions options, string deploymentId)
    {
        if (string.IsNullOrWhiteSpace(options.AzureOpenAIEndpoint))
        {
            throw new InvalidOperationException(
                $"Registered azureopenai model '{deploymentId}' has no DigitalBrain:Llm:AzureOpenAIEndpoint configured.");
        }

        var azureClient = string.IsNullOrWhiteSpace(options.AzureOpenAIKey)
            ? new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(options.AzureOpenAIEndpoint), new Azure.Identity.DefaultAzureCredential())
            : new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(options.AzureOpenAIEndpoint), new System.ClientModel.ApiKeyCredential(options.AzureOpenAIKey));

        return new ChatClientBuilder(azureClient.GetChatClient(deploymentId).AsIChatClient())
            .UseOpenTelemetry(sourceName: "DigitalBrain.Neuron")
            .Build();
    }
}
```

(This mirrors the existing Azure OpenAI construction already present in `DigitalBrainChat.cs` — confirm the exact `DefaultAzureCredential`/`ApiKeyCredential` fallback shape used there before finalizing, since this task deliberately does not touch `DigitalBrainChat.cs` itself, only adds the parallel keyed-registration path.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: PASS — the new test green, no regressions in `ScopedChatClientFactory`'s existing tests (the refactor preserves exact behavior, just extracts shared construction helpers).

- [ ] **Step 5: Commit**

```bash
git add src/DigitalBrain.Kernel/Llm/ScopedChatClientFactory.cs src/DigitalBrain.Kernel/Llm/DigitalBrainChatClientRegistration.cs tests/DigitalBrain.Tests/Kernel/DigitalBrainChatClientRegistrationTests.cs
git commit -m "feat: register one keyed IChatClient per declared LLM model"
```

---

## Task 5: The `[Llm<TModel>] IChatClient` Orleans facet attribute

**Files:**
- Create: `src/DigitalBrain.Kernel.Abstractions/LlmAttribute.cs`
- Modify: `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs` (wire `AddDigitalBrainChatClients` + register attribute mappers for every declared model type)
- Test: `tests/DigitalBrain.Tests/Kernel/LlmAttributeTests.cs`

**Interfaces:**
- Produces: `LlmAttribute<TModel> : Attribute, IFacetMetadata`, `LlmAttributeMapper<TModel> : IAttributeToFactoryMapper<LlmAttribute<TModel>>`. Verified against real Orleans source (`src/Orleans.Runtime/Facet/IFacetMetadata.cs`, `IAttributeToFactoryMapper.cs`, `GrainConstructorArgumentFactory.cs`, and `PersistentStateAttribute`/`PersistentStateAttributeMapper` as the reference example Orleans itself ships): `GrainConstructorArgumentFactory` resolves `services.GetService<IAttributeToFactoryMapper<TMetadata>>()` where `TMetadata` is the **closed** generic attribute type (e.g. `LlmAttribute<Llama31_8B>`), so one mapper registration is needed per concrete model type — done reflectively.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Reflection;
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class LlmAttributeTests
{
    [Fact]
    public async Task MapperResolvesTheKeyedChatClientForTheAttributesModelType()
    {
        var services = new ServiceCollection();
        var expectedClient = new FakeChatClient();
        services.AddKeyedSingleton<IChatClient>(TestModel.ExpectedServiceKey, expectedClient);
        var provider = services.BuildServiceProvider();

        var mapper = new LlmAttributeMapper<TestModel>();
        var parameter = typeof(FakeGrain).GetConstructors()[0].GetParameters()[0];
        var factory = mapper.GetFactory(parameter, new LlmAttribute<TestModel>());

        var context = new FakeGrainContext(provider);
        var resolved = factory(context);

        Assert.Same(expectedClient, resolved);
    }

    [Fact]
    public void ThrowsAClearErrorWhenAppliedToTheWrongParameterType()
    {
        var mapper = new LlmAttributeMapper<TestModel>();
        var parameter = typeof(FakeGrainWithWrongParameterType).GetConstructors()[0].GetParameters()[0];

        var ex = Assert.Throws<ArgumentException>(() => mapper.GetFactory(parameter, new LlmAttribute<TestModel>()));

        Assert.Contains("IChatClient", ex.Message);
    }

    private sealed class TestModel
    {
        public const string ExpectedServiceKey = "test-provider-test-model";
    }

    private sealed class FakeChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeGrain(IChatClient chatClient) { }
    private sealed class FakeGrainWithWrongParameterType(string notAChatClient) { }

    private sealed class FakeGrainContext(IServiceProvider services) : IGrainContext
    {
        public IServiceProvider ActivationServices => services;
        public GrainReference GrainReference => throw new NotSupportedException();
        public GrainId GrainId => default;
        public object? GrainInstance => null;
        public ActivationId ActivationId => default;
        public GrainAddress Address => throw new NotSupportedException();
        public IServiceProvider ActivationCancellationTokenSource => throw new NotSupportedException();
        // Remaining IGrainContext members are unused by this test — implement the minimal subset your
        // installed Orleans version's IGrainContext interface actually requires and throw NotSupportedException
        // for the rest; check the interface's current member list before finalizing this fake.
    }
}
```

**Note for the implementer:** `IGrainContext` is a fairly large interface with several members; the test above sketches only what's directly needed (`ActivationServices`). Read the actual installed `Orleans.Runtime.IGrainContext` interface first and implement a minimal fake satisfying it (or check whether Orleans' own test utilities expose a lightweight fake grain context already — `Orleans.TestingHost` or `Orleans.Runtime.Internal` may have one; prefer reusing an existing one over hand-rolling if it exists) rather than guessing at the full member list here.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: FAIL to build — `LlmAttribute<T>`/`LlmAttributeMapper<T>` do not exist yet.

- [ ] **Step 3: Write minimal implementation**

`src/DigitalBrain.Kernel.Abstractions/LlmAttribute.cs`:

```csharp
namespace DigitalBrain.Kernel.Llm;

using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

// Grain constructors declare [Llm<SomeModel>] IChatClient chatClient to get the keyed client Task 4
// registered for that exact model — plugging into Orleans' own constructor-facet extensibility point
// (the same one [PersistentState(...)] uses), not a bespoke DI convention. Verified against Orleans'
// real IFacetMetadata/IAttributeToFactoryMapper/GrainConstructorArgumentFactory source.
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class LlmAttribute<TModel> : Attribute, IFacetMetadata
{
}

public sealed class LlmAttributeMapper<TModel> : IAttributeToFactoryMapper<LlmAttribute<TModel>>
{
    public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, LlmAttribute<TModel> metadata)
    {
        if (parameter.ParameterType != typeof(IChatClient))
        {
            throw new ArgumentException(
                $"Parameter '{parameter.Name}' on '{parameter.Member.DeclaringType}' must be of type IChatClient "
                + $"because it has an [Llm<{typeof(TModel).Name}>] attribute.",
                parameter.Name);
        }

        var serviceKey = LlmServiceKeys.For(typeof(TModel));
        return context => context.ActivationServices.GetRequiredKeyedService<IChatClient>(serviceKey);
    }
}

/// <summary>
/// Maps a model marker <see cref="Type"/> to the ServiceKey <see cref="DigitalBrainChatClientRegistration"/>
/// registered it under. Model marker types used with [Llm&lt;TModel&gt;] must expose a public const/static
/// string field or property named "ServiceKey" — reflected here so the attribute stays a zero-argument
/// generic (`[Llm&lt;Llama31_8B&gt;]`, not `[Llm&lt;Llama31_8B&gt;]("ollama-llama3-1-8b")`).
/// </summary>
internal static class LlmServiceKeys
{
    public static string For(Type modelType)
    {
        var member = modelType.GetProperty("ServiceKey", BindingFlags.Public | BindingFlags.Static)
            ?? (MemberInfo?)modelType.GetField("ServiceKey", BindingFlags.Public | BindingFlags.Static);
        if (member is null)
        {
            throw new InvalidOperationException(
                $"Type '{modelType.Name}' used with [Llm<{modelType.Name}>] has no public static ServiceKey member.");
        }

        var value = member switch
        {
            PropertyInfo property => property.GetValue(null) as string,
            FieldInfo field => field.GetValue(null) as string,
            _ => null
        };

        return value ?? throw new InvalidOperationException($"Type '{modelType.Name}'.ServiceKey returned null.");
    }
}
```

**Note for the implementer:** the test's `TestModel.ExpectedServiceKey` is a `const string`, which reflection reads via `GetField`, not `GetProperty` — confirm `LlmServiceKeys.For` handles both a `const string ServiceKey` field and a computed `static string ServiceKey => ...` property, since Task 8's real model classes will likely want the latter (computed from `Provider`/`Id` via the same normalization `DigitalBrainModelDescriptor.ServiceKey` already uses in Task 1 — consider having `LlmModel` itself expose a static-friendly way to compute this consistently, or simply having each concrete model class declare `public static string ServiceKey => "ollama-llama3-1-8b";` as a literal matching what it registers under, whichever is less error-prone; use your judgment here and note the choice in your report).

In `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs`, wire the keyed-client registration into the silo builder (near wherever `IScopedChatClientFactory`/other kernel services are registered — read the current file to find the right spot rather than assuming a line number) and register attribute mappers for whichever model types are actually declared:

```csharp
builder.Services.AddDigitalBrainChatClients(builder.Configuration);
```

Attribute-mapper registration needs the set of concrete model *types* actually in use — since this repo doesn't have IAW's assembly-wide `LLMModel.All` auto-discovery (Task 1 deliberately did not add that; DigitalBrain's registry is an explicit registration list, not a scan), register mappers reflectively for whatever model marker types exist as `DigitalBrainModel`-derived classes in `DigitalBrain.Aspire`'s assembly (the same assembly Task 8's new provider model classes will live in):

```csharp
foreach (var modelType in typeof(DigitalBrain.Aspire.LlmModel).Assembly.GetTypes()
    .Where(t => typeof(DigitalBrain.Aspire.LlmModel).IsAssignableFrom(t) && !t.IsAbstract))
{
    var mapperInterface = typeof(IAttributeToFactoryMapper<>).MakeGenericType(
        typeof(DigitalBrain.Kernel.Llm.LlmAttribute<>).MakeGenericType(modelType));
    var mapperImpl = typeof(DigitalBrain.Kernel.Llm.LlmAttributeMapper<>).MakeGenericType(modelType);
    builder.Services.AddSingleton(mapperInterface, mapperImpl);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: PASS — both new tests green.

- [ ] **Step 5: Commit**

```bash
git add src/DigitalBrain.Kernel.Abstractions/LlmAttribute.cs src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs tests/DigitalBrain.Tests/Kernel/LlmAttributeTests.cs
git commit -m "feat: add [Llm<TModel>] IChatClient Orleans facet attribute"
```

---

## Task 6: Mirror for voice — `[Voice2Text<TModel>] IVoiceTranscriber`

**Files:**
- Create: `src/DigitalBrain.Kernel.Abstractions/Voice2TextAttribute.cs`
- Modify: `src/DigitalBrain.Kernel/Voice/VoiceTranscription.cs` (keyed `IVoiceTranscriber` registration per declared voice model, symmetric to Task 4)
- Modify: `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs` (register `Voice2TextAttribute<T>` mappers alongside `LlmAttribute<T>`'s)
- Test: `tests/DigitalBrain.Tests/Kernel/Voice2TextAttributeTests.cs`

**Why this task exists as its own step (explicitly requested):** Task 4/5 give the LLM side keyed-DI + the attribute ergonomic; without this task, voice-to-text would stay a second-class citizen with only the flat `DefaultVoiceToText` resolution. This mirrors the exact same mechanism for symmetry.

**Interfaces:**
- Produces: `Voice2TextAttribute<TModel> : Attribute, IFacetMetadata`, `Voice2TextAttributeMapper<TModel> : IAttributeToFactoryMapper<Voice2TextAttribute<TModel>>` resolving a keyed `IVoiceTranscriber`; `AddDigitalBrainVoiceTranscription` (existing, in `VoiceTranscription.cs`) gains a keyed registration per declared voice-to-text model, alongside its existing unkeyed default.

- [ ] **Step 1: Write the failing test**

```csharp
using DigitalBrain.Kernel.Voice;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class Voice2TextAttributeTests
{
    [Fact]
    public async Task MapperResolvesTheKeyedTranscriberForTheAttributesModelType()
    {
        var services = new ServiceCollection();
        var expected = new FakeTranscriber();
        services.AddKeyedSingleton<IVoiceTranscriber>(TestVoiceModel.ExpectedServiceKey, expected);
        var provider = services.BuildServiceProvider();

        var mapper = new Voice2TextAttributeMapper<TestVoiceModel>();
        var parameter = typeof(FakeVoiceGrain).GetConstructors()[0].GetParameters()[0];
        var factory = mapper.GetFactory(parameter, new Voice2TextAttribute<TestVoiceModel>());

        var resolved = factory(new FakeGrainContext(provider));

        Assert.Same(expected, resolved);
    }

    private sealed class TestVoiceModel
    {
        public const string ExpectedServiceKey = "openai-compatible-whisper-test";
    }

    private sealed class FakeTranscriber : IVoiceTranscriber
    {
        public Task<VoiceTranscriptionResult> TranscribeAsync(VoiceTranscriptionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeVoiceGrain(IVoiceTranscriber transcriber) { }
}
```

(Reuse the `FakeGrainContext` from Task 5's test file — move it to a small shared internal test-support file, e.g. `tests/DigitalBrain.Tests/Kernel/FakeGrainContext.cs`, if it doesn't already have a home; check for duplication before adding a second copy.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: FAIL to build — `Voice2TextAttribute<T>`/`Voice2TextAttributeMapper<T>` do not exist yet.

- [ ] **Step 3: Write minimal implementation**

`src/DigitalBrain.Kernel.Abstractions/Voice2TextAttribute.cs` — mirrors `LlmAttribute.cs` exactly, substituting `IVoiceTranscriber` for `IChatClient`:

```csharp
namespace DigitalBrain.Kernel.Voice;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class Voice2TextAttribute<TModel> : Attribute, IFacetMetadata
{
}

public sealed class Voice2TextAttributeMapper<TModel> : IAttributeToFactoryMapper<Voice2TextAttribute<TModel>>
{
    public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, Voice2TextAttribute<TModel> metadata)
    {
        if (parameter.ParameterType != typeof(IVoiceTranscriber))
        {
            throw new ArgumentException(
                $"Parameter '{parameter.Name}' on '{parameter.Member.DeclaringType}' must be of type IVoiceTranscriber "
                + $"because it has a [Voice2Text<{typeof(TModel).Name}>] attribute.",
                parameter.Name);
        }

        var serviceKey = DigitalBrain.Kernel.Llm.LlmServiceKeys.For(typeof(TModel));
        return context => context.ActivationServices.GetRequiredKeyedService<IVoiceTranscriber>(serviceKey);
    }
}
```

(`LlmServiceKeys.For` from Task 5 is provider-agnostic reflection over a `ServiceKey` member — reused as-is, not duplicated; if this cross-namespace reuse feels awkward, move `LlmServiceKeys` to a shared, more neutrally-named home, e.g. `DigitalBrain.Kernel.Llm.DigitalBrainServiceKeys`, and update Task 5's reference too — use your judgment on the cleanest home, note the choice in your report.)

In `src/DigitalBrain.Kernel/Voice/VoiceTranscription.cs`, extend `AddDigitalBrainVoiceTranscription` to add keyed registrations alongside the existing unkeyed default:

```diff
     public static IServiceCollection AddDigitalBrainVoiceTranscription(
         this IServiceCollection services,
         IConfiguration config)
     {
         services.AddSingleton(DigitalBrainVoiceRuntimeOptions.FromConfiguration(config));
         services.TryAddSingleton<HttpClient>();
         services.TryAddSingleton<NoOpVoiceTranscriber>();
         services.TryAddSingleton<OpenAICompatibleVoiceTranscriber>();
         services.TryAddSingleton<IVoiceTranscriber>(sp =>
         {
             var options = sp.GetRequiredService<DigitalBrainVoiceRuntimeOptions>();
             return OpenAICompatibleVoiceTranscriber.CanHandle(options)
                 ? sp.GetRequiredService<OpenAICompatibleVoiceTranscriber>()
                 : sp.GetRequiredService<NoOpVoiceTranscriber>();
         });
+
+        var entries = DigitalBrainModelRegistrySnapshot.Read(config);
+        foreach (var entry in entries)
+        {
+            if (entry.Kind != DigitalBrainCapabilityKind.VoiceToText || string.IsNullOrWhiteSpace(entry.ServiceKey))
+            {
+                continue;
+            }
+
+            services.AddKeyedSingleton<IVoiceTranscriber>(entry.ServiceKey, (sp, _) =>
+                sp.GetRequiredService<IVoiceTranscriber>());
+        }
+
         return services;
     }
```

In `DigitalBrainOrleansExtensions.cs`, alongside Task 5's `LlmAttribute<>` reflective mapper registration, add the `Voice2TextAttribute<>` equivalent looping over `VoiceToTextModel`-derived types the same way.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: PASS — the new test green, existing voice tests (`VoiceTranscriptionTests.cs`) unaffected.

- [ ] **Step 5: Commit**

```bash
git add src/DigitalBrain.Kernel.Abstractions/Voice2TextAttribute.cs src/DigitalBrain.Kernel/Voice/VoiceTranscription.cs src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs tests/DigitalBrain.Tests/Kernel/Voice2TextAttributeTests.cs
git commit -m "feat: mirror the Llm<T> facet attribute for voice-to-text — [Voice2Text<TModel>] IVoiceTranscriber"
```

---

## Task 7: Real Anthropic provider

**Files:**
- Modify: `Directory.Packages.props` (add `Anthropic` package — official `anthropics/anthropic-sdk-csharp`, has first-party `AsIChatClient()`, verified via Context7 against real SDK source, not the alternative community `Anthropic.SDK` package)
- Create: `src/DigitalBrain.Aspire/Models/Anthropic/` — `Claude45Haiku.cs`, `Claude47Sonnet.cs` (fast/balanced-reasoning pair; verify current real Anthropic model ids before finalizing — do not guess)
- Modify: `src/DigitalBrain.Kernel/Llm/DigitalBrainChatClientRegistration.cs` (add the anthropic branch)
- Modify: `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` (`WithLLM<T>()`'s provider-specific side effects: lazily add an `anthropic-api-key` secret parameter when an Anthropic model is registered, mirroring the existing `azureopenai` endpoint/key parameter pattern)
- Test: extend `tests/DigitalBrain.Tests/Kernel/DigitalBrainChatClientRegistrationTests.cs`

**Interfaces:**
- Produces: `DigitalBrain.Aspire.Models.Anthropic.Claude45Haiku : LlmModel` (`Provider = "anthropic"`, `Capabilities = ToolCapable`), similarly for a balanced/reasoning-tier model.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void RegistersAnthropicChatClientWhenApiKeyIsConfigured()
{
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
        ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "anthropic",
        ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "claude-haiku-4-5",
        ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "anthropic-claude-haiku-4-5",
        ["DigitalBrain:ModelRegistry:Registrations:0:Role"] = "Fast",
        ["DigitalBrain:ModelRegistry:Registrations:0:SupportsTools"] = "true",
        ["DigitalBrain:Llm:AnthropicApiKey"] = "test-key",
    }).Build();

    var services = new ServiceCollection();
    services.AddDigitalBrainChatClients(config);
    var provider = services.BuildServiceProvider();

    var client = provider.GetKeyedService<IChatClient>("anthropic-claude-haiku-4-5");

    Assert.NotNull(client);
}

[Fact]
public void ThrowsAClearErrorWhenAnthropicModelIsRegisteredWithoutAnApiKey()
{
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
        ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "anthropic",
        ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "claude-haiku-4-5",
        ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "anthropic-claude-haiku-4-5",
    }).Build();

    var services = new ServiceCollection();
    services.AddDigitalBrainChatClients(config);
    var provider = services.BuildServiceProvider();

    var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredKeyedService<IChatClient>("anthropic-claude-haiku-4-5"));
    Assert.Contains("AnthropicApiKey", ex.Message);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: FAIL — no `anthropic` branch in `DigitalBrainChatClientRegistration` yet, `DigitalBrain:Llm:AnthropicApiKey` not read anywhere.

- [ ] **Step 3: Write minimal implementation**

Add the package: `dotnet add src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj package Anthropic` (no pinned version). Confirm after restore that `new AnthropicClient { ApiKey = ... }.AsIChatClient(modelId)` compiles as described (verified via Context7 against `anthropics/anthropic-sdk-csharp`'s real `AnthropicClientExtensions.AsIChatClient` source) — if the resolved package version's API differs, stop and re-check Context7 rather than guessing at an adapted shape.

In `DigitalBrainLlmRuntimeOptions.cs`, add an `AnthropicApiKey` field read from `DigitalBrain:Llm:AnthropicApiKey`, mirroring the existing `AzureOpenAIKey` field exactly.

In `DigitalBrainChatClientRegistration.cs`, add the anthropic branch:

```diff
             services.AddKeyedSingleton<IChatClient>(entry.ServiceKey, (_, _) =>
-                string.Equals(entry.Provider, DigitalBrainProviderIds.AzureOpenAI, StringComparison.OrdinalIgnoreCase)
-                    ? BuildAzureOpenAi(runtimeOptions, entry.Id)
-                    : DigitalBrainChatClients.BuildOllama(runtimeOptions.OllamaEndpoint, entry.Id));
+                entry.Provider switch
+                {
+                    var p when string.Equals(p, DigitalBrainProviderIds.AzureOpenAI, StringComparison.OrdinalIgnoreCase) => BuildAzureOpenAi(runtimeOptions, entry.Id),
+                    var p when string.Equals(p, DigitalBrainProviderIds.Anthropic, StringComparison.OrdinalIgnoreCase) => BuildAnthropic(runtimeOptions, entry.Id),
+                    _ => DigitalBrainChatClients.BuildOllama(runtimeOptions.OllamaEndpoint, entry.Id)
+                });
```

```csharp
    private static IChatClient BuildAnthropic(DigitalBrainLlmRuntimeOptions options, string modelId)
    {
        if (string.IsNullOrWhiteSpace(options.AnthropicApiKey))
        {
            throw new InvalidOperationException(
                $"Registered anthropic model '{modelId}' has no DigitalBrain:Llm:AnthropicApiKey configured.");
        }

        var client = new Anthropic.AnthropicClient { ApiKey = options.AnthropicApiKey };
        return new ChatClientBuilder(client.AsIChatClient(modelId))
            .UseOpenTelemetry(sourceName: "DigitalBrain.Neuron")
            .Build();
    }
```

`src/DigitalBrain.Aspire/Models/Anthropic/Claude45Haiku.cs`:

```csharp
namespace DigitalBrain.Aspire.Models.Anthropic;

using DigitalBrain.Core.Models;

/// <summary>Fast/cheap Anthropic tier. Verify "claude-haiku-4-5" is still the current fast-tier model id before relying on this.</summary>
public sealed class Claude45Haiku : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.Anthropic;
    public override string Id => "claude-haiku-4-5";
    public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ToolCapable;
}
```

In `DigitalBrainBuilderExtensions.cs`'s `AddDigitalBrain`, add the lazy `anthropic-api-key` secret parameter, mirroring the existing `azureopenai` block:

```diff
         IResourceBuilder<ParameterResource>? azureOpenAIEndpoint = null;
         IResourceBuilder<ParameterResource>? azureOpenAIKey = null;
         if (string.Equals(llmProvider, "azureopenai", StringComparison.OrdinalIgnoreCase))
         {
             azureOpenAIEndpoint = builder.AddParameter("azure-openai-endpoint");
             azureOpenAIKey = builder.AddParameter("azure-openai-key", secret: true);
         }
+
+        IResourceBuilder<ParameterResource>? anthropicApiKey = null;
+        if (options.ModelRegistry.Registrations.Any(r => string.Equals(r.Model.Provider, DigitalBrainProviderIds.Anthropic, StringComparison.OrdinalIgnoreCase)))
+        {
+            anthropicApiKey = builder.AddParameter("anthropic-api-key", secret: true);
+        }
```

(Thread `anthropicApiKey` through `DigitalBrainContext` and `WireKernelSilo` the same way `AzureOpenAIKey` already is — one new nullable property, one new conditional `kernel.WithEnvironment("DigitalBrain__Llm__AnthropicApiKey", ...)` call, matching the existing pattern exactly.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: PASS — both new tests green.

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props src/DigitalBrain.Aspire/Models/Anthropic/ src/DigitalBrain.Kernel/Llm/DigitalBrainChatClientRegistration.cs src/DigitalBrain.Kernel/Llm/DigitalBrainLlmRuntimeOptions.cs src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs tests/DigitalBrain.Tests/Kernel/DigitalBrainChatClientRegistrationTests.cs
git commit -m "feat: add a real Anthropic provider (official SDK, AsIChatClient)"
```

---

## Task 8: Real xAI provider (no dedicated SDK — reuse the OpenAI SDK against x.ai's endpoint)

**Files:**
- Create: `src/DigitalBrain.Aspire/Models/Xai/Grok41.cs`
- Modify: `src/DigitalBrain.Kernel/Llm/DigitalBrainChatClientRegistration.cs` (add the xai branch)
- Modify: `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` (lazy `xai-api-key` secret parameter, same pattern as Task 7's anthropic key)
- Test: extend `tests/DigitalBrain.Tests/Kernel/DigitalBrainChatClientRegistrationTests.cs`

**Why no new package:** xAI's Grok API is OpenAI-API-compatible; the one real, working xAI integration found in reference material builds it as `new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri("https://api.x.ai/v1") }).GetChatClient(modelId).AsIChatClient()` — no dedicated xAI SDK exists or is needed. `Directory.Packages.props` already has the `OpenAI` package (via `Microsoft.Extensions.AI.OpenAI`'s dependency) — confirm the raw `OpenAI` package itself (not just the `Microsoft.Extensions.AI.OpenAI` bridge) is directly referenced by `DigitalBrain.Kernel.csproj`; add a direct `PackageReference` if it currently only comes in transitively.

**Interfaces:**
- Produces: `DigitalBrain.Aspire.Models.Xai.Grok41 : LlmModel` (`Provider = "xai"`, `Capabilities = ToolCapable`).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void RegistersXaiChatClientWhenApiKeyIsConfigured()
{
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
        ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "xai",
        ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "grok-4-1",
        ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "xai-grok-4-1",
        ["DigitalBrain:Llm:XaiApiKey"] = "test-key",
    }).Build();

    var services = new ServiceCollection();
    services.AddDigitalBrainChatClients(config);
    var provider = services.BuildServiceProvider();

    Assert.NotNull(provider.GetKeyedService<IChatClient>("xai-grok-4-1"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: FAIL — no `xai` branch yet.

- [ ] **Step 3: Write minimal implementation**

Add `XaiApiKey` to `DigitalBrainLlmRuntimeOptions` (reads `DigitalBrain:Llm:XaiApiKey`, same pattern as `AnthropicApiKey`).

```csharp
    private static IChatClient BuildXai(DigitalBrainLlmRuntimeOptions options, string modelId)
    {
        if (string.IsNullOrWhiteSpace(options.XaiApiKey))
        {
            throw new InvalidOperationException(
                $"Registered xai model '{modelId}' has no DigitalBrain:Llm:XaiApiKey configured.");
        }

        var clientOptions = new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://api.x.ai/v1") };
        var openAiClient = new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(options.XaiApiKey), clientOptions);
        return new ChatClientBuilder(openAiClient.GetChatClient(modelId).AsIChatClient())
            .UseOpenTelemetry(sourceName: "DigitalBrain.Neuron")
            .Build();
    }
```

Add the `xai` switch arm alongside `anthropic`/`azureopenai` in the same `entry.Provider switch` from Task 7. `src/DigitalBrain.Aspire/Models/Xai/Grok41.cs`:

```csharp
namespace DigitalBrain.Aspire.Models.Xai;

using DigitalBrain.Core.Models;

/// <summary>Verify "grok-4-1" is still the current model id before relying on this.</summary>
public sealed class Grok41 : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.Xai;
    public override string Id => "grok-4-1";
    public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ToolCapable;
}
```

Add the lazy `xai-api-key` secret parameter in `DigitalBrainBuilderExtensions.cs`, mirroring Task 7's `anthropic-api-key` block.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DigitalBrain.Aspire/Models/Xai/ src/DigitalBrain.Kernel/Llm/DigitalBrainChatClientRegistration.cs src/DigitalBrain.Kernel/Llm/DigitalBrainLlmRuntimeOptions.cs src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs tests/DigitalBrain.Tests/Kernel/DigitalBrainChatClientRegistrationTests.cs
git commit -m "feat: add a real xAI provider (reuses the OpenAI SDK against x.ai's OpenAI-compatible endpoint)"
```

---

## Task 9: A genuinely tool-capable Ollama model, and wire Ino to actually use it

**Files:**
- Modify: `src/DigitalBrain.Aspire/LlmModels.cs` (add a tool-capable Ollama model)
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs` (register it, tagged Reasoning; fix the stale "README LLM provider switch" comment reference)
- Modify: `integrations/DigitalBrain.Ino/InoNeuron.cs` (`HandleGenericIntentAsync`: prefer the tool-capable registered model over the flat single default)
- Test: `tests/DigitalBrain.Tests/Ino/InoNeuronToolCapableModelResolutionTests.cs`

**This is the task that actually fixes the reported bug.** Everything before this is the framework; this is wiring Ino to use it.

**Interfaces:**
- Consumes: Task 3's `DigitalBrainModelRegistrySnapshot`, Task 4's keyed `IChatClient` registrations.

- [ ] **Step 1: Write the failing test**

```csharp
using DigitalBrain.Core;
using DigitalBrain.Ino;
using DigitalBrain.TestKit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Ino;

public sealed class InoNeuronToolCapableModelResolutionTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = "LargeLanguageModel",
                ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = "ollama",
                ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "qwen2.5-coder:1.5b",
                ["DigitalBrain:ModelRegistry:Registrations:0:ServiceKey"] = "ollama-qwen2-5-coder-1-5b",
                ["DigitalBrain:ModelRegistry:Registrations:0:Role"] = "Balanced",
                ["DigitalBrain:ModelRegistry:Registrations:0:SupportsTools"] = "false",
                ["DigitalBrain:ModelRegistry:Registrations:1:Kind"] = "LargeLanguageModel",
                ["DigitalBrain:ModelRegistry:Registrations:1:Provider"] = "ollama",
                ["DigitalBrain:ModelRegistry:Registrations:1:Id"] = "llama3.1:8b",
                ["DigitalBrain:ModelRegistry:Registrations:1:ServiceKey"] = "ollama-llama3-1-8b",
                ["DigitalBrain:ModelRegistry:Registrations:1:Role"] = "Reasoning",
                ["DigitalBrain:ModelRegistry:Registrations:1:SupportsTools"] = "true",
            }).Build();
            services.AddSingleton<IConfiguration>(config);
            services.AddDigitalBrainChatClients(config);
            // The "flat default" IChatClient is deliberately the chat-only model here, so this test can
            // prove Ino picks the tool-capable one instead of just grabbing whatever the unkeyed default is.
            services.AddKeyedSingleton<IChatClient>("ollama-qwen2-5-coder-1-5b", new RecordingChatClient("chat-only"));
            services.AddKeyedSingleton<IChatClient>("ollama-llama3-1-8b", new RecordingChatClient("tool-capable"));
        });

    [Fact]
    public async Task GenericIntentPathResolvesTheToolCapableRegisteredModelOverTheFlatDefault()
    {
        var ino = Grain<IInoNeuron>("ino-tool-capable");
        await ino.FireAsync(new InoRequest("tell me a joke", "session-tool-capable"));

        var response = (await ino.GetOutgoingTimelineAsync()).OfType<InoResponse>().Last();
        Assert.Contains("tool-capable", response.Response);
    }

    private sealed class RecordingChatClient(string tag) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, tag)));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
```

**Note for the implementer:** this test's exact shape depends on precisely how `HandleGenericIntentAsync` currently resolves its `IChatClient` after Tasks 6-10 of the *previous* plan (`ResolveGlobalLlmClientAsync() ?? ServiceProvider.GetService<IChatClient>()`) — read the current method body first. The fix should insert a new resolution step between those two: try the per-user pack-config override first (unchanged), then try resolving the tool-capable registered model via the registry snapshot, then fall back to the flat unkeyed default last. Adjust this test if the intent classifier's fast-path heuristic (documented in the prior plan's Task 6 review) intercepts "tell me a joke" differently than expected — use a prompt confirmed to reach the generic path if so.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: FAIL — `response.Response` currently contains "chat-only" (Ino still grabs the flat unkeyed default), not "tool-capable".

- [ ] **Step 3: Write minimal implementation**

In `src/DigitalBrain.Aspire/LlmModels.cs`, add:

```csharp
/// <summary>
/// A real tool-calling-capable local Ollama model, for roles (like Ino's generic tool-calling path) that
/// specifically need native function-calling support — unlike Qwen25Coder1_5B (a code-completion model
/// that does not reliably use native tool-calling; this was the root cause of Ino showing raw hallucinated
/// tool-call text to users instead of actually invoking tools). Verify Ollama's current model library still
/// tags llama3.1 as tool-capable before relying on this, and that your local Ollama has GPU/RAM headroom
/// for an 8B model beyond the 1.5B fallback.
/// </summary>
public sealed class Llama31_8B : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.Ollama;
    public override string Id => "llama3.1:8b";
    public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ToolCapable;
}
```

In `integrations/DigitalBrain.Ino/InoNeuron.cs`, add a private resolution helper and use it in `HandleGenericIntentAsync` before the existing fallback chain:

```csharp
    private async Task<IChatClient?> ResolveToolCapableChatClientAsync(CancellationToken cancellationToken)
    {
        var config = ServiceProvider.GetService<IConfiguration>();
        if (config is null)
        {
            return null;
        }

        var entries = DigitalBrainModelRegistrySnapshot.Read(config);
        var toolCapable = DigitalBrainModelRegistrySnapshot.FirstOrDefault(
            entries, DigitalBrainCapabilityKind.LargeLanguageModel, e => e.Capabilities.SupportsTools);
        if (toolCapable is null || string.IsNullOrWhiteSpace(toolCapable.ServiceKey))
        {
            return null;
        }

        return ServiceProvider.GetKeyedService<IChatClient>(toolCapable.ServiceKey);
    }
```

```diff
-        var chat = await ResolveGlobalLlmClientAsync(cancellationToken) ?? ServiceProvider.GetService<IChatClient>();
+        var chat = await ResolveGlobalLlmClientAsync(cancellationToken)
+            ?? await ResolveToolCapableChatClientAsync(cancellationToken)
+            ?? ServiceProvider.GetService<IChatClient>();
```

(Per-user pack-config overrides via `ResolveGlobalLlmClientAsync` still win first — a user who's explicitly picked a provider keeps that choice; the tool-capable registered model is the new, better-informed *default* when no override exists, replacing the previously-arbitrary flat unkeyed default.)

In `hosts/DigitalBrain.AppHost/AppHost.cs`, register the new model and fix the stale doc pointer:

```diff
 var ctx = builder.AddDigitalBrain("digitalbrain", options =>
 {
-    options.WithLLM<Qwen25Coder1_5B>();
+    options.WithLLM<Qwen25Coder1_5B>().AsFast();
+    options.WithLLM<Llama31_8B>().AsReasoning();
     options.WithEmbedding<NomicEmbedText>();
     // Local Whisper container is always present in run mode (see AddDigitalBrain), so this is safe to
     // register unconditionally — the kernel wiring extension's voice wiring falls back gracefully whether or not a real
     // endpoint ends up set (manual override > local Whisper container > unset).
     options.WithVoice2Text<Whisper1Local>();
-    // To switch to Azure OpenAI, call options.WithLLM<Gpt4oMini>() instead — it needs the
-    // azure-openai-endpoint/-key parameters wired below (see README "LLM provider switch").
+    // To switch to Azure OpenAI, Anthropic, or xAI instead of local Ollama, call
+    // options.WithLLM<Gpt4oMini>() / .WithLLM<Claude45Haiku>() / .WithLLM<Grok41>() — each needs its
+    // matching secret parameter (azure-openai-endpoint/-key, anthropic-api-key, xai-api-key respectively),
+    // wired automatically by AddDigitalBrain when that provider is registered.
 });
```

(Registering `Qwen25Coder1_5B` as Fast and `Llama31_8B` as Reasoning means `DefaultLlm` — used everywhere except Ino's new tool-capable-specific resolution — still resolves to whichever is tagged Balanced; since neither is tagged Balanced here, `DefaultLlm`'s fallback chain picks Reasoning next, i.e. `Llama31_8B` becomes the new global default too. Confirm this is the intended outcome — it means everything, not just Ino, now defaults to the larger model — or explicitly tag `Qwen25Coder1_5B` `.AsBalanced()` instead if only Ino's tool-calling path should use the bigger model and everything else should keep the cheap 1.5B default. This is a real product decision, not just a code detail — flag it in your task report rather than picking silently.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --logger "console;verbosity=minimal"` (from repo root)
Expected: PASS — the new test green, and the full suite still shows only the one known out-of-scope `GatewayServiceSalesforceViaChatIdentityTests` failure.

- [ ] **Step 5: Commit**

```bash
git add src/DigitalBrain.Aspire/LlmModels.cs hosts/DigitalBrain.AppHost/AppHost.cs integrations/DigitalBrain.Ino/InoNeuron.cs tests/DigitalBrain.Tests/Ino/InoNeuronToolCapableModelResolutionTests.cs
git commit -m "fix: give Ino a genuinely tool-capable model instead of the code-completion-only default"
```

---

## Execution order

Tasks 1-3 are foundation (capability flag, env export, shared reader) and must land in order. Tasks 4-6 (keyed DI + both attribute facets) depend on 1-3 and on each other only loosely (6 depends on 4's `LlmServiceKeys` helper) — land 4 then 5 then 6. Tasks 7-8 (Anthropic, xAI) depend only on 4 and are independent of each other and of 5/6 — order between them doesn't matter. Task 9 (the actual fix) depends on 3 and 4, and should land last since it's the integration point for everything else.

## Known verification items to double-check at implementation time (flagged throughout, collected here)

- Exact current OpenAI, Anthropic, and xAI model identifiers — this plan's `Claude45Haiku`/`Grok41` and any OpenAI model refresh are best-effort based on adjacent evidence, not confirmed against a live API; verify before shipping.
- Whether Ollama's current model library still tags `llama3.1:8b` as tool-capable (verify via Ollama's own docs/model library at implementation time) — swap to a different confirmed tool-capable tag if not.
- The exact `IGrainContext` interface member list for Task 5/6's test fakes (check the installed Orleans version).
- The Balanced-vs-Reasoning tiering decision in Task 9 (flagged inline) — confirm whether Ino's tool-calling path alone should get the bigger model, or the whole app's default should move too.
