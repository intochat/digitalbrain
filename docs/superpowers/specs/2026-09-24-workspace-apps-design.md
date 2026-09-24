# Workspace apps

## Approved direction

Apps are dedicated neuron types, with a definition declaring required modules and behavior composition. One instance belongs to one workspace. A host registers available app implementations with AddApp<T>; each workspace independently installs and configures apps. Modules are host-provided capabilities: installation rejects missing requirements rather than provisioning infrastructure.

IntoChat is the host and transport shell. Assistant and Settings are built-in apps. User apps use a generic composed app neuron and portable, versioned definitions. Behaviors contain logic; definitions contain named parts, configuration and signal bindings. Installed, running and open are separate states. Activation is recoverable state, not a one-shot event. Opening an on-demand app activates it. Workspace activation starts workspace apps. Explicit background activation remains independent of browser presence.

## Creation and sharing

Provide an app creation/editor surface, marketplace publication, installation and execution in IntoChat. Publisher identity is derived from the authenticated account; creator packages use user/appname. An immutable published version contains its complete executable composition and defaults, never references to the author's runtime neurons, state or credentials. Installation resolves fresh workspace-local identities. Different configurations and state are independent. Retrying publication or installation must not overwrite a published version or reset an existing installation.

Use the existing AppManifest, catalog and CreatorPublishing runtime. Preserve existing first-party dotted aliases. Validate namespaced creator IDs, ownership, complete composition, bounded graphs, declared configuration and required capabilities. Reject unsupported executable artifacts rather than pretending a manifest is a working app. Support host-registered behavior implementations first; custom worker code must use the existing isolated behavior system rather than loading arbitrary creator assemblies in the silo.

## Account switching

Provide a visible account switch action. End old subscriptions, replace the cookie session and re-create the shell with account-scoped local persistence. Account ownership and workspace membership must be independent; changing the active account cannot expose previous account state. Preserve configured bootstrap Basic authentication. Do not expose passwordless impersonation as production account authentication.

## Acceptance

Hosted E2E: create two accounts, create an app with at least two connected behavior parts as the first account, execute it, publish as first-user/appname, switch to the second account in the UI, find and install the exact marketplace version, set different local configuration and execute it. Assert output, source/destination state isolation, persistence after reload, namespace ownership and forbidden foreign-workspace access. Include unit coverage for composition validation, lifecycle idempotency, missing modules, immutable versions and configuration validation. Run actual tests and report environmental limitations explicitly.

## Execution decisions

The user explicitly requested autonomous planning and implementation on the current branch. Save the plan and implement without another approval gate. Preserve existing grain aliases and serialized field IDs. Record implementation decisions and verification in the plan ledger.
