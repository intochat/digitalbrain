# Milestone 3 Design Plan: Roslyn Source Generator & Test-Driven Loop

This document outlines the synthesized design and implementation details for Milestone 3 of the DigitalBrain Production Readiness project.

## 1. Architecture & Design Decisions

### A. Roslyn Source Generator: `InoTestGenerator`
- **Assembly Location**: `BrainOS.Core.SourceGen` (`kernel/BrainOS.Core.SourceGen/`)
- **Pattern**: Incremental source generator (`IIncrementalGenerator`) that discovers test target classes decorated with `[InoTestTargetAttribute]`.
- **InoLang Code Inclusion**: Link the AST, Lexer, Parser, Diagnostics, and Text directories of `DigitalBrain.InoLang` directly into `BrainOS.Core.SourceGen.csproj` via `<Compile Include="..." />` to avoid runtime assembly loading issues inside the Roslyn analyzer host context.
- **Dynamic Attribute Injection**: During post-initialization, emit `DigitalBrain.InoLang.Testing.InoTestTargetAttribute` to make it globally available to target test projects.
- **Scenario Extraction**: For each class decorated with `[InoTestTarget("file.ino")]`, find the matching `AdditionalFiles` matching `"file.ino"`. Parse it using `Lexer` and `Parser` to extract:
  - Scenario list (names and ordering).
  - Diagnostics (if compilation fails, emit a failing xUnit test reporting compilation errors).
- **Code Generation**: Emit a partial class in the same namespace containing static `[Fact]` methods corresponding to each scenario.
  - Each `[Fact]` runs `InoScenarioProjection.RunAsync(...)` passing:
    - `rootPath`: Build-time absolute directory path of the `.ino` file.
    - `relativePath`: Base name of the `.ino` file.
    - `scenarioName`: Escaped name of the scenario.
    - `scenarioKey`: Key string format `"scenario:<index>"`.
    - `catalog`: Obtained by calling a static `GetCatalog()` method defined on the target partial class.
    - `TestContext.Current.CancellationToken`: xUnit v3 cancellation token.

### B. MSBuild Configuration Changes
- **`BrainOS.Core.SourceGen.csproj`**:
  - Add `<Compile Include="..\..\inolang\DigitalBrain.InoLang\**\*.cs" Link="InoLang\%(RecursiveDir)%(Filename)%(Extension)" Exclude="..\..\inolang\DigitalBrain.InoLang\obj\**;..\..\inolang\DigitalBrain.InoLang\bin\**" />`
- **`BrainOS.Domains.Onboarding.Tests.csproj`**:
  - Reference `BrainOS.Core.SourceGen` as an analyzer:
    `<ProjectReference Include="..\..\..\kernel\BrainOS.Core.SourceGen\BrainOS.Core.SourceGen.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`
  - Add `onboarding.ino` as an `AdditionalFiles`:
    `<AdditionalFiles Include="..\BrainOS.Domains.Onboarding\Onboarding\onboarding.ino" Link="Onboarding\onboarding.ino" />`
- **`BrainOS.Domains.Travel.Tests.csproj`**:
  - Reference `BrainOS.Core.SourceGen` as an analyzer:
    `<ProjectReference Include="..\..\..\kernel\BrainOS.Core.SourceGen\BrainOS.Core.SourceGen.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`
  - Add `TripPlanner.ino` as an `AdditionalFiles`:
    `<AdditionalFiles Include="..\BrainOS.Domains.Travel\TripRadar\TripPlanner.ino" Link="TripRadar\TripPlanner.ino" />`

### C. Test Refactoring
- **`OnboardingProjectionTests.cs`**:
  - Decorate with `[InoTestTarget("onboarding.ino")]`.
  - Mark class as `partial`.
  - Define `public static IContractCatalog GetCatalog() => SeamCatalog();`.
  - Remove manual `[Theory]` and `SeamScenarios()` discovery method.
- **`TripRadarProjectionTests.cs`**:
  - Decorate with `[InoTestTarget("TripPlanner.ino")]`.
  - Mark class as `partial`.
  - Define `public static IContractCatalog GetCatalog() => SeamCatalog();`.
  - Remove manual `[Theory]` and `SeamScenarios()` discovery method.

---

## 2. Step-by-Step Implementation and Verification Checklist

### Step 1: Source Generator Core Implementation
- Implement `InoTestGenerator.cs` in `BrainOS.Core.SourceGen/`.
- Update `BrainOS.Core.SourceGen.csproj` to include the direct `<Compile>` links to InoLang compiler classes.
- Verify clean building of `BrainOS.Core.SourceGen`.

### Step 2: Onboarding Test Setup & Migration
- Add the `BrainOS.Core.SourceGen` ProjectReference (as analyzer) and `onboarding.ino` as `AdditionalFiles` inside `BrainOS.Domains.Onboarding.Tests.csproj`.
- Update `OnboardingProjectionTests.cs` with `[InoTestTarget("onboarding.ino")]`, `partial`, and the static `GetCatalog()` method, removing the manual theory discovery.
- Build the test project. Verify that the generator runs and produces `<ClassNamespace>_OnboardingProjectionTests.InoTests.g.cs` in the intermediate directory containing the static `[Fact]` methods.
- Run tests using `dotnet test --filter "FullyQualifiedName~OnboardingProjectionTests"` and verify all generated facts pass.

### Step 3: Travel Test Setup & Migration
- Add the `BrainOS.Core.SourceGen` ProjectReference (as analyzer) and `TripPlanner.ino` as `AdditionalFiles` inside `BrainOS.Domains.Travel.Tests.csproj`.
- Update `TripRadarProjectionTests.cs` with `[InoTestTarget("TripPlanner.ino")]`, `partial`, and the static `GetCatalog()` method, removing the manual theory discovery.
- Build the test project. Verify that the generator runs and produces the generated test file.
- Run tests using `dotnet test --filter "FullyQualifiedName~TripRadarProjectionTests"` and verify all generated facts pass.

### Step 4: Full Verification Suite Run
- Run the full fast test suite: `dotnet test BrainOS.Fast.slnx`
- Verify that 100% of tests are passing.
- Run the full integration test suite if applicable: `dotnet test BrainOS.Integration.slnx` (or similar filter `Stage=e2e`).
