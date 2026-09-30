# Microsoft Playwright

This module automates one existing desktop-owned WebView2 page per neuron. Register
'PlaywrightModule' with the backend. It has no Aspire resource, browser
installation, browser launch, persisted endpoint, or customer-research logic.

Attach with a loopback port and an unpredictable 32-hex session ID. The initial
page URL must exactly match 'about:blank#digitalbrain-{sessionId}'; missing or
duplicate matches fail. Later reconnects can reuse only the same previously
verified CDP target ID on the same port/session. This transient mapping is capped at 256 sessions; closed native targets are reclaimed
when attaching to their endpoint. Live target identity never expires merely through inactivity.

Operations are serialized, while detach/replacement cancels older operations.
Cancellation disposes the Playwright automation transport, never the native page,
context, or browser. The next research run can attach again to the verified target.
After a backend restart, a new marked UI page is required. Stale detach is a no-op.

## Network policy

Navigation accepts public HTTP(S), standard ports, and no embedded credentials.
HTTP requests are fulfilled through a DNS-validated, address-pinned transport;
private/reserved addresses, downloads, POST requests and WebSockets are blocked.
Images, stylesheets, fonts and scripts remain enabled. Cookies are not forwarded.

The desktop environment must use a dead-end proxy to block native bypass paths,
disable background networking and non-proxied WebRTC, and prevent popups and
permissions. The Flutter owner configures these flags. This is a restricted public
research view, not an authenticated general-purpose browser.

Navigate resolves up to eight redirects before loading the final URL. A redirect
encountered while clicking or from a server-dependent response displays an
intermediate page with a validated destination link, rather than allowing native
redirect following outside interception. Subresources follow validated redirects
within the pinned transport. Limits are 8 MiB per resource, 128 MiB and 2,000
requests per navigation or interactive action; observations return at most 16,000 text characters and
80 links. Each action starts a fresh resource budget; late responses retain the previous budget.

## Verification

Run:

    dotnet test --project src/Modules/Microsoft/Playwright/DigitalBrain.Modules.Microsoft.Playwright.Tests.Unit/DigitalBrain.Modules.Microsoft.Playwright.Tests.Unit.csproj -p:CodeGraphRefresh=false

The native smoke test is skipped unless 'DIGITALBRAIN_PLAYWRIGHT_CDP_PORT' and
'DIGITALBRAIN_PLAYWRIGHT_SESSION' identify a live marked WebView2 page. It navigates
to example.com, compares observations with the page URL, verifies the native page
survives detach, and reconnects to the same validated target. It requires internet
access and changes the supplied visible page.
