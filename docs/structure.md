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
```

Other modules use `{Contracts,<Name>}` and add `Aspire.Hosting` when needed. Excel and Time do not need that adapter. Flutter's Dart workspace lives in `app/`, with its existing `core`, `ui`, and `shell` packages.

Contracts expose capabilities using the DigitalBrain neuron/Orleans model. Implementations reference the contracts they use; applications choose and host implementations. Application service defaults are registered by the silo, not by the reusable DigitalBrain Aspire package.

Configuration types live in a `Configuration/` folder within their owning project. Hosting options belong in `Aspire.Hosting/Configuration/`; runtime options belong in the module implementation's `Configuration/` folder. Keep options in the project's public namespace so folder organization does not change the configuration API. In module composition lambdas, use `module` for the module builder and `options` for its configuration object. See [configuration.md](configuration.md) for ownership, precedence, and examples.

IntoChat is the product built on the DigitalBrain framework. Its application projects are `IntoChat`, `IntoChat.AppHost`, `IntoChat.ServiceDefaults`; the Aspire process resource is `IntoChat`. The core NuGet package and assembly remain `DigitalBrain`, while the non-packable product executable is `IntoChat.dll`. Flutter C# projects use `DigitalBrain.Modules.Flutter`, `.Contracts`, and `.Aspire.Hosting`, with namespace `DigitalBrain.Flutter` and module type `FlutterModule`. Existing UI wire aliases, routes, configuration keys, and Dart package names remain unchanged.

## Verification

The shared `DigitalBrain.Testing` framework and simple test projects are available for every module and IntoChat. A small runtime test verifies that `BrainSimulation` starts and restarts. The other test projects are empty starting points; the old suites and production fake providers remain removed. See [the testing guide](../src/Testing/README.md). Enabled data modules require real providers and valid connections.

```powershell
dotnet build DigitalBrain.slnx
aspire start --apphost src/Applications/IntoChat/AppHost/IntoChat.AppHost.csproj --non-interactive
```

## Local state after relocation

Aspire's default volume names include a hash of the AppHost location. Relocating AppHost selects a new local volume namespace; existing volumes are not removed or migrated. Reusing old local data requires an explicit volume migration or binding decision. This does not change production connection-string keys.

Flutter Windows CMake caches contain absolute source paths. Run `flutter clean` from `src/Modules/Flutter/app/shell` if a previously built checkout reports a CMake source-directory mismatch after relocation.
