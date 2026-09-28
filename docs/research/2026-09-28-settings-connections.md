# Settings and Connections proposal

Date: 2026-09-28. Source inspection only; no claim about currently connected live accounts. Product changes are proposed, not implemented.

## What exists today

There are two Settings experiences. With a programming client, the gear opens a backend-owned Settings app containing Display name, a free-text `Theme (system, light, dark)` field, and Apply preferences. Its **More settings** button opens the separate Flutter settings screen. Offline, the gear opens that Flutter screen directly. This duplicates name/theme preferences and mixes workspace-persisted values with device preferences. See [entry and synchronization](../../src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_app.dart), [built-in settings surface](../../src/Applications/IntoChat/IntoChat/Apps/BuiltIn/SettingsApp.cs), and [More settings action](../../src/Modules/Google/Flutter/app/shell/lib/workspace/apps/built_in_app_view.dart).

The Flutter screen has five sections ([source](../../src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_settings.dart)):

| Section | Actual controls/content | Recommendation |
| --- | --- | --- |
| Profile | Device-local display name, role/team, Save profile, authentication disclaimer | Keep display name and actual account identity together. Remove role/team until a feature consumes it; source search finds storage/editing only. |
| Appearance | System/light/dark, compact spacing, reduce motion | Keep all three; they are consumed by the app. Use an actual choice control everywhere, never free-text theme. |
| Connections | Embedded connection form/list plus separate OAuth links | Promote to a proper service management section and unify the lists. |
| Assistant | Four descriptive cards: IntoChat, Salesforce Admin, Lead Researcher, Automation Builder | Remove the static brochure from Settings. Keep useful role descriptions in the composer picker; replace this section with functional Models settings. |
| Developer tools | UI gallery, local-storage explanation, Save workspace now | Put gallery behind developer mode. Keep storage explanation/status under Advanced; manual save is redundant for normal autosave, but a retry action is useful on persistence failure. |

The current composer agent names are not LLM choices. They are sent as conversational context; the actual model is selected on the server. See [roles](../../src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_store.dart) and [chat request](../../src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_chat.dart).

## Connections: existing capability and gaps

[ConnectWindow](../../src/Modules/Google/Flutter/app/shell/lib/integrations/connect_window.dart) supports Supabase, Salesforce, Gmail and Web research, with connection name, masked token/string, Connect, Refresh, Probe and Disconnect. [The connector API](../../src/Modules/DigitalBrain/Kernel/DigitalBrain.Sdk/Connectors/ConnectorEndpoints.cs) and [IConnectors neuron](../../src/Modules/DigitalBrain/Kernel/DigitalBrain.Sdk/Connectors/IConnectors.cs) already provide list/connect/probe/disconnect and vault references.

The separate [IntegrationsMenu](../../src/Modules/Google/Flutter/app/shell/lib/integrations/integrations_menu.dart) lists Gmail, Salesforce and GitHub as raw login links. These are not proof of connection. There is also a concrete wiring mismatch: the menu opens `/integrations/{id}/login` without a request token, while [BrowserLoginSurface](../../src/Modules/DigitalBrain/Kernel/DigitalBrain.Sdk/Auth/BrowserLoginSurface.cs) requires the one-use token created by [BrowserLogins.Require](../../src/Modules/DigitalBrain/Kernel/DigitalBrain.Sdk/Auth/BrowserLogins.cs). Redesign should use the existing provider login service through a proper start action, not copy those raw URLs.

The default [CredentialPresenceProbe](../../src/Modules/DigitalBrain/Kernel/DigitalBrain.Sdk/Connectors/CredentialPresenceProbe.cs) returns Connected for a stored credential while explicitly saying the source has not been verified. New UI must distinguish **Configured**, **Verified**, **Expired** and **Unavailable** instead of treating every current Connected record as verified service access. A unified status projection needs provider-specific account/capability evidence.

Recommended row: **Google** · account email · status, with actual authorized capabilities underneath (for example Gmail). Show only verified capabilities; Gmail authorization does not imply Calendar or Drive access. Actions: Connect / Reconnect / Manage / Disconnect. Connected services first, Add connection second. Move raw token entry behind Add connection → Advanced. Distinguish deployment configuration from the current account’s authorization, and show the workspace/account scope explicitly.

## Models: existing capability and gaps

The AI module already supports OpenAI, Anthropic, Google, xAI and Ollama configuration plus named model profiles, defaults, capability declarations and reasoning/output settings in [AIOptions](../../src/Modules/AI/DigitalBrain.Modules.AI/Configuration/AIOptions.cs). [ModelProfiles.Resolve](../../src/Modules/AI/DigitalBrain.Modules.AI/ModelProfiles.cs) resolves a named profile or explicit provider/model, rejects unconfigured providers and checks tool capability. These definitions are configuration capabilities, not evidence that a provider is configured in this deployment.

IntoChat does not expose this as a chat picker: [AgentEndpoints](../../src/Applications/IntoChat/IntoChat/Agent/AgentEndpoints.cs) accepts workspace/thread/run/messages only; RunModel reads `IntoChat:Assistant:Model` or the AI default. There is no discovered browser-facing configured-model catalog in this path.

Required work: expose a safe authenticated catalog of configured, permitted chat profiles; show default and capability/status metadata without secrets; accept an allowed profile ID on a chat turn; validate it server-side; persist conversation choice; pin it for the active run. Keep **Automatic (workspace default)** as the initial choice. A composer model picker changes subsequent turns in that conversation, while Settings chooses the default. Do not repurpose the existing assistant-role picker as a model selector. Provider API keys can remain operator-managed in the first version; user-owned keys and billing scopes are a separate feature.

## Layout options

1. **Recommended: General · Connections · Models · Advanced.** General combines profile and appearance. Connections manages external service accounts. Models lists configured AI providers/profiles and default choice. Advanced holds storage and developer tools. One Settings surface, desktop side navigation, mobile section navigation. Best balance of clarity and growth; composer offers on-demand model switching.
2. **Compact: General · Connections · Advanced**, with Services / AI providers sub-tabs inside Connections. Fewer top-level tabs and all integrations together, but models and data authorizations need distinctly labeled groups because they have different ownership and usage semantics.
3. **Minimal migration: keep Profile · Appearance · Connections, replace Assistant with Models, hide Developer tools outside developer mode.** Smaller UI change, but more navigation and existing preference ownership still needs consolidation.

Recommended delivery sequence: consolidate the two Settings entry paths and preference ownership; build unified connection status/start flows; then add the configured model catalog and conversation picker. Reuse existing collection/list presentation for service rows and existing connection/model neurons beneath a narrow authenticated projection rather than inventing another independent credentials store.
