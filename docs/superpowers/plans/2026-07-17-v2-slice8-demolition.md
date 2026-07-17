# DigitalBrain v2 — Slice 8: Demolition

> Final slice. The original acceptance criterion of the whole v2 effort: delete the v1 tree, make the v2 stack the only stack, record the deletion metrics.

**Goal:** Remove every v1 project and directory, rewrite the AppHost to orchestrate only the v2 resources, keep the exact-root test gate green (now just the v2 tests), re-prove the live rail on the v2-only topology, and record the LOC reduction.

## The boundary (measured)

**Keep (v2 + shared infra):**
- `kernel/Brain.Contracts`, `Brain.Kernel`, `Brain.Client`
- `modules/Brain.Modules.*` (Sdk, Workspace, Ai, Web, Connections, Google, Salesforce, Behaviors)
- `edge/Brain.Mcp`, `Brain.UiGateway`
- `hosts/Brain.Kernel.Host`, `hosts/DigitalBrain.ServiceDefaults` (generic Aspire infra — the ONLY DigitalBrain.* the v2 stack references), `hosts/DigitalBrain.AppHost` (REWRITTEN to v2-only)
- `behaviors/smoke`, `behaviors/inbox-brief`
- `tests/Brain.KernelTests`, `tests/Brain.ConformanceTests`
- `workspace/` (the v2 Flutter app — becomes THE client)

**Delete (v1 trash):**
- `src/DigitalBrain.*` (Features.Sdk, Features.Testing, Kernel, Kernel.Contracts, Mcp) — ~26.8k
- `integrations/DigitalBrain.Integrations.*` (6 projects) — ~4.6k
- `hosts/DigitalBrain.FeatureBuilder`, `FeatureHost`, `RuntimeHost` — v1 hosts
- `features/` (EmailSummarizer, EnrichSalesforce + Tests) — ~0.45k
- `tests/DigitalBrain.*` (E2ETests, AppHostTests, IntegrationContractTests, OrleansTests, UnitTests) — ~23.5k, all bound to deleted machinery
- `shared/`, `deploy/` — v1
- `app/` (v1 Flutter: RFW, ui_kit, generated grpc, studio) — ~38k Dart

## Stages (each verified before the next)

1. **Rewrite the AppHost** — new minimal `AppHost.cs` (Ollama + brain-kernel + brain-mcp + brain-ui; brain-kernel gets the Ollama endpoint env; mcp/ui WaitFor kernel). Strip `DigitalBrain.AppHost.csproj` to reference only the three v2 host projects + `CommunityToolkit.Aspire.Hosting.Ollama`. Delete `Composition/` and v1 appsettings/secrets. Build the AppHost alone → compiles against v2 only.
2. **Strip the solution** — remove every `DigitalBrain.*` v1 project entry from `Brain.slnx` (src, integrations, FeatureBuilder/FeatureHost/RuntimeHost, features, tests/DigitalBrain.*, deploy, app/Flutter.proj). Keep the v2 + ServiceDefaults + AppHost.
3. **Delete the v1 directories** — `git rm -r` the delete-set above.
4. **Verify** — `dotnet build` clean (v2 only); exact root `dotnet test --logger "console;verbosity=minimal"` = Brain.KernelTests + Brain.ConformanceTests green, zero skips; `workspace` flutter analyze + test green.
5. **Add `workspace/` to the solution** as the client (replace the deleted `app/Flutter.proj`). Wiring the Flutter app into the AppHost dev graph is a carry unless trivial.
6. **Live proof** — `aspire run` the v2-only topology; MCP `neuron_invoke` chat + the behavior rail (propose→approve→run under identity, granted works / ungranted refused) still pass end-to-end; the Dart gateway live test still green.
7. **Record deletion metrics** — production C# and Dart before/after, percentage reduction, per the spec §19 acceptance criterion.

## Constraints

Zero comments · CPM · the v2 root test gate stays green zero skips · no v2 behavior change (deletion only, plus the AppHost rewrite) · prune obviously-orphaned `Directory.Packages.props` entries if clean, else leave (unused PackageVersion is harmless).
