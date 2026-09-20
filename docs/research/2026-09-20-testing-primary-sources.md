# Testing architecture: primary-source research

Date: 2026-09-20. Research only; no test execution or product changes.

## Scope and evidence

The repository pins Aspire 13.5.3 and Microsoft.Playwright 1.62.0 in `Directory.Packages.props`, and .NET SDK 11.0.100-rc.1.26425.128 in `global.json`. Its Flutter packages require Dart ^3.13.0; this inspection did not establish the installed Flutter engine revision. Flutter engine findings below describe current upstream source and must be checked against the installed SDK before treating a specific engine implementation as proven locally.

Context7 `resolve-library-id` was attempted independently for Aspire, Flutter, and Playwright .NET. All three calls returned “Monthly quota exceeded,” so no IDs could be resolved and `query-docs` could not be used correctly. Official documentation and upstream source were used instead. There was no existing `docs/research` directory; this dated note establishes a location alongside the existing dated design/plan documents.

Repository grounding: `CONTEXT.md`, `src/Testing/README.md`, `src/Testing/DigitalBrain.Testing.E2E/E2EBrain.cs`, `src/Testing/DigitalBrain.Testing.Integration/IntegrationTest.cs`, and `src/Modules/Flutter/app/shell/lib/main.dart`.

## Aspire facts and architectural implications

Aspire testing orchestrates application resources as separate processes. Test-process DI changes cannot directly replace services inside those applications; configuration/environment settings can influence their startup. Its defaults include disabled dashboard and randomized proxied ports; disposing the test application cleans up resources. This supports keeping process boundaries explicit even if public test setup becomes uniform. [Aspire testing overview](https://aspire.dev/testing/overview/)

The normal `CreateAsync<TEntryPoint>` path executes an AppHost entry point. However, the pinned 13.5.3 source also exposes `DistributedApplicationTestingBuilder.Create(...)` for constructing the resource model directly. Its implementation still searches the call stack for an assembly carrying `dcpclipath` metadata and errors if none exists, referring to `Aspire.Hosting.AppHost` and `Aspire.AppHost.Sdk`. Therefore **a separate ModuleAppHost project is not an API-level requirement, but AppHost build/runtime support remains necessary**. Eliminating that project requires deliberately relocating SDK/package/build ownership, and validating packaging and resource launch; merely switching the method name is insufficient. [Aspire 13.5.3 testing builder source](https://raw.githubusercontent.com/microsoft/aspire/v13.5.3/src/Aspire.Hosting.Testing/DistributedApplicationTestingBuilder.cs)

Aspire documents passing configuration through AppHost arguments, including disabling persistent volumes. It also supports factory lifecycle hooks and shared fixtures to amortize expensive AppHost startup. A shared fixture is a performance technique, not evidence that shared mutable application state is safe between tests. [Manage AppHost in tests](https://aspire.dev/testing/manage-app-host/)

The first-test example explicitly waits for resource health before making the request. Resource start, health, HTTP availability, and a specific user flow being ready are different observations; test infrastructure should preserve those distinctions in failures. [Write your first Aspire test](https://aspire.dev/testing/write-your-first-test/)

**Proposal:** share module selection and typed configuration between production composition and tests. In-process tests may register replacement provider objects directly. External-process tests need startup-loadable provider implementations, explicit configuration, or fake network endpoints; arbitrary test-process delegates/objects cannot transparently cross that boundary. Reuse configuration vocabulary without pretending execution mechanisms are identical.

## Flutter placeholder lifecycle and the reported timeout

Flutter web accessibility is opt-in. Flutter documents either activating its invisible accessibility button or calling `SemanticsBinding.instance.ensureSemantics()` in app code. The latter creates the semantics tree without needing a browser click. [Flutter web accessibility](https://docs.flutter.dev/ui/accessibility/web-accessibility)

`ensureSemantics` returns a handle; disposing the handle ends that request for semantics. The application should retain it for the intended lifetime. The repository already retains `_semantics` globally when the URL includes `?semantics=true`. That query parameter is **this application's convention**, not a generally documented Flutter URL switch. [Flutter ensureSemantics API](https://api.flutter.dev/flutter/semantics/SemanticsBinding/ensureSemantics.html)

Upstream engine `didReceiveSemanticsUpdate` disposes the semantics helper when programmatic updates enable engine semantics. Thus a disappearing placeholder can be a sign of successful initialization. [Flutter engine semantics source](https://raw.githubusercontent.com/flutter/flutter/master/engine/src/flutter/lib/web_ui/lib/src/engine/semantics/semantics.dart)

The desktop semantics helper creates `flt-semantics-placeholder`; activating semantics disposes it, and disposal removes the element from the DOM. The source also explicitly accounts for programmatic-enable races. This is a temporary accessibility activation control, not a durable application-ready marker. [Flutter semantics helper source](https://raw.githubusercontent.com/flutter/flutter/master/engine/src/flutter/lib/web_ui/lib/src/engine/semantics/semantics_helper.dart)

**Repository-specific diagnosis hypothesis, not a reproduced finding:** `E2EBrain.OpenBrowserAsync` navigates with `semantics=true`, observes placeholder count, then calls `placeholder.First.EvaluateAsync(...)`. The app independently enables semantics. The placeholder can exist during the count and disappear before the subsequent locator evaluation resolves it. That produces precisely the kind of wait for `Locator("flt-semantics-placeholder").First` reported by the user. `SlowMo` can widen this timing window. A trace or targeted reproduction remains necessary to establish that this caused the particular failure.

**Subsequent local verification by the main investigation:** the existing `UiKitWebFacts` reproduced this timeout. Its trace recorded placeholder count 1, followed by the 60-second locator wait; the failure snapshot contained the semantics tree and expected chart text, but no placeholder. See [the review's reproduction evidence](2026-09-20-testing-unification-review.md#2-the-reported-failure-and-the-deeper-contract-problems). The hypothesis above records the state of the initial source research; the subsequent trace establishes this mechanism for the new local run.

**Proposal:** use the existing programmatic semantics opt-in as the single owned-app mechanism; wait for the semantics tree and then the actual app readiness condition. If a generic fallback is retained for other apps, make activation optional and tolerate disappearance as a valid state transition. Do not require a temporary placeholder to survive between separate operations. A larger timeout cannot repair that contract.

## Browser readiness and execution preferences

Playwright defaults to headless; headed mode and `SlowMo` are independent launch controls intended to aid observation/debugging. The repository can intentionally choose another default, but should expose these as execution preferences rather than module capabilities. A Flutter web host and a headless browser are compatible: headless describes the browser display, not the existence of the web frontend. [Playwright .NET debugging](https://playwright.dev/dotnet/docs/debug)

Playwright actions auto-wait for applicable actionability conditions; click checks include visibility, stability, event reception, and enabled state. These checks concern the target element and cannot prove the application backend/session/subscriptions are ready. [Playwright .NET auto-waiting](https://playwright.dev/dotnet/docs/actionability)

Modern pages may continue fetching data and adding UI after load. Playwright describes hydration cases where a visible control is not yet functional and recommends making readiness part of application behavior. Waiting for document load, or merely attaching `flutter-view`, therefore should not be interpreted as completion of DigitalBrain's initialization. [Playwright .NET navigations](https://playwright.dev/dotnet/docs/navigations)

**Proposal:** keep three independent dimensions in the public API: application/module composition; execution boundary (in-process, hosted integration, whole application); execution preferences (headed/headless, slow motion, diagnostics, deadlines). Reuse a common configuration model where meaningful, but return capabilities appropriate to each boundary. Record startup failures by stage and capture browser console/page errors alongside trace/screenshot artifacts. Prefer a durable, user-observable readiness assertion over repeated navigation or long sleeps.
