# DigitalBrain

DigitalBrain is an Aspire-orchestrated, Orleans-backed personal runtime with a Flutter desktop client and independently discoverable product modules.

## Run the product

From the repository root:

```powershell
aspire start
```

The CoreV2 AppHost starts persistent Azurite storage, the Orleans runtime, the ProductHost HTTP/SSE/MCP gateway, and the Flutter Windows client. Flutter is launched by its module-owned Aspire hosting extension and receives the ProductHost endpoint from the resource graph. The Aspire dashboard also exposes the Flutter hot-reload command.

## Product modules

- Proof, Conversation, Scheduling, Behavior, and Memory are available and durable.
- AI, Google, and Salesforce are discoverable as optional modules and report `NeedsSetup` until their provider adapters are configured.
- Conversation has a dedicated Flutter surface; every module can also be inspected and invoked through the generic operation surface.

Migration and verification evidence is recorded in [status.md](status.md).

## CoreV2 cutover baseline

`DigitalBrain.slnx` groups V1 under `/src/` (`srcv2/Kernel`, `srcv2/Modules`) and CoreV2 under `/srcv2/` (`srcv2/CoreV2`) plus `tests/CoreV2`. CoreV2 AppHost, ProductHost, and RuntimeHost are the compiled product surface; V1 remains in the solution for the AppHost composition graph until its scheduled removal.

CI verifies the CoreV2 architecture, Abstractions, Core, and Proof suites independently and builds the full solution in Release with warnings treated as errors. See [plans/COREV2.md](plans/COREV2.md) for the verified framework scope.
