# Adversarial Review — InoTestGenerator Source Generator

## Challenge Summary

**Overall risk assessment**: **LOW**

The `InoTestGenerator` source generator is extremely robust, resilient, and carefully designed. It successfully handles syntax/semantic errors in `.ino` specification files, missing/empty specifications, and duplicate scenario names without crashing or producing C# compilation errors. All generated test structures compile and execute cleanly in the real sandbox.

---

## Challenges & Stress Test Results

### [Low] Challenge 1: Syntax/Semantic Compiler Crash
- **Assumption challenged**: That syntax or semantic errors in `.ino` files could cause the Roslyn analyzer host to crash, or cause a C# build compilation failure, halting the developer inner loop.
- **Attack scenario**: A developer introduces broken syntax (`invalid_syntax_here!!`) or semantic errors (e.g. invalid FQN) into an `.ino` file listed under `AdditionalFiles`.
- **Blast radius**: If the generator crashed, it would stop the compilation of the entire test project, forcing the IDE/command-line builds to fail completely.
- **Verification & Result**:
  - We physically created `syntax_error.ino` and registered it as an `AdditionalFiles` input.
  - **Expected behavior**: The generator catches the compilation error cleanly, does not crash, generates a `Scenario_CompileError` test fact that uses the `CompileErrorScenarioKey` (`"<compile-error>"`), and fails only at runtime when tests are run.
  - **Actual behavior**: **PASS**. The C# compilation completed successfully with zero warnings and zero errors. Running `dotnet test` surfaced a single failing test fact:
    ```
    failed syntax_error.ino :: <compile error>
      syntax_error.ino: INO100 Unexpected character '!'. | INO100 Unexpected character '!'. | INO201 Expected a neuron FQN but found 'SyntaxErrorNeuron' (Ident)...
        at BrainOS.Domains.Onboarding.Tests.SyntaxErrorProjectionTests.Scenario_CompileError()
    ```
  - **Mitigation**: The design gracefully intercepts errors via `doc is null || bag.HasErrors` in the generator core, shifting failures from compile-time (blocking) to run-time (test-driven alerts).

---

### [Low] Challenge 2: Spec-First Scenario Emptiness
- **Assumption challenged**: That an `.ino` file containing zero scenarios would produce a silent pass, an empty generated file, or an analyzer warning.
- **Attack scenario**: A spec-first developer creates an `.ino` file containing only the neuron signature and dependencies, but has not yet written any scenarios.
- **Blast radius**: Without an explicit test alert, the project might build and pass in CI, leaving the feature completely untested without warning.
- **Verification & Result**:
  - We physically created `zero_scenarios.ino` with a namespace-qualified neuron FQN and zero scenarios.
  - **Expected behavior**: The generator detects `doc.Scenarios.Count == 0`, emits a `Scenario_NoScenarios` test fact with the `<no-scenarios>` sentinel, and fails at runtime indicating spec-first refuses to gate a scenerioless file.
  - **Actual behavior**: **PASS**. The generator emitted:
    ```csharp
    [Fact(DisplayName = "zero_scenarios.ino :: <no scenarios>")]
    public async Task Scenario_NoScenarios()
    {
        var catalog = GetCatalog();
        var report = await global::DigitalBrain.InoLang.TestRunner.InoScenarioProjection.RunAsync(
            @"...", "zero_scenarios.ino", "<no-scenarios>", "<no-scenarios>",
            catalog, global::Xunit.TestContext.Current.CancellationToken);
        global::Xunit.Assert.True(report.Passed, report.Message);
    }
    ```
    This ran and failed at runtime as expected:
    ```
    v3 §L6: zero_scenarios.ino has zero scenarios — spec-first refuses to gate it.
    ```

---

### [Low] Challenge 3: Scenario Name Collisions
- **Assumption challenged**: That multiple scenarios having duplicate display names inside an `.ino` file could cause duplicate method declarations in the emitted C# partial class, resulting in C# build compilation errors.
- **Attack scenario**: A developer copies and pastes a scenario or names two scenarios identically (e.g. `"Duplicate Scenario Name"`).
- **Blast radius**: The generated code has duplicate method signatures (e.g. `public async Task Scenario_DuplicateScenarioName()`), which results in a fatal C# compiler error CS0111, breaking the entire project build.
- **Verification & Result**:
  - We physically created `duplicate_names.ino` containing two scenarios both named `"Duplicate Scenario Name"`.
  - **Expected behavior**: The generator maps the C# method names to sequential indexes (`Scenario_0()`, `Scenario_1()`) ensuring uniqueness, while keeping the descriptive display name in the `[Fact(DisplayName = "...")]` attribute.
  - **Actual behavior**: **PASS**. The C# code compiled perfectly with zero warnings. The generated file contained:
    ```csharp
    [Fact(DisplayName = "duplicate_names.ino :: Duplicate Scenario Name")]
    public async Task Scenario_0() { ... }

    [Fact(DisplayName = "duplicate_names.ino :: Duplicate Scenario Name")]
    public async Task Scenario_1() { ... }
    ```
    Both tests ran successfully at runtime by index and passed!

---

## Stress Test Results Summary Matrix

| Scenario / Edge Case | Expected Behavior | Actual Behavior | Result |
|---|---|---|---|
| Malformed `.ino` (syntax/semantic errors) | Compile-time safety (no generator/MSBuild crash); Emits failing `Scenario_CompileError` test fact. | Complete C# compilation success; `Scenario_CompileError` test failed with detailed diagnostics. | **PASS** |
| Scenarioless `.ino` (zero scenarios) | Complete C# compilation success; Emits failing `<no-scenarios>` sentinel test. | Emitted `Scenario_NoScenarios` and failed at runtime showing spec-first refusal. | **PASS** |
| Duplicate Scenario Names | Emits unique C# method signatures (`Scenario_0`, `Scenario_1`); Preserves descriptive duplicate DisplayNames. | Complete C# compilation success; Dispatched by index, passed at runtime. | **PASS** |

---

## Unchallenged Areas

- **Full Bindings Check**: Semantic binding resolution is handled by InoLang at runtime. Out of scope for the source generator compile-time stage, which is lightweight by design.
