# Module options: documentation findings

Researched 2026-09-17. Context7 library resolution was attempted first but returned a monthly-quota error. Microsoft Learn search and full-page fetch supplied the Options documentation; official Microsoft Learn/Aspire documentation supplied the orchestration boundary. This is a design recommendation, not an implemented API.

## Documented .NET behavior

| API | Appropriate use |
| --- | --- |
| `IOptions<T>` | Singleton wrapper, injectable into any lifetime; cached configuration without reload or named-option access. |
| `IOptionsMonitor<T>` | Singleton wrapper with current values, named options and change notifications; suitable for singleton consumers that deliberately respond to updates. |
| `IOptionsSnapshot<T>` | Scoped wrapper; values cached within the scope/request and recomputed for a new scope; cannot be injected into a singleton. |
| `OptionsBuilder<T>` | Registration/configuration API obtained from `AddOptions<T>`; supports binding, configuration delegates and validation. It is not the object consumers read. |

These semantics and the requirement for a provider that supports updated values are documented in [Microsoft Learn: Options pattern](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#options-interfaces).

`Configure<T>(section)` registers binding. `AddOptions<T>().Bind(section)` exposes the richer fluent registration surface; `Configure(...)` can apply programmatic settings, and `PostConfigure(...)` runs after configuration. `Validate(...)`, `ValidateDataAnnotations()` or `IValidateOptions<T>` supply rules; `ValidateOnStart()` requests startup validation rather than waiting for first access. Validation is also performed on reload. See [registration and validation](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#options-validation) and [validation timing](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0#run-options-validation-when-the-app-starts-with-validateonstart).

`BindConfiguration(sectionPath)` resolves `IConfiguration` from the same DI container. It does not reach into another process. See [BindConfiguration API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.optionsbuilderconfigurationextensions.bindconfiguration).

## Aspire boundary and recommendation

AppHost describes and starts the distributed application; its configuration concerns the AppHost. Child resources receive explicit configuration, for example an AppHost parameter passed through `WithEnvironment`. References convey service/connection information. See [AppHost configuration](https://learn.microsoft.com/en-us/dotnet/aspire/app-host/configuration), [external parameters](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/external-parameters), and [hosting versus client integrations](https://learn.microsoft.com/dotnet/aspire/fundamentals/integrations-overview).

**Proposal:** retain plain, eagerly configured hosting option objects for module choices that create containers, volumes, ports or resource references. Validate those choices before materializing the graph. In each consuming .NET process, register its own strongly typed runtime options using `AddOptions`, binding and startup validation. Keep the existing explicit projection from AppHost choices/parameters into child configuration.

**Inference from the documented process boundary:** registering `IOptions<T>` in AppHost does not register it in the child application. `IOptionsMonitor<T>` invalidates option values; it does not rerun resource-building calls, add/remove containers, or provide a cross-process configuration channel. A live runtime setting requires a reload-capable source inside the child plus code that rereads or applies the new value. Environment projection alone is not a live-update mechanism. Changes to graph-defining settings should follow the application's restart/redeployment workflow unless an explicit reconciliation mechanism is implemented.

For runtime defaults, prefer `IOptions<T>` initially. Introduce `IOptionsMonitor<T>` only for settings with defined update semantics; use `IOptionsSnapshot<T>` when request/scope consistency is specifically useful. These choices are recommendations based on the documented lifetimes, not additional framework guarantees.
