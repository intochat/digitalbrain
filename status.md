# CoreV2 product migration status

Updated: 2026-08-14

## Outcome

CoreV2 is becoming the only compiled product graph. The target local startup is one `aspire start` command that brings up storage, an Orleans runtime silo, the ProductHost client, and the Flutter desktop client with module-owned Aspire projections.

## Current truth

- The CoreV2 AppHost is tracked under `src/CoreV2` and the repository Aspire entry point targets it.
- CoreV2 build and tests are green on .NET 11; obsolete .NET 8/9 MTP bridge flags were removed from the test projects.
- `Brain.ServiceDefaults` now provides shared health checks, service discovery, HTTP resilience, and OpenTelemetry conventions.
- CoreV2 domain and proof tests exist, but the proof runtime is still hosted behind one in-memory Orleans test grain rather than production grains.
- `DigitalBrain.ProductHost` is not yet a production Orleans client and does not yet expose the complete product protocol.
- The V1 AppHost, hosting extensions, module system, and Flutter client are reference implementations only; they are not part of the CoreV2 compiled graph.
- The rejected ProductHost-local persistence slice remains reverted. Core state must live behind Orleans grains in the runtime host.
- The earlier large cutover plan is superseded by the small, dependency-ordered migration plan in `docs/superpowers/plans/2026-08-14-corev2-aspire-hosting-spine.md`.

## Migration order

1. Establish the tracked CoreV2 AppHost and green build/test baseline.
2. Add shared service defaults.
3. Add AppHost-side `Brain.Aspire.Hosting` resource and module projection abstractions.
4. Add runtime-side `Brain.Aspire` Orleans silo/client hosting extensions.
5. Add a dedicated CoreV2 runtime host and convert ProductHost into an Orleans client process.
6. Prove the storage -> runtime -> ProductHost graph with `aspire start`.
7. Add module-owned Flutter Aspire hosting and a minimal CoreV2 Flutter shell.
8. Migrate durable product operations, protocol endpoints, module UI, and remaining modules in independently verified slices.
9. Remove V1 from the product path only after parity and live cutover verification.

## Active slice

Adding the AppHost-side `Brain.Aspire.Hosting` resource model and module projection builder.

## Definition of done

- `dotnet build DigitalBrain.slnx -c Release` succeeds.
- Every CoreV2 test project passes through the native .NET 11 test coordinator.
- `aspire start --isolated --non-interactive` reaches healthy storage, runtime, ProductHost, and Flutter resources.
- Flutter can discover and invoke module operations and observe durable activity through ProductHost.
- No compiled CoreV2 project references V1 source roots.
