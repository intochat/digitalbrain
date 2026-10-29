# MODULE_NAME

A DigitalBrain module.

- `MODULE_NAME/MODULE_CLASSModule.cs` declares the module and its manifest: what the host must supply (`Needs`) and the options it binds (`Settings`).
- `MODULE_NAME/Contracts/` holds the public interface other code calls.
- `MODULE_NAME/src/` holds the module's internal neurons.
- `tests/MODULE_NAME.Tests.Unit/` checks the manifest.

```
dotnet build
dotnet test --project tests/MODULE_NAME.Tests.Unit/MODULE_NAME.Tests.Unit.csproj
```

The build runs the DigitalBrain analyzers: a module without `[Module]` or a static `Manifest` fails with DB0001.
The tests use the `DigitalBrain.Testing` fakes. TestOS arrives in a later DigitalBrain release.
