# Module structure unification

**Approved design:** Modules own contracts, implementations, optional hosting adapters, and tests. DigitalBrain is the foundational module. Applications own Silo, AppHost, and ServiceDefaults. UI becomes Flutter on disk and in C# project, package, namespace, and module names. The core package/assembly remains DigitalBrain; the host remains DigitalBrain.Silo.

**Execution:** In the requested checkout, on `codex/module-structure-unification`. Mechanical refactoring, without changing runtime contracts or adding packages. Existing tests may fail; production builds must be checked. No deployment or commits requested.

- [x] Move core and Aspire libraries to `src/Modules/DigitalBrain/{Contracts,DigitalBrain,Mcp,Aspire,Aspire.Hosting}` and application projects to `src/Applications/DigitalBrain/{Silo,AppHost,ServiceDefaults}` using git moves.
- [x] Move UI to `src/Modules/Flutter/{Contracts,Flutter,Aspire.Hosting,app}`. Rename C# UI project identities, namespaces, and UIModule. Preserve wire names, HTTP paths, Dart package identities, and configuration keys.
- [x] Rebase project references using old/new absolute locations; update solution groups, CI, Docker, Aspire configuration, runtime path discovery, and current documentation.
- [x] Create module-owned Tests projects referencing DigitalBrain.Testing. Move standalone facts and their fixtures. Move module BDD sources beside modules, with the existing system runner linking scenarios that require its composed harness. Keep shared runtime SDK free of feature-module dependencies.
- [x] Build production projects and the solution; correct relocation/compiler errors. Run suitable isolated module tests, inspect discovery/remaining test failures, and restart AppHost to inspect resource hierarchy and Flutter path resolution.
- [x] Review all references, package identities, solution membership, and git diff. Report verification and any remaining test limitations.

## Verification

- Solution build: 0 warnings, 0 errors. All 54 project paths and references resolve.
- Runtime tests: 12 passed. Flutter C# tests: 37 passed. Coding tests: 120 passed, 2 opt-in tests skipped. Hosting hierarchy: 2 passed.
- System BDD discovery: 151 tests; full BDD execution not run. Scenario-only module test projects currently discover zero standalone tests (MTP exit code 8 when run alone); their scenarios run through the system runner.
- Aspire MCP confirmed Kernel/storage hierarchy and new Silo/Flutter working directories. Silo healthy; Flutter build cache regenerated with flutter clean, native Windows build succeeded, and executable launched.
- Core package/assembly DigitalBrain, packable=true; Silo assembly DigitalBrain.Silo, packable=false; Flutter package/assembly DigitalBrain.Modules.Flutter.
- AppHost relocation changes local Aspire volume namespace; old volumes were left intact, without migration.
- Automatic approval review blocked deletion of obsolete ignored bin/obj directories under the old source locations; those generated artifacts remain.
- Read-only review findings (external Reqnroll feature discovery and a leftover test scaffold) resolved and rechecked.
