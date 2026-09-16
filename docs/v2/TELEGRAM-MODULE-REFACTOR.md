# Telegram module hosting refactor

## Agreed direction

Telegram stays in the DigitalBrain process, following UI's module projection pattern. Delete the separate Gateway project. Extend IModule with Configure(IEndpointRouteBuilder), defaulting to no endpoints for existing modules. Introduce the typed IBot neuron and use TripRadar's Telegram.BotAPI 9.5.0 package.

## Implementation and checks

- Core: register the configured module instances and map their endpoints generically. Owner authentication remains the default; explicitly marked Telegram routes perform provider authentication. A dedicated listener accepts only endpoints owned by Telegram.
- Hosting: AddModule<TelegramModule>(telegram => telegram.WithBot()) composes secret parameters, built Flutter assets, a named Telegram HTTP endpoint and optional Cloudflare tunnel. No gateway resource or reverse-proxy hop remains. Static bundle packaging belongs to the Telegram project.
- Domain: IBot and BotNeuron replace CLR names ITelegram and TelegramNeuron while retaining the telegram contract alias, grain identity, commands, state and receipt deduplication. Existing saved behaviors remain valid.
- Transport: typed NuGet calls replace manual Bot API envelopes; typed Update parsing preserves strict private-chat identity validation. Provider errors remain sanitized and provider calls suppress token-bearing traces.
- Verification: endpoint ownership/authentication/listener tests, SDK protocol tests, existing durable reminder recovery tests, Release AppHost build and full backend regression. Verify the new listener locally without claiming real delivery when no bot token is supplied.

The existing Flutter workspace location is retained so its shared packages and release build remain compatible. Conversational replies and outbound reminder messages are outside this refactor.

## Validation

- Full Release suite: 585 passed, 6 existing skips, 0 failures (591 total).
- Focused suites: Telegram 35 passed; module endpoints 22 passed.
- Release AppHost build: zero warnings and errors.
- Fresh local publish output contains Mini App index and main.dart.js through the module's content items.
- Aspire startup: kernel healthy, separate named http/telegram endpoints, no gateway resource. The live registrar reached Connected using the configured bot credential without exposing it.
- Actual Telegram listener: unrelated /mcp, /health and /agent/connections/telegram returned 404; unsigned Telegram health/state returned 401; Mini App returned 200. The owner listener exposes connection status as intended.
- No real incoming receipt was observed during validation. Tests cover typed receipt admission, duplicate suppression, reminders and recovery.
