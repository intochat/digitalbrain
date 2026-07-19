# DigitalBrain package quickstart

From the repository root:

```powershell
.\eng\pack.ps1
.\samples\DigitalBrain.Quickstart\Start-Quickstart.ps1
```

This quickstart is Development-only. It starts the Aspire resources in the background, waits for the kernel, and launches the package-based console in the foreground so it owns your terminal. Development defaults `digitalbrain-owner` to `quickstart-user`; pass `-Owner alice` to the second command when you want a different durable owner. Aspire prompts for the OpenAI and Anthropic API keys. Provider credentials are injected only into the kernel; client projects receive restricted Orleans discovery configuration.

The console starts with the balanced role and the `main` durable conversation. Use `/role fast|balanced|reasoning`, `/new`, `/conversation [id]`, `/help`, and `/exit`.

## Troubleshooting

- Run `.\eng\pack.ps1` again when restore reports a missing `DigitalBrain.*` version.
- Run `.\eng\test-quickstart.ps1 -CleanCache` to prove restore and build from an empty NuGet cache.
- Keep `DOTNET_ENVIRONMENT=Development`; the AppHost, console, Dashboard, and DevUI are intentionally disabled elsewhere.
- Store provider keys through Aspire parameter configuration. Do not add keys to project files, source, or `NuGet.config`.
- Confirm Docker is running when the Azurite emulators cannot start.
