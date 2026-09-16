# Module configuration unification implementation plan

**Goal:** Apply the approved typed-options design and configuration folder conventions across all modules, framework, and IntoChat composition.

**Architecture:** Modules own public runtime option POCOs in their implementation project's Configuration folder, register binding/validation using Microsoft.Extensions.Options, and consume options in runtime services. Aspire hosting projects own graph-construction options in Configuration folders and explicitly project runtime configuration across processes. Contracts remain capability interfaces.

**Spec:** The approved design in this conversation: module-owned options, application-owned values, stable fluent composition, module/options lambda names, and Configuration folders in both hosting and runtime projects.

## Constraints

- Preserve configuration keys, defaults, endpoint security checks, optional OAuth setup, fakes, connection strings, and deferred Aspire parameter/endpoint references.
- Keep public namespaces stable; do not invent empty options for configuration-free modules.
- Do not build a temporary service provider for registration. Startup graph choices may bind a typed snapshot; runtime services consume IOptions<T>.
- Keep existing IConfiguration overloads when tests or external callers use them, forwarding to typed settings rather than duplicating parsing.
- Preserve existing uncommitted work, including the digitalBrain variable rename in AppHost. No commits or application restarts are required.
- User deprioritized tests; compile the solution and run focused configuration checks where useful, not the whole integration suite.

## Tasks

- [x] AI: inventory provider/telemetry/default-model/voice/search/workspace settings; introduce typed configuration and hosting configuration folders; preserve dynamic provider/model maps.
- [x] Google, Salesforce, Microsoft: typed OAuth, endpoints, repository and Aspire settings with module-owned registration; preserve lazy credential checks and URI allowlists; organize hosting configuration.
- [x] Memory, ClickHouse, Supabase, Coding, Time: typed runtime settings, binding/validation and consumers; organize all hosting settings; retain provider/fake selection behavior.
- [x] Framework, Flutter, Excel and IntoChat: organize existing configuration, add typed options only for actual settings, standardize all module lambdas, and document ownership/precedence.
- [x] Verify solution compilation, inspect effective options and security/provider compatibility, review changes, update documentation and this checklist.

## Verification

Build with `dotnet build DigitalBrain.slnx --no-restore -p:CodeGraphRefresh=false`; restore only if dependency changes require it. Inspect direct configuration access after migration to ensure remaining reads are intentional integration boundaries. Run `git diff --check`. Configuration validation must not require credentials for disabled capabilities or fake providers.

## Execution notes

Work continues in the existing codex/module-structure-unification workspace to preserve the user's active changes. Independent module groups can be edited concurrently; the coordinator owns shared framework files, project-wide build settings, IntoChat and documentation. Agents must not commit, change other groups, or run concurrent builds.

Review identified and corrected options precedence for derived connections and legacy AI models, consistency between auth/session/CORS consumers, and late provider changes. The user subsequently authorized Aspire stop/start for live verification. Debug and Release builds passed with zero warnings/errors; 17 application tests and the first six options regression tests passed. Final review follow-ups cover custom connections without default connections and default-model resolution.

Final verification: all 12 options regression tests (AI 1, ClickHouse 5, Memory 4, Supabase 2) and 17 IntoChat tests pass. The final Debug build succeeds; it reports six transient file-copy retry warnings while the stopped app finishes releasing assemblies, with zero errors. Aspire subsequently rebuilds/starts successfully, and `aspire wait` confirms IntoChat and Flutter healthy. Scoped re-review confirms all three final findings resolved. `git diff --check` passes. Application restored to running state; changes remain uncommitted.
