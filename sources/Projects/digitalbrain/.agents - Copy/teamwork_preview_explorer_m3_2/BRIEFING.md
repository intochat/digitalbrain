# BRIEFING — 2026-05-23T02:45:33+02:00

## Mission
Design the Roslyn Source Generator to translate `.ino` files (from AdditionalFiles) into executable C# test steps using `DigitalBrain.InoLang` and `BrainOS.Core.SourceGen`.

## 🔒 My Identity
- Archetype: Explorer
- Roles: Milestone 3 Explorer 2 (Source Gen Designer)
- Working directory: e:/digitalbrain/.agents/teamwork_preview_explorer_m3_2
- Original parent: fbd28d5f-aa1c-49e1-ad34-d744ad0d92fd
- Milestone: Milestone 3: Source Generator & Test-Driven Loop

## 🔒 Key Constraints
- Read-only investigation — do NOT implement
- Do NOT write or modify code (except files in own working directory)
- Update progress.md periodically for liveness
- Once done, send a message to orchestrator with the path to handoff.md

## Current Parent
- Conversation ID: fbd28d5f-aa1c-49e1-ad34-d744ad0d92fd
- Updated: 2026-05-23T02:45:33+02:00

## Investigation State
- **Explored paths**:
  - `kernel/BrainOS.Core.SourceGen/` (`NeuronGenerator.cs` and `BrainOS.Core.SourceGen.csproj`)
  - `inolang/DigitalBrain.InoLang/` (`InoCompiler.cs`, `Parsing/Parser.cs`, `Ast/Scenarios.cs`, etc.)
  - `inolang/DigitalBrain.InoLang.TestRunner/` (`InoScenarioProjection.cs`, `InoTestRunner.cs`)
  - `inolang/DigitalBrain.InoLang.TestRunner.Tests/` (`CanonicalBootScenarioTests.cs`, `InoScenarioProjectionTests.cs`)
  - `samples/BrainOS.Domains.Onboarding/BrainOS.Domains.Onboarding.Tests/OnboardingProjectionTests.cs`
  - `docs/v3/2026-05-21-inolang-roslyn-meta-language.md` (Metadata and new syntax specification)
- **Key findings**:
  - Incremental Roslyn source generators (like `NeuronGenerator`) can easily process `AdditionalFiles` via `context.AdditionalTextsProvider`.
  - `DigitalBrain.InoLang`'s parser `Parser.ParseDocument()` compiles source text into AST (`NeuronDoc`), exposing a strongly-typed `Scenarios` list without requiring a catalog (catalog is only used in linking/lowering).
  - The project currently relies on xUnit v3 dynamic theories (`InoScenarioProjection.Discover`) loaded at runtime via IO.
  - Generating static `[Fact]` methods per scenario makes tests compile-time visible, IDE-navigable, and faster, while keeping the full execution plan logic inside `InoScenarioProjection.RunAsync(...)`.
- **Unexplored areas**:
  - Packaging structure of `DigitalBrain.InoLang` inside Roslyn compiler context.
  - Multi-package catalog loading integration with MSBuild.

## Key Decisions Made
- Designed a dual-option `InoTestGenerator` that reads `*.ino` files from `AdditionalFiles` and generates xUnit-compatible scenario runner classes.
- Used `[InoTestTarget("filename.ino")]` attribute matching to link hand-written test classes (with custom `MapCatalog` definitions) to their `.ino` files, allowing seamless transition from existing dynamic runtime discovery to static generated methods.

## Artifact Index
- e:/digitalbrain/.agents/teamwork_preview_explorer_m3_2/original_prompt.md — Recording of original prompt
- e:/digitalbrain/.agents/teamwork_preview_explorer_m3_2/BRIEFING.md — Working memory index
- e:/digitalbrain/.agents/teamwork_preview_explorer_m3_2/progress.md — Liveness progress report
- e:/digitalbrain/.agents/teamwork_preview_explorer_m3_2/handoff.md — Detailed structured design report

