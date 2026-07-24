# Compiled Modules and Brain Hosting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace convention/string-driven module activation and caller-configured storage with compiled module capsules and the single durable `builder.AddDigitalBrain("name")` AppHost entry point.

**Architecture:** Module runtime assemblies generate a typed capsule beside each partial `IModule` marker. AppHost records generated `ModuleId` values, the compiled silo validates and activates matching capsules, and Testing can consume the same capsules later. `AddDigitalBrain` creates the one Azure/Azurite durability profile internally; module hosting extensions receive a typed module builder instead of recovering mutable state through `ConditionalWeakTable`.

**Tech Stack:** .NET 10, C# incremental source generators, Orleans 10.2.2-rc.2, Aspire 13.4.6, xUnit v3.

## Global Constraints

- Work only in `E:\intochat\digitalbrain` on the current branch.
- Preserve the user's existing unstaged `Directory.Packages.props` line-ending change; never stage it.
- `IDigitalBrain` remains the owner-scoped client facade; do not add a root `DigitalBrain` neuron.
- Public neuron capability methods omit `Async`; infrastructure lifecycle methods retain normal .NET async naming.
- Neuron method attributes use `[Alias(nameof(Method))]`; persisted facts/state retain explicit stable aliases.
- Module identity is generated from fully-qualified type identity; no hand-authored short names.
- No runtime assembly scanning, AppDomain catalog, method-name lookup, or compatibility wrappers.
- AppHost exposes `AddDigitalBrain(name)` only for brain infrastructure; storage profiles are internal.
- `AsClient()` must never receive storage, module secrets, or the state-protection key.
- Each task uses test-first changes, keeps the touched projects green, and ends with a focused commit.
- Root release gate remains:

```powershell
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build
```

---

## File structure

### Create

| File | Responsibility |
|---|---|
| `src/DigitalBrain.Abstractions/ModuleId.cs` | Typed generated module identity crossing AppHost/silo configuration |
| `src/DigitalBrain.Kernel/ICompiledModule.cs` | Hidden compiler ABI consumed by the silo and Testing |
| `src/DigitalBrain.Aspire.Hosting/DigitalBrainBuilder.cs` | Opaque AppHost composition handle and owned infrastructure |
| `src/DigitalBrain.Aspire.Hosting/DigitalBrainModuleBuilder.cs` | Typed module-hosting configuration context |
| `src/DigitalBrain.Aspire.Hosting/DigitalBrainModuleProjection.cs` | One module-owned silo projection |
| `src/DigitalBrain.Aspire.Hosting/DigitalBrainHostingExtensions.cs` | `AddDigitalBrain`, `AddModule`, and brain references |
| `tests/DigitalBrain.Tests/NeuronContractNamingContracts.cs` | L0 neuron method/alias law |
| `tests/DigitalBrain.Tests/CompiledModuleContracts.cs` | L0 capsule identity and activation law |

### Modify

| File group | Responsibility |
|---|---|
| `src/DigitalBrain.Abstractions/IModule.cs` | Require generator-supplied static `ModuleId` |
| `src/DigitalBrain.Abstractions/DigitalBrain.Abstractions.csproj` | Consume the generator privately for `INeuron` identity |
| `src/DigitalBrain.Abstractions/INeuron.cs`, `ISessionNeuron.cs`, `ISubscriptionRegistry.cs` | Drop `Async` from neuron methods and use `nameof` aliases |
| `src/DigitalBrain.Kernel/Neuron.cs`, `SessionNeuron.cs`, `SubscriptionRegistry.cs` | Implement renamed kernel neuron contracts |
| `src/DigitalBrain.SourceGeneration/DispatchManifestGenerator.cs` | Emit partial module capsules and typed silo composition |
| `src/DigitalBrain.Kernel/DigitalBrainSiloBuilderExtensions.cs` | Validate/activate typed capsules |
| `src/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj` | Compile the split hosting files |
| `modules/*.Contracts/*.csproj` | Consume the generator privately for neuron identities |
| `modules/*.Contracts/I*.cs` | Partial neuron interfaces with generated fully-qualified aliases |
| `modules/*/*Module.cs` | Partial module marker plus typed runtime hook |
| `modules/*.Contracts/*.cs` and implementations | Neuron method rename/alias migration |
| `modules/*.Aspire.Hosting/*.cs` | Typed `DigitalBrainModuleBuilder<TModule>` extensions |
| `hosts/DigitalBrain.AppHost/AppHost.cs` | One-call durable brain composition |
| `hosts/DigitalBrain.TestingAppHost/AppHost.cs` | One-call durable test graphs |
| `tests/DigitalBrain.Tests/*Contracts.cs` | New generated/hosting surface |
| `docs/architecture.md`, `docs/quickstart.md`, `docs/packages.md` | Approved names and hosting examples |

### Delete

| File/API | Reason |
|---|---|
| `src/DigitalBrain.Aspire.Hosting/BrainHosting.cs` | Split and replaced by focused typed files |
| `BrainService`, `BrainModuleHosting`, `BrainModuleReference` | Mutable/convention-based hosting ABI |
| `AddBrain`, `WithAzureStorage`, `WithDevelopmentStores` | Storage is owned by `AddDigitalBrain` |
| Module `Configure` / `ConfigureSerialization` name conventions | Replaced by generated capsule plus partial hook |

---

### Task 1: Pin the neuron method and alias law

**Files:**
- Create: `tests/DigitalBrain.Tests/NeuronContractNamingContracts.cs`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`

**Interfaces:**
- Consumes: exported assemblies already referenced by `DigitalBrain.Tests`
- Produces: one L0 guard covering every interface assignable to `INeuron`

- [ ] **Step 1: Write the failing reflection and source-shape tests**

Create `NeuronContractNamingContracts.cs`:

```csharp
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using DigitalBrain.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Orleans;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class NeuronContractNamingContracts
{
    private static readonly Assembly[] ContractAssemblies =
    [
        typeof(INeuron).Assembly,
        typeof(IAgent).Assembly,
        typeof(IGmail).Assembly,
        typeof(ISalesforce).Assembly,
        typeof(ITask).Assembly,
    ];

    [Fact]
    public void NeuronCapabilityMethodsDoNotEndInAsync()
    {
        var offenders = NeuronContracts()
            .SelectMany(type => type.GetMethods().Select(method => $"{type.FullName}.{method.Name}"))
            .Where(name => name.EndsWith("Async", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryNeuronCapabilityAliasEqualsItsMethodName()
    {
        var offenders = NeuronContracts()
            .SelectMany(type => type.GetMethods())
            .Select(method => new
            {
                Method = $"{method.DeclaringType!.FullName}.{method.Name}",
                Alias = method.GetCustomAttribute<AliasAttribute>()?.Alias,
                method.Name,
            })
            .Where(entry => entry.Alias is null || entry.Alias != entry.Name)
            .Select(entry => $"{entry.Method} alias={entry.Alias ?? "<missing>"}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryNeuronContractAliasIsItsFullyQualifiedTypeName()
    {
        var offenders = NeuronContracts()
            .Select(type => new
            {
                type.FullName,
                Alias = type.GetCustomAttribute<AliasAttribute>()?.Alias,
            })
            .Where(entry => entry.Alias != entry.FullName)
            .Select(entry => $"{entry.FullName} alias={entry.Alias ?? "<missing>"}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NeuronCapabilitySourceUsesNameofAliases()
    {
        var root = RepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(root, "src", "DigitalBrain.Abstractions"),
            Path.Combine(root, "modules"),
        };
        var offenders = sourceRoots
            .SelectMany(sourceRoot => Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            .SelectMany(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path))
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .SelectMany(method => method.AttributeLists
                    .SelectMany(list => list.Attributes)
                    .Where(attribute => attribute.Name.ToString().EndsWith("Alias", StringComparison.Ordinal))
                    .Where(attribute => attribute.ArgumentList?.Arguments.SingleOrDefault()?.Expression
                        is not InvocationExpressionSyntax
                        {
                            Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                        })
                    .Select(_ => $"{Path.GetRelativePath(root, path)}:{method.GetLocation().GetLineSpan().StartLinePosition.Line + 1}")))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<Type> NeuronContracts() =>
        ContractAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.IsInterface && type != typeof(INeuron) && typeof(INeuron).IsAssignableFrom(type))
            .Append(typeof(INeuron));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("DigitalBrain.slnx was not found.");
    }
}
```

Add the Roslyn reference to `DigitalBrain.Tests.csproj`:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
```

- [ ] **Step 2: Run the new guard and verify the intended red state**

Run:

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~NeuronContractNamingContracts" --logger "console;verbosity=minimal"
```

Expected: FAIL listing `INeuron.DeliverAsync`, `ISessionNeuron.FireAsync`, AI, Tasks, Google, and
Salesforce capability methods, literal method aliases, and short hand-authored neuron type aliases.

- [ ] **Step 3: Commit the red architecture guard**

```powershell
git add tests/DigitalBrain.Tests/NeuronContractNamingContracts.cs tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj
git commit -m "test: pin neuron method naming law"
```

---

### Task 2: Rename neuron capability methods in one alpha migration

**Files:**
- Modify: `src/DigitalBrain.Abstractions/INeuron.cs`
- Modify: `src/DigitalBrain.Abstractions/ISessionNeuron.cs`
- Modify: `src/DigitalBrain.Abstractions/ISubscriptionRegistry.cs`
- Modify: `src/DigitalBrain.Kernel/Neuron.cs`
- Modify: `src/DigitalBrain.Kernel/SessionNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/SubscriptionRegistry.cs`
- Modify: `src/DigitalBrain.Client/DigitalBrainClient.cs`
- Modify: `modules/DigitalBrain.Modules.AI.Contracts/IAgent.cs`
- Modify: `modules/DigitalBrain.Modules.AI.Contracts/ILLM.cs`
- Modify: `modules/DigitalBrain.Modules.Google.Contracts/IGmail.cs`
- Modify: `modules/DigitalBrain.Modules.Salesforce.Contracts/ISalesforce.cs`
- Modify: `modules/DigitalBrain.Modules.Tasks.Contracts/ITask.cs`
- Modify: `modules/DigitalBrain.Modules.Tasks.Contracts/IWorker.cs`
- Modify: implementations and callers selected by the compiler
- Modify: tests and documentation examples selected by `rg`

**Interfaces:**
- Consumes: the failing law from Task 1
- Produces: semantic neuron verbs without `Async`, `[Alias(nameof(...))]`, and partial neuron
  interfaces ready for generated type aliases

- [ ] **Step 1: Apply the exhaustive contract rename table**

Use this table exactly; do not rename private infrastructure helpers:

```text
INeuron.DeliverAsync                    -> Deliver
INeuron.ReadJournalAsync                -> ReadJournal
INeuron.WatchAsync                      -> Watch
INeuron.UnwatchAsync                    -> Unwatch
ISessionNeuron.FireAsync                -> Fire
ISessionNeuron.EmitAsync                -> Emit
ISessionNeuron.ReadNeuronJournalAsync   -> ReadNeuronJournal
ISessionNeuron.WatchNeuronAsync         -> WatchNeuron
ISessionNeuron.UnwatchNeuronAsync       -> UnwatchNeuron
ISubscriptionRegistry.RegisterAsync     -> Register
ISubscriptionRegistry.SubscribersAsync  -> Subscribers
ISubscriptionRegistry.SubscriberCountAsync -> SubscriberCount
IAgent.RespondAsync                     -> Respond
ILLM.RespondAsync                       -> Respond
IGmail.ReadMessageAsync                 -> ReadMessage
ISalesforce.ProposeAccountDescriptionAsync -> ProposeAccountDescription
ISalesforce.ApproveAccountDescriptionAsync -> ApproveAccountDescription
ITask.StartAsync                        -> Start
ITask.CancelAsync                       -> Cancel
ITask.ReadAsync                         -> Read
IWorker.AcceptAsync                     -> Accept
IWorker.ContinueAsync                   -> Continue
IWorker.CancelAsync                     -> Cancel
```

Every declaration uses its resulting method name:

```csharp
[Alias(nameof(Respond))]
Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages);
```

Make each `INeuron`-derived interface `partial`. Keep its existing type-level alias only until Task 3
emits the generated replacement.

Rename matching public implementations and call sites. Keep lifecycle/framework methods such as
`StartAsync`, `StopAsync`, `DisposeAsync`, `GetResponseAsync`, MAF checkpoint overrides, HTTP calls,
and private helpers unchanged.

- [ ] **Step 2: Use the compiler to finish all typed call sites**

Run:

```powershell
dotnet build DigitalBrain.slnx -c Release
```

Expected initially: compiler errors naming remaining old contract calls. Resolve every error by
calling the renamed typed method. Do not add obsolete forwarding methods.

- [ ] **Step 3: Prove the naming law and client behavior**

Run:

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~NeuronCapabilityMethodsDoNotEndInAsync|FullyQualifiedName~EveryNeuronCapabilityAliasEqualsItsMethodName|FullyQualifiedName~NeuronCapabilitySourceUsesNameofAliases|FullyQualifiedName~ClientApiContracts|FullyQualifiedName~SerializationContracts" --logger "console;verbosity=minimal"
```

Expected: PASS. `EveryNeuronContractAliasIsItsFullyQualifiedTypeName` remains red until Task 3
replaces the hand-authored interface aliases with generated identities.

- [ ] **Step 4: Prove that no neuron contract compatibility surface remains**

Run:

```powershell
rg -n --glob "*.cs" "interface I[A-Za-z0-9_]+.*INeuron|Task(<[^;]+>)?\s+[A-Za-z0-9_]+Async\(" src/DigitalBrain.Abstractions modules
```

Expected: async methods may remain in implementation/infrastructure files, but no exported
`INeuron`-derived contract method ends in `Async`.

- [ ] **Step 5: Commit the alpha contract migration**

```powershell
git add src/DigitalBrain.Abstractions src/DigitalBrain.Kernel src/DigitalBrain.Client modules samples hosts tests docs
git restore --staged Directory.Packages.props
git commit -m "refactor: make neuron capability names intrinsically async"
```

---

### Task 3: Generate neuron identities and one compiled capsule beside every module marker

**Files:**
- Create: `src/DigitalBrain.Abstractions/ModuleId.cs`
- Create: `src/DigitalBrain.Kernel/ICompiledModule.cs`
- Create: `tests/DigitalBrain.Tests/CompiledModuleContracts.cs`
- Modify: `src/DigitalBrain.Abstractions/IModule.cs`
- Modify: `src/DigitalBrain.Abstractions/DigitalBrain.Abstractions.csproj`
- Modify: `src/DigitalBrain.SourceGeneration/DispatchManifestGenerator.cs`
- Modify: every `modules/*.Contracts/*.csproj`
- Modify: every `INeuron`-derived interface to remove its hand-authored type alias
- Modify: each runtime module `.csproj` to reference the generator as an analyzer
- Modify: `AIModule.cs`, `TasksModule.cs`, `GoogleModule.cs`, `SalesforceModule.cs`

**Interfaces:**
- Consumes: partial public classes implementing `IModule`
- Produces:

```csharp
public interface IModule
{
    static abstract ModuleId Id { get; }
}

[EditorBrowsable(EditorBrowsableState.Never)]
public interface ICompiledModule
{
    ModuleId Id { get; }
    void Activate(ISiloBuilder builder);
}
```

- [ ] **Step 1: Write failing capsule contracts**

Create `CompiledModuleContracts.cs`:

```csharp
using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;
using DigitalBrain.Tasks;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CompiledModuleContracts
{
    private static readonly Type[] Modules =
    [
        typeof(AIModule),
        typeof(TasksModule),
        typeof(GoogleModule),
        typeof(SalesforceModule),
    ];

    [Fact]
    public void EveryModuleHasOneGeneratedFullyQualifiedIdentity()
    {
        var identities = Modules
            .Select(type => (type, Id: ((ICompiledModule)Activator.CreateInstance(type)!).Id))
            .ToArray();

        Assert.All(identities, entry => Assert.Equal(entry.type.FullName, entry.Id.Value));
        Assert.Equal(identities.Length, identities.Select(entry => entry.Id).Distinct().Count());
    }

    [Fact]
    public void CapsuleAbiIsHiddenFromNormalIntellisense()
    {
        var attribute = typeof(ICompiledModule)
            .GetCustomAttributes(typeof(EditorBrowsableAttribute), inherit: false)
            .Cast<EditorBrowsableAttribute>()
            .Single();

        Assert.Equal(EditorBrowsableState.Never, attribute.State);
    }
}
```

- [ ] **Step 2: Run the capsule tests and verify red**

Run:

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~CompiledModuleContracts" --logger "console;verbosity=minimal"
```

Expected: FAIL because `ModuleId` and `ICompiledModule` do not exist.

- [ ] **Step 3: Add the typed identity and hidden runtime ABI**

Add `ModuleId.cs`:

```csharp
namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.module-id")]
public readonly record struct ModuleId
{
    public ModuleId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    [Id(0)]
    public string Value { get; }

    public override string ToString() => Value;
}
```

Change `IModule.cs` to:

```csharp
namespace DigitalBrain.Abstractions;

public interface IModule
{
    static abstract ModuleId Id { get; }
}
```

Add `ICompiledModule.cs`:

```csharp
using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

[EditorBrowsable(EditorBrowsableState.Never)]
public interface ICompiledModule
{
    ModuleId Id { get; }

    void Activate(ISiloBuilder builder);
}
```

- [ ] **Step 4: Make module markers partial and replace convention methods with partial hooks**

The AI shape is:

```csharp
public sealed partial class AIModule : IModule
{
    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        DurablePayloadProtectionHosting.Configure(builder.Services, builder.Configuration);
        AIClients.Add(builder.Services);
        builder.Services.AddSerializer(serializer => serializer.AddJsonSerializer(IsMeaiContractType));
    }

    private static bool IsMeaiContractType(Type type)
        => type == typeof(ChatMessage) || type == typeof(ChatResponse);
}
```

Google and Salesforce put their existing runtime registrations in the same hook. `TasksModule` is:

```csharp
public sealed partial class TasksModule : IModule;
```

Delete the old public static `Configure` and `ConfigureSerialization` methods.

- [ ] **Step 5: Extend the generator to emit the capsule**

For each public partial interface implementing `INeuron`, emit a partial declaration with a
fully-qualified identity:

```csharp
[global::Orleans.Alias("DigitalBrain.AI.IAgent")]
public partial interface IAgent;
```

Emit a compiler diagnostic when a neuron contract is not partial or still declares its own
type-level `Alias`. Persisted synapse/state aliases are outside this rule and remain unchanged.

For each public non-abstract partial class implementing `IModule`, emit a partial declaration
equivalent to:

```csharp
public sealed partial class AIModule : global::DigitalBrain.Kernel.ICompiledModule
{
    public static global::DigitalBrain.Abstractions.ModuleId Id { get; } =
        new("DigitalBrain.AI.AIModule");

    global::DigitalBrain.Abstractions.ModuleId
        global::DigitalBrain.Kernel.ICompiledModule.Id => Id;

    void global::DigitalBrain.Kernel.ICompiledModule.Activate(
        global::Orleans.Hosting.ISiloBuilder builder)
    {
        ConfigureRuntime(builder);
        builder.AddBroadcastHandlers(typeof(AIModule).Assembly);
    }

    static partial void ConfigureRuntime(global::Orleans.Hosting.ISiloBuilder builder);
}
```

Emit compiler diagnostics for a module marker that is not `partial`, is nested, is generic, or lacks
a public parameterless constructor. Remove `ModuleSerializationMethod`,
`HasSerializationHook`, and all `GetMembers("Configure...")` logic from the generator.

- [ ] **Step 6: Add the analyzer reference to every contracts and runtime module**

Add this item to `src/DigitalBrain.Abstractions/DigitalBrain.Abstractions.csproj`:

```xml
<ProjectReference Include="..\DigitalBrain.SourceGeneration\DigitalBrain.SourceGeneration.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false"
                  PrivateAssets="all" />
```

Add this item to the AI, Google, Salesforce, and Tasks Contracts projects and the Google and
Salesforce runtime projects:

```xml
<ProjectReference Include="..\..\src\DigitalBrain.SourceGeneration\DigitalBrain.SourceGeneration.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false"
                  PrivateAssets="all" />
```

Keep the existing equivalent analyzer reference in the AI and Tasks runtime projects. Plan 4 uses
the second form for the Quickstart and Time Contracts/runtime projects when those projects are
created.

- [ ] **Step 7: Run capsule and generator tests**

Run:

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~CompiledModuleContracts|FullyQualifiedName~DispatchManifestContracts" --logger "console;verbosity=minimal"
```

Expected: PASS, and generated module identities equal fully-qualified CLR names.

- [ ] **Step 8: Commit capsules**

```powershell
git add src/DigitalBrain.Abstractions src/DigitalBrain.Kernel/ICompiledModule.cs src/DigitalBrain.SourceGeneration modules tests/DigitalBrain.Tests/CompiledModuleContracts.cs
git restore --staged Directory.Packages.props
git commit -m "feat: generate compiled module capsules"
```

---

### Task 4: Activate selected typed capsules in the silo

**Files:**
- Modify: `src/DigitalBrain.SourceGeneration/DispatchManifestGenerator.cs`
- Modify: `src/DigitalBrain.Kernel/DigitalBrainSiloBuilderExtensions.cs`
- Modify: `tests/DigitalBrain.Tests/DispatchManifestContracts.cs`
- Modify: `tests/DigitalBrain.Tests/ModuleActivationContracts.cs`

**Interfaces:**
- Consumes: generated `IModule.Id` and `ICompiledModule`
- Produces:

```csharp
public static IReadOnlySet<ModuleId> Add(
    ISiloBuilder builder,
    string? siloLabel,
    IReadOnlyCollection<ICompiledModule> availableModules);
```

- [ ] **Step 1: Rewrite the activation tests to prohibit the string catalog**

Replace the reflection assertion over `DigitalBrain.Generated.ModuleCatalog.Modules` with:

```csharp
[Fact]
public void GeneratedCompositionContainsTypedCompiledModules()
{
    var composition = Probed.GetType(
        "DigitalBrain.Generated.CompiledModuleCatalog",
        throwOnError: true)!;
    var modules = (IReadOnlyList<ICompiledModule>)composition
        .GetProperty("Modules", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
        .GetValue(null)!;

    Assert.Contains(modules, module => module.Id == AIModule.Id);
    Assert.DoesNotContain(
        composition.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
        field => field.FieldType == typeof(string[]));
}
```

Update selection tests to assign `AIModule.Id.Value` at the environment boundary and assert that
activation registers `IChatClient`.

- [ ] **Step 2: Run the focused tests and verify red**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~DispatchManifestContracts|FullyQualifiedName~ModuleActivationContracts" --logger "console;verbosity=minimal"
```

Expected: FAIL because the generator still emits `string[] Modules`.

- [ ] **Step 3: Emit a typed generated catalog and extension**

Generate:

```csharp
internal static class CompiledModuleCatalog
{
    internal static IReadOnlyList<global::DigitalBrain.Kernel.ICompiledModule> Modules { get; } =
    [
        new global::DigitalBrain.AI.AIModule(),
        new global::DigitalBrain.Tasks.TasksModule(),
    ];
}
```

The generated silo extension calls:

```csharp
return global::DigitalBrain.Kernel.DigitalBrainRuntime.Add(
    builder,
    siloLabel,
    global::DigitalBrain.Generated.CompiledModuleCatalog.Modules);
```

Delete generated direct calls to module static methods and generated module-name comparisons.

- [ ] **Step 4: Make runtime selection typed**

In `DigitalBrainRuntime.Add`, parse the external configuration exactly once:

```csharp
var declared = hostContext.Configuration
    .GetSection("DigitalBrain:Modules")
    .GetChildren()
    .Select(section => new ModuleId(section.Value
        ?? throw new InvalidOperationException("DigitalBrain:Modules contains an empty module identity.")))
    .ToArray();
```

Validate duplicates and unavailable IDs using `ModuleId`. Activate selected capsules:

```csharp
foreach (var module in availableModules.Where(module => selected.Contains(module.Id)))
{
    module.Activate(builder);
}
```

The environment is a string wire boundary; no other runtime code compares module type-name strings.

- [ ] **Step 5: Run activation, serialization, and full L0 tests**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~DispatchManifestContracts|FullyQualifiedName~ModuleActivationContracts|FullyQualifiedName~SerializationContracts" --logger "console;verbosity=minimal"
```

Expected: PASS.

- [ ] **Step 6: Commit typed activation**

```powershell
git add src/DigitalBrain.SourceGeneration/DispatchManifestGenerator.cs src/DigitalBrain.Kernel/DigitalBrainSiloBuilderExtensions.cs tests/DigitalBrain.Tests
git commit -m "refactor: activate typed module capsules"
```

---

### Task 5: Replace BrainService with typed hosting builders

**Files:**
- Create: `src/DigitalBrain.Aspire.Hosting/DigitalBrainBuilder.cs`
- Create: `src/DigitalBrain.Aspire.Hosting/DigitalBrainModuleBuilder.cs`
- Create: `src/DigitalBrain.Aspire.Hosting/DigitalBrainModuleProjection.cs`
- Create: `src/DigitalBrain.Aspire.Hosting/DigitalBrainHostingExtensions.cs`
- Modify: `tests/DigitalBrain.Tests/IntegrationHostingContracts.cs`
- Modify: `tests/DigitalBrain.Tests/AIHostingContracts.cs`
- Delete after green: `src/DigitalBrain.Aspire.Hosting/BrainHosting.cs`

**Interfaces:**
- Consumes: `IModule.Id`
- Produces:

```csharp
DigitalBrainBuilder builder.AddDigitalBrain(string name);
DigitalBrainBuilder brain.AddModule<TModule>();
DigitalBrainBuilder brain.AddModule<TModule>(
    Action<DigitalBrainModuleBuilder<TModule>> configure);
ClientDigitalBrainReference brain.AsClient();
```

- [ ] **Step 1: Rewrite hosting shape tests first**

Add assertions that the only public root creation method is:

```csharp
var add = typeof(DigitalBrainHostingExtensions)
    .GetMethods(BindingFlags.Public | BindingFlags.Static)
    .Single(method => method.Name == "AddDigitalBrain");

Assert.Equal(typeof(IDistributedApplicationBuilder), add.GetParameters()[0].ParameterType);
Assert.Equal(typeof(string), add.GetParameters()[1].ParameterType);
Assert.Equal(typeof(DigitalBrainBuilder), add.ReturnType);
```

Add negative assertions:

```csharp
var publicNames = typeof(DigitalBrainHostingExtensions).Assembly
    .GetExportedTypes()
    .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
    .Select(method => method.Name)
    .ToHashSet(StringComparer.Ordinal);

Assert.DoesNotContain("AddBrain", publicNames);
Assert.DoesNotContain("WithAzureStorage", publicNames);
Assert.DoesNotContain("WithDevelopmentStores", publicNames);
Assert.DoesNotContain("BrainModuleHosting", typeof(DigitalBrainHostingExtensions).Assembly.GetExportedTypes().Select(type => type.Name));
```

- [ ] **Step 2: Run hosting contracts and verify red**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~IntegrationHostingContracts|FullyQualifiedName~AIHostingContracts" --logger "console;verbosity=minimal"
```

Expected: FAIL on the old `AddBrain`/storage/profile surface.

- [ ] **Step 3: Add the opaque builders**

`DigitalBrainBuilder` owns the application builder, Orleans model, generated module IDs,
projections, storage references, readiness dependencies, and protection-key parameter. Expose only
`Name` publicly. Internal members are visible to hosting extension packages through explicitly
hidden methods, not a `ConditionalWeakTable`.

`DigitalBrainModuleBuilder<TModule>` has this shape:

```csharp
public sealed class DigitalBrainModuleBuilder<TModule>
    where TModule : class, IModule, new()
{
    internal DigitalBrainModuleBuilder(DigitalBrainBuilder brain) => Brain = brain;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public DigitalBrainBuilder Brain { get; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AddProjection(DigitalBrainModuleProjection projection)
        => Brain.AddProjection(projection);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void RequireStateProtection()
        => Brain.RequireStateProtection();
}
```

`DigitalBrainModuleProjection` retains one abstract `Apply<TResource>` operation for silo-only
projection. Rename the client reference to `ClientDigitalBrainReference`.

- [ ] **Step 4: Implement typed module selection without module instances**

```csharp
public static DigitalBrainBuilder AddModule<TModule>(this DigitalBrainBuilder brain)
    where TModule : class, IModule, new()
    => brain.AddModule<TModule>(static _ => { });

public static DigitalBrainBuilder AddModule<TModule>(
    this DigitalBrainBuilder brain,
    Action<DigitalBrainModuleBuilder<TModule>> configure)
    where TModule : class, IModule, new()
{
    ArgumentNullException.ThrowIfNull(brain);
    ArgumentNullException.ThrowIfNull(configure);
    brain.Select(TModule.Id);
    configure(new DigitalBrainModuleBuilder<TModule>(brain));
    return brain;
}
```

`Select` rejects a duplicate `ModuleId`. Delete module-marker instantiation for AppHost state,
`BrainModuleHosting.Bind/Unbind`, and every `ConditionalWeakTable`.

- [ ] **Step 5: Move reference projection unchanged behind the new names**

Silo reference projects:

- the Orleans service;
- journal connection;
- readiness waits;
- state-protection key when required;
- generated module ID values;
- module-specific projections.

Client reference projects only `brain.Orleans.AsClient()`.

- [ ] **Step 6: Run hosting contracts**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~IntegrationHostingContracts|FullyQualifiedName~AIHostingContracts|FullyQualifiedName~PackageBoundaryContracts" --logger "console;verbosity=minimal"
```

Expected: PASS.

- [ ] **Step 7: Delete the monolithic old file and commit**

```powershell
git rm src/DigitalBrain.Aspire.Hosting/BrainHosting.cs
git add src/DigitalBrain.Aspire.Hosting tests/DigitalBrain.Tests
git commit -m "refactor: introduce typed brain hosting builders"
```

---

### Task 6: Make AddDigitalBrain own the complete durable profile

**Files:**
- Modify: `src/DigitalBrain.Aspire.Hosting/DigitalBrainHostingExtensions.cs`
- Modify: `tests/DigitalBrain.Tests/IntegrationHostingContracts.cs`
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `hosts/DigitalBrain.TestingAppHost/AppHost.cs`

**Interfaces:**
- Consumes: `DigitalBrainBuilder`
- Produces: one durable Azure/Azurite profile from `AddDigitalBrain(name)`

- [ ] **Step 1: Add failing resource-ownership tests**

Build an AppHost model with:

```csharp
var builder = DistributedApplication.CreateBuilder();
var brain = builder.AddDigitalBrain("orders");
var silo = builder.AddResource(new ProjectionProbe("silo")).WithReference(brain);
var client = builder.AddResource(new ProjectionProbe("client")).WithReference(brain.AsClient());
```

Assert:

- one Azure Storage account named `orders-storage`;
- tables `orders-clustering` and `orders-reminders`;
- Blob resource `orders-journal`;
- silo projection contains all required connections and health waits;
- client projection contains none of them;
- no memory clustering/reminder/storage annotation exists;
- a selected protected module creates one `orders-state-protection-key` secret and only the silo sees it.

- [ ] **Step 2: Run the focused tests and verify red**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~IntegrationHostingContracts" --logger "console;verbosity=minimal"
```

Expected: FAIL because the caller still has to supply storage.

- [ ] **Step 3: Construct storage inside AddDigitalBrain**

Implement the profile inside `AddDigitalBrain`:

```csharp
var storage = builder
    .AddAzureStorage($"{name}-storage")
    .RunAsEmulator();
var clustering = storage.AddTables($"{name}-clustering");
var reminders = storage.AddTables($"{name}-reminders");
var journal = storage.AddBlobs($"{name}-journal");
var orleans = builder
    .AddOrleans(name)
    .WithClustering(clustering)
    .WithReminders(reminders);
```

Store the journal and dependency resources inside `DigitalBrainBuilder`. `WithReference(brain)` adds
`WaitUntilHealthy` annotations for storage, clustering, reminders, and journal. Do not add a
configuration switch or memory fallback.

- [ ] **Step 4: Simplify both AppHosts**

The production shape becomes:

```csharp
var brain = builder.AddDigitalBrain("brain");

brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());
```

The Testing AppHost creates `brain` and `probe` with two calls and no shared caller-owned storage.

- [ ] **Step 5: Run L0 and AppHost build**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~IntegrationHostingContracts|FullyQualifiedName~AIHostingContracts" --logger "console;verbosity=minimal"
dotnet build hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj -c Release
dotnet build hosts/DigitalBrain.TestingAppHost/DigitalBrain.TestingAppHost.csproj -c Release
```

Expected: PASS.

- [ ] **Step 6: Commit one-call hosting**

```powershell
git add src/DigitalBrain.Aspire.Hosting hosts tests/DigitalBrain.Tests
git commit -m "feat: encapsulate durable brain infrastructure"
```

---

### Task 7: Migrate module Aspire extensions off mutable marker state

**Files:**
- Modify: `modules/DigitalBrain.Modules.AI.Aspire.Hosting/AIHostingExtensions.cs`
- Modify: `modules/DigitalBrain.Modules.Google.Aspire.Hosting/GoogleHostingExtensions.cs`
- Modify: `modules/DigitalBrain.Modules.Salesforce.Aspire.Hosting/SalesforceHostingExtensions.cs`
- Modify: `src/DigitalBrain.Integrations.Mcp.Aspire.Hosting/McpHosting.cs`
- Modify: `tests/DigitalBrain.Tests/AIHostingContracts.cs`
- Modify: `tests/DigitalBrain.Tests/IntegrationHostingContracts.cs`

**Interfaces:**
- Consumes: `DigitalBrainModuleBuilder<TModule>`
- Produces: module-owned resources and silo-only projections with no marker-instance lookup

- [ ] **Step 1: Change extension receiver contracts in tests**

Assert these exact receiver types:

```csharp
Assert.Equal(
    typeof(DigitalBrainModuleBuilder<AIModule>),
    typeof(AIHostingExtensions).GetMethod(nameof(AIHostingExtensions.WithLlm))!
        .GetParameters()[0].ParameterType.GetGenericTypeDefinition()
        .MakeGenericType(typeof(AIModule)));
```

Use equivalent direct reflection for `WithGmail` and `WithSalesforce`. Add a source guard that
`ConditionalWeakTable` does not occur under `modules/*.Aspire.Hosting` or
`src/DigitalBrain.Aspire.Hosting`.

- [ ] **Step 2: Run hosting tests and verify red**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~AIHostingContracts|FullyQualifiedName~IntegrationHostingContracts" --logger "console;verbosity=minimal"
```

Expected: FAIL on old marker receivers.

- [ ] **Step 3: Change each extension receiver**

AI:

```csharp
public static DigitalBrainModuleBuilder<AIModule> WithLlm<TModel>(
    this DigitalBrainModuleBuilder<AIModule> module)
    where TModel : LLM
{
    ArgumentNullException.ThrowIfNull(module);
    module.RequireStateProtection();
    module.AddProjection(AIModelProjection.Create<TModel>(module.Brain));
    return module;
}
```

Google and Salesforce use:

```csharp
public static DigitalBrainModuleBuilder<GoogleModule> WithGmail(
    this DigitalBrainModuleBuilder<GoogleModule> module)
{
    McpProviderHosting.Register(module, Gmail);
    return module;
}
```

Change `McpProviderHosting.Register` to accept the typed module builder and attach its projection.
Delete all `BrainModuleHosting.BrainOf`, `Bind`, `Unbind`, and module-keyed static state.

- [ ] **Step 4: Preserve duplicate detection inside the brain**

The brain owns selected model/provider identities. Calling `WithLlm<Llama32>()` or `WithGmail()`
twice throws with brain and resource identity. Do not reintroduce a process-static dictionary.

- [ ] **Step 5: Run all hosting and module package tests**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~AIHostingContracts|FullyQualifiedName~IntegrationHostingContracts|FullyQualifiedName~PackableProjects" --logger "console;verbosity=minimal"
```

Expected: PASS.

- [ ] **Step 6: Commit module hosting migration**

```powershell
git add modules src/DigitalBrain.Integrations.Mcp.Aspire.Hosting tests/DigitalBrain.Tests
git commit -m "refactor: make module hosting configuration typed"
```

---

### Task 8: Update architecture, docs, and deletion gates

**Files:**
- Modify: `docs/architecture.md`
- Modify: `docs/quickstart.md`
- Modify: `docs/packages.md`
- Modify: `tests/DigitalBrain.Tests/ArchitectureCutContracts.cs`
- Modify: `tests/DigitalBrain.Tests/PackageBoundaryContracts.cs`

**Interfaces:**
- Consumes: final foundation/hosting APIs
- Produces: no stale public examples or old API names

- [ ] **Step 1: Add failing deletion guards**

Add source/API guards for:

```text
IDigitalBrain remains the owner-scoped client contract
no concrete DigitalBrain neuron or root-neuron interface exists
public AddBrain
public WithAzureStorage
public WithDevelopmentStores
BrainService
BrainModuleHosting
ConditionalWeakTable<IModule
ModuleSerializationMethod
GetMembers("Configure
DigitalBrain.Generated.ModuleCatalog string[]
```

Use reflection to assert `IDigitalBrain` remains implemented by `DigitalBrainClient`, and that no
exported type named `DigitalBrain` derives from `Neuron` or implements `INeuron`. Hosting state stays
in `DigitalBrainBuilder`; it does not become an addressable root neuron.

The guard may mention these names only in its own forbidden-name array and in
`docs/superpowers/**`.

- [ ] **Step 2: Run the guard and verify red against stale docs**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~ArchitectureCutContracts|FullyQualifiedName~PackageBoundaryContracts" --logger "console;verbosity=minimal"
```

Expected: FAIL until stale documentation and surfaces are removed.

- [ ] **Step 3: Update architecture and package documentation**

Replace the storage example with:

```csharp
var brain = builder.AddDigitalBrain("brain");
brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());
```

State explicitly that local execution uses Azurite for the same durable profile and that the
compiled executable remains explicit because it contains the generated module catalog. Update neuron
examples to the approved non-`Async` capability names.

- [ ] **Step 4: Run focused and full foundation gates**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --logger "console;verbosity=minimal"
dotnet build DigitalBrain.slnx -c Release
npm --prefix docs test
npm --prefix docs run build
```

Expected: PASS.

- [ ] **Step 5: Verify the deletion budget**

```powershell
rg -n "AddBrain|WithAzureStorage|WithDevelopmentStores|BrainService|BrainModuleHosting|ConditionalWeakTable<IModule|ModuleSerializationMethod" src modules hosts samples tests docs --glob "!docs/superpowers/**"
```

Expected: no matches.

- [ ] **Step 6: Commit docs and guards**

```powershell
git add docs tests/DigitalBrain.Tests
git commit -m "docs: publish compiled module hosting path"
```

---

## Plan 1 completion gate

Run from a clean index while preserving the user's unstaged line-ending-only file:

```powershell
git status --short
dotnet build DigitalBrain.slnx -c Release
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build
```

Expected:

- all commands pass;
- `Directory.Packages.props` remains the only unrelated unstaged file;
- AppHost uses `AddDigitalBrain(name)` with no storage setup;
- generated typed capsules are the only module runtime catalog;
- no old hosting APIs or method-name conventions remain.
