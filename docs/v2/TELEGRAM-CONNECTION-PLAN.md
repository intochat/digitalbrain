# Live Telegram connection plan

Continue on v2 after committing the existing behavior/reminder implementation.

- [x] Aspire prompts for a secret Telegram bot token; generates/persists a webhook secret. Telegram:Enabled=false explicitly disables local onboarding.
- [x] Local default runs a restricted Telegram gateway and cloudflared quick tunnel; supplied stable public HTTPS URL bypasses quick tunnel. No localhost fallback and no exposure of generic kernel routes.
- [x] Build the Mini App before first local use and serve only the module bundle.
- [x] Runtime validates getMe, probes the public route against this kernel instance, registers setWebhook without dropping updates, registers the Mini App menu, and verifies getWebhookInfo.
- [x] Status distinguishes missing input, failed setup, configured webhook and actually received messages. Bot token never appears in provider errors, logs or tracing.
- [x] Tests cover registration protocol, failures, gateway isolation, fresh tunnel parsing, and existing durable ingress. Full Release regression: 573 passed, 6 existing skips, 0 failures.
- [x] Restart the AppHost to make the secret prompt available; verify live state honestly. Gateway and Cloudflare tunnel run successfully. Public `/mcp` returns 404 and unsigned Telegram health/state return 401.
- [ ] User enters bot token in the open Aspire secret prompt, then sends a real message. Kernel waits for that parameter; no real message or provider registration has been claimed.

Reference: E:/projects/TripRadar/src/Aspire/Hosting/{Bot,Telegram,Cloudflared}. Keep its secret parameter and local HTTPS pattern, remove permissive forwarding, shell restart loops and localhost fallback. Cloudflare quick tunnels are local development only; production uses a stable public HTTPS endpoint or named tunnel.
