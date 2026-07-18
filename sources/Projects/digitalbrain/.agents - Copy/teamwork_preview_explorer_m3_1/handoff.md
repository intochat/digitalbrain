# InoLang Spec & Test Steps Mapping Analysis Handoff

## 1. Observation
This read-only investigation explored the `DigitalBrain.InoLang` core library and its associated test runner to map the loading, compilation, and execution lifecycles of InoLang (`.ino`) scenarios, and designed the structured C# mapping patterns for `.ino` test steps.

### File Paths & Code References Observed

#### A. Loading and Discovering `.ino` Files
- **Path**: `inolang/DigitalBrain.InoLang.TestRunner/InoFileDiscovery.cs`
  - Purpose: Recursively scans a directory for `*.ino` files, ordinally sorting them and excluding directories like `bin`, `obj`, `Generated`, `.git`, and `node_modules` (lines 5-9).
- **Path**: `inolang/DigitalBrain.InoLang.TestRunner/InoScenarioProjection.cs`
  - Purpose: Connects discovered `.ino` files to xUnit-v3. It projects each `.ino` scenario into a distinct `TheoryDataRow<string, string, string>` (lines 31-51) carrying `(relativePath, scenarioName, scenarioKey)`.
  - Dispatch: Uses a key format `scenario:<index>` to address individual scenarios safely without name collision, falling back to synthetic sentinel rows (`<compile-error>`, `<no-scenarios>`, `<missing-root>`) if the file fails basic compile/structural tests.

#### B. Parsing
- **Path**: `inolang/DigitalBrain.InoLang/Ast/Scenarios.cs`
  - AST Representation of Scenario and steps (lines 5-14):
    ```csharp
    public abstract record ScenarioStep(SourceSpan Span);

    public sealed record GivenSeamReturns(string Port, Expr Value, SourceSpan Span) : ScenarioStep(Span);
    public sealed record GivenPredicate(CallExpr Subject, string Value, SourceSpan Span) : ScenarioStep(Span);
    public sealed record WhenInject(string Port, IReadOnlyList<NamedArg> Args, SourceSpan Span) : ScenarioStep(Span);
    public sealed record ThenSignalEmitted(string Port, string? WithField, Expr? WithValue, SourceSpan Span) : ScenarioStep(Span);
    public sealed record ThenResourceHas(string Port, Expr Value, SourceSpan Span) : ScenarioStep(Span);
    public sealed record ThenCounter(string Counter, long Value, SourceSpan Span) : ScenarioStep(Span);

    public sealed record ScenarioDecl(string Name, IReadOnlyList<ScenarioStep> Steps, SourceSpan Span);
    ```
- **Path**: `inolang/DigitalBrain.InoLang/Parsing/Parser.cs`
  - Method `ParseScenario()` (lines 462-472) parses the `scenario "<name>"` header followed by an indented list of steps parsed via `ParseScenarioStep()` (lines 483-548).

#### C. Linking
- **Path**: `inolang/DigitalBrain.InoLang/Linking/Linker.cs`
  - Matches the FQNs declared via `using` statements against an `IContractCatalog` to ensure type-safety (lines 13-23).
  - Method `CheckScenarioStep()` (lines 190-228) validates that every port referenced in the scenario exists under the correct signature, and that its fields are matched.

#### D. Lowering
- **Path**: `inolang/DigitalBrain.InoLang/Planning/Lowering.cs`
  - Method `Lower()` (lines 8-52) converts the verified AST representation `LinkedNeuron` into an `ExecutionPlan`.
  - Field Canonicalization (lines 54-61): Canonicalizes the casing of all user-written field names in statements, expressions, and scenario steps using the catalog schema to prevent silent runtime lookup mismatches.

#### E. Execution
- **Path**: `inolang/DigitalBrain.InoLang/Testing/ScenarioRunner.cs`
  - Method `RunOneAsync()` executes a scenario on the `Interpreter` in three steps:
    1. **Given**: Pins seam returns inside a `StubSeamHost` (`stub.SeamReturns[step.Port] = Literal(step.Value)`) and predicate returns (`stub.PredicateValues[step.Subject.Builtin] = step.Value`) (lines 23-27).
    2. **When**: Injects the synapse/signal with compiled literal arguments and awaits `Interpreter.RunAsync` (lines 29-41).
    3. **Then/And**: Asserts against the produced `ActivationResult`'s `EmittedSignals`, `SavedResources`, and `Counters` (lines 43-70).

---

## 2. Logic Chain
We trace the step-by-step lifecycle of a `.ino` scenario from disk discovery to running interpreter:

1. **Discovery & Projection**: `InoScenarioProjection.Discover(root)` runs `InoFileDiscovery` to locate files. For each valid `.ino` file, it parses the document and registers each scenario as an addressable xUnit theory row with a unique `scenario:<index>` key.
2. **Compilation**: When xUnit invokes a scenario test row, `InoScenarioProjection.RunAsync()` calls `InoCompiler.Compile()`. This triggers:
   - **Lexing**: `Lexer` converts string contents into tokens.
   - **Parsing**: `Parser` builds the `NeuronDoc` AST, including all `ScenarioDecl` blocks.
   - **Linking**: `Linker` queries `IContractCatalog` (implemented by `MapCatalog` in tests or assembly catalogs in production) to link type information.
   - **Lowering**: `Lowering` canonicalizes field-name casings and compiles handlers and scenarios into an optimized `ExecutionPlan`.
3. **Execution**: `ScenarioRunner` runs the scenario against the plan using an in-memory `Interpreter` sandbox. The runner populates `StubSeamHost` from `given` statements, fires the trigger specified in `when`, and then verifies `then`/`and` outcomes against `ActivationResult`.

---

## 3. Caveats
- **Interpreter Context Limits**: The current interpreter runtime translates expressions dynamically using `EvalToString` (lines 399-422 in `Interpreter.cs`). Highly complex nested expressions or functions not explicitly listed as built-ins (`is-successful-spawn`, `get-token-from-spawn`, `is-azure`) default to empty string outcomes.
- **Scenario Steps Order**: The `ScenarioRunner` assumes a logical flow of `Given` -> `When` -> `Then`. A scenario containing multiple `WhenInject` blocks or mixed order will run the first encountered `WhenInject` trigger step and assert the outcomes of all steps based on that single execution.

---

## 4. Conclusion
The InoLang specs and `.ino` scenarios are elegantly designed to enforce a **spec-first gating constraint** (L6): no neuron can be promoted to production unless its scenarios pass. 

To bridge `.ino` specifications and C# integration tests seamlessly, the six `.ino` test step AST nodes map cleanly to class/method-level `[Binding]` step definitions using **Reqnroll** (the C# SpecFlow successor). 

Here is the exact structural mapping from InoLang AST steps to Reqnroll C# steps:

### 1. `given <seam> returns "<val>"`
- **AST Node**: `GivenSeamReturns(string Port, Expr Value, SourceSpan Span)`
- **Reqnroll C# Mapping Pattern**:
  ```csharp
  [Given(@"the seam ""([^""]*)"" returns ""([^""]*)""")]
  public void GivenSeamReturns(string seamName, string returnValue)
  {
      // Setup the stub value on the test harness or in-memory seam registry
      _brainOS.SetupSeamResponse(seamName, returnValue);
  }
  ```

### 2. `given <predicate> is "<val>"`
- **AST Node**: `GivenPredicate(CallExpr Subject, string Value, SourceSpan Span)`
- **Reqnroll C# Mapping Pattern**:
  ```csharp
  [Given(@"the predicate ""([^""]*)"" with argument ""([^""]*)"" evaluates to ""([^""]*)""")]
  public void GivenPredicateEvaluatesTo(string predicateName, string argument, string expectedValue)
  {
      // Registers the mock predicate outcome in the runtime seam engine
      _brainOS.SetupPredicateValue(predicateName, argument, expectedValue);
  }
  ```

### 3. `when synapse <port>(<field>: "<val>", ...)`
- **AST Node**: `WhenInject(string Port, IReadOnlyList<NamedArg> Args, SourceSpan Span)`
- **Reqnroll C# Mapping Pattern**:
  ```csharp
  [When(@"synapse ""([^""]*)"" is injected with:")]
  public async Task WhenSynapseInjected(string portName, Table table)
  {
      _correlationId = Guid.NewGuid();
      var payload = ParseTableToDictionary(table);
      // Construct and dispatch the synapse contract through the TestBrainOS bus
      await _brainOS.EmitInboundSynapse(portName, payload, _correlationId);
  }
  ```

### 4. `then signal <port> emitted with <field> == "<val>"`
- **AST Node**: `ThenSignalEmitted(string Port, string? WithField, Expr? WithValue, SourceSpan Span)`
- **Reqnroll C# Mapping Pattern**:
  ```csharp
  [Then(@"signal ""([^""]*)"" should be emitted")]
  public async Task ThenSignalEmitted(string portName)
  {
      // Await signal event on the correlation stream with a timeout
      _lastSignal = await _brainOS.AwaitSignal(portName, _correlationId, TimeSpan.FromSeconds(10));
      _lastSignal.Should().NotBeNull();
  }

  [Then(@"the emitted signal should have field ""([^""]*)"" equal to ""([^""]*)""")]
  public void ThenSignalFieldMatches(string fieldName, string expectedValue)
  {
      _lastSignal.Should().NotBeNull();
      _lastSignal.Fields[fieldName].Should().Be(expectedValue);
  }
  ```

### 5. `then resource <port> has "<val>"`
- **AST Node**: `ThenResourceHas(string Port, Expr Value, SourceSpan Span)`
- **Reqnroll C# Mapping Pattern**:
  ```csharp
  [Then(@"resource ""([^""]*)"" should have value ""([^""]*)""")]
  public async Task ThenResourceHasValue(string resourcePort, string expectedValue)
  {
      // Verify state persistence in the mock repository or local database
      var actualValue = await _brainOS.GetResourceState(resourcePort);
      actualValue.Should().Be(expectedValue);
  }
  ```

### 6. `then counter <name> == <val>`
- **AST Node**: `ThenCounter(string Counter, long Value, SourceSpan Span)`
- **Reqnroll C# Mapping Pattern**:
  ```csharp
  [Then(@"counter ""([^""]*)"" should equal (\d+)")]
  public void ThenCounterEquals(string counterName, long expectedValue)
  {
      // Query telemetry engine inside the test harness
      var actualValue = _brainOS.GetCounterValue(counterName);
      actualValue.Should().Be(expectedValue);
  }
  ```

---

## 5. Verification Method
To independently verify the observations, trace logic, and run tests:

1. **Verify Compiler and Test Runner Suites**:
   - Run the fast tests solution:
     ```powershell
     dotnet test BrainOS.Fast.slnx
     ```
     This executes both `DigitalBrain.InoLang.Tests` and `DigitalBrain.InoLang.TestRunner.Tests`.
2. **Inspect the Core Specification files**:
   - View `inolang/DigitalBrain.InoLang/Ast/Scenarios.cs` to verify step declarations.
   - View `inolang/DigitalBrain.InoLang/Testing/ScenarioRunner.cs` to confirm how scenarios are interpreter-run and verified.
   - View `samples/Boot/BrainOS.ino` to see the canonical Genesis `.ino` file containing a real scenario.
   - View `samples/BrainOS.Domains.Travel/BrainOS.Domains.Travel/TripRadar/TripRadarOrchestrator.Steps.cs` to check real Reqnroll C# steps.
