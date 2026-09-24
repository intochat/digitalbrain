# Apps

App manifests, catalog installation and versions, consent, proxy calls, and workspace app execution.

Apps sits beside Kernel and Behaviors under `src/Modules/DigitalBrain`. The existing Contracts, Apps and Tests/Unit projects organize code by capability:

- **Manifests**: app declarations, JSON serialization, validation, generation and first-party manifest loading.
- **Composition**: portable graphs, validation and host-provided deterministic behaviors.
- **Catalog**: installed versions, saved apps and scoped manifest discovery.
- **Consent**: review and approval.
- **Proxy**: platform-mediated app operations.
- **Workspace**: installation orchestration, execution, snapshots and internal durable operation receipts.
- **Lifecycle**: durable workspace activation.

Signals live under their owning contract feature. Public namespaces and serialized identities are independent of folder placement and remain stable. Concrete first-party packages live in `src/Apps`; IntoChat owns its HTTP endpoints and built-in applications.

Run the module tests from the repository root:

```powershell
dotnet test --project src/Modules/DigitalBrain/Apps/Tests/Unit/DigitalBrain.Modules.Apps.Tests.Unit.csproj -p:CodeGraphRefresh=false
```
