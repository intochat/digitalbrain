# Repository structure

The disk and `DigitalBrain.slnx` group projects by ownership.

```text
src/
  Modules/
    DigitalBrain/{Contracts,DigitalBrain,Mcp,Aspire,Aspire.Hosting,Tests}
    Flutter/{Contracts,Flutter,Aspire.Hosting,Tests,app}
    AI/{Contracts,AI,Aspire.Hosting,Tests}
    Salesforce/{Contracts,Salesforce,Aspire.Hosting,Tests}
    ...
  Applications/
    IntoChat/{IntoChat,AppHost,ServiceDefaults,Tests}
  Testing/
    DigitalBrain.Testing/
    Module.Tests.props
tests/
  DigitalBrain.Tests/
```

Other modules use `{Contracts,<Name>,Tests}` and add `Aspire.Hosting` when needed. Excel and Time do not need that adapter. Flutter's Dart workspace lives in `app/`, with its existing `core`, `ui`, and `shell` packages and package-local Dart tests.

Contracts expose capabilities using the DigitalBrain neuron/Orleans model. Implementations reference the contracts they use; applications choose and host implementations. Application service defaults are registered by the silo, not by the reusable DigitalBrain Aspire package.

IntoChat is the product built on the DigitalBrain framework. Its application projects are `IntoChat`, `IntoChat.AppHost`, `IntoChat.ServiceDefaults`, and `IntoChat.Tests`; the Aspire process resource is `IntoChat`. The core NuGet package and assembly remain `DigitalBrain`, while the non-packable product executable is `IntoChat.dll`. Flutter C# projects use `DigitalBrain.Modules.Flutter`, `.Contracts`, and `.Aspire.Hosting`, with namespace `DigitalBrain.Flutter` and module type `FlutterModule`. Existing UI wire aliases, routes, configuration keys, and Dart package names remain unchanged.

## Tests

Each module's `Tests` project references the shared `DigitalBrain.Testing` runtime harness through `src/Testing/Module.Tests.props`. The shared SDK depends on the foundational runtime, not feature modules. Module fixtures live beside their module's tests. Application composition and HTTP tests live in `src/Applications/IntoChat/Tests`.

The existing BDD suite uses a composed, multi-module `BrainWorld`. Module-specific bindings and feature files are owned by the module's `Tests/Scenarios` folder but linked into the system runner at `tests/DigitalBrain.Tests`. Explicit `ReqnrollFeatureFile` items preserve discovery on a fresh checkout. Cross-module integration features and their scenario harness remain with that runner.

Some module projects currently have only scenarios or fixtures, and therefore discover no standalone xUnit tests; run their existing scenarios through the system runner. Do not add placeholder assertions solely to populate those projects. Shared AI/Coding fixtures used by application tests are linked from their owning module rather than copied into the runtime SDK.

```powershell
dotnet build DigitalBrain.slnx
dotnet test --project src/Modules/Flutter/Tests/DigitalBrain.Modules.Flutter.Tests.csproj
dotnet test --project tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj
aspire start --apphost src/Applications/IntoChat/AppHost/IntoChat.AppHost.csproj --non-interactive
```

## Local state after relocation

Aspire's default volume names include a hash of the AppHost location. Relocating AppHost selects a new local volume namespace; existing volumes are not removed or migrated. Reusing old local data requires an explicit volume migration or binding decision. This does not change production connection-string keys.

Flutter Windows CMake caches contain absolute source paths. Run `flutter clean` from `src/Modules/Flutter/app/shell` if a previously built checkout reports a CMake source-directory mismatch after relocation.
