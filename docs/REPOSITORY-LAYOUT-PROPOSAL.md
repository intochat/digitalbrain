# Proposed solution layout

Discussion draft; folder moves have not been applied. Contains all 116 remaining solution projects, each exactly once. Project extensions omitted.

The physical layout mirrors these groups below `src/`; the solution has no `src/` folder. Each project directory matches its project name. Test projects live in the nearest domain-level `tests/` folder. Core libraries move out of `Modules/` into `DigitalBrain/`. Existing project names are preserved in this proposal; namespace alignment needs a separate audit before moves are applied.

## Required disk and solution alignment

- Apply this hierarchy both on disk under `src/` and in `DigitalBrain.slnx`; changing only solution folders is insufficient.
- Omit only the physical `src/` prefix from solution folder names. `Solution Items` is a virtual group for repository-root files.
- Use lowercase `tests/` beside the projects each suite covers, including `src/Aspire/tests/` and the matching `/Aspire/tests/` solution folder.
- Each project has its own directory named exactly like its `.csproj`; align assembly, package (where packable), and root namespace with that name.
- Update project references, build imports, CI paths, Dockerfiles, fixtures, tooling and documentation after physical moves. Preserve serialized aliases and field IDs when namespaces change.
- Verify that all 116 projects occur exactly once, every solution path exists, and build/test discovery succeeds after the move.

For example:

```text
Disk:     src/Aspire/tests/DigitalBrain.Aspire.Hosting.Tests.Unit/DigitalBrain.Aspire.Hosting.Tests.Unit.csproj
Solution: /Aspire/tests/ -> DigitalBrain.Aspire.Hosting.Tests.Unit
```

## Complete proposed solution tree

```text
DigitalBrain.slnx
├── Aspire/
│   ├── tests/
│   │   └── DigitalBrain.Aspire.Hosting.Tests.Unit
│   ├── DigitalBrain.Aspire.Client
│   ├── DigitalBrain.Aspire.Hosting
│   └── DigitalBrain.Aspire.Server
├── DigitalBrain/
│   ├── Client/
│   │   ├── tests/
│   │   │   ├── DigitalBrain.Client.Orleans.Tests.Unit
│   │   │   └── DigitalBrain.Client.Tests.Unit
│   │   ├── DigitalBrain.Client
│   │   └── DigitalBrain.Client.Orleans
│   ├── Deployment/
│   │   ├── tests/
│   │   │   └── DigitalBrain.Deployment.Tests
│   │   └── DigitalBrain.Deployment
│   ├── Kernel/
│   │   ├── tests/
│   │   │   ├── DigitalBrain.Kernel.Tests.E2E
│   │   │   └── DigitalBrain.Kernel.Tests.Unit
│   │   ├── DigitalBrain.Kernel
│   │   └── DigitalBrain.Kernel.AspNetCore
│   ├── Mcp/
│   │   ├── tests/
│   │   │   └── DigitalBrain.Mcp.Tests.Unit
│   │   └── DigitalBrain.Mcp
│   ├── Platform/
│   │   ├── tests/
│   │   │   └── DigitalBrain.Platform.Tests.Unit
│   │   ├── DigitalBrain.Platform
│   │   └── DigitalBrain.Platform.Contracts
│   ├── Sdk/
│   │   ├── tests/
│   │   │   └── DigitalBrain.Sdk.Tests.Unit
│   │   └── DigitalBrain.Sdk
│   ├── DigitalBrain
│   └── DigitalBrain.Contracts
├── IntoChat/
│   ├── tests/
│   │   ├── IntoChat.Tests.E2E
│   │   └── IntoChat.Tests.Unit
│   ├── IntoChat
│   ├── IntoChat.AppHost
│   └── IntoChat.ServiceDefaults
├── Modules/
│   ├── AI/
│   │   ├── tests/
│   │   │   ├── DigitalBrain.Modules.AI.Tests.E2E
│   │   │   └── DigitalBrain.Modules.AI.Tests.Unit
│   │   ├── DigitalBrain.Modules.AI
│   │   ├── DigitalBrain.Modules.AI.Aspire.Hosting
│   │   ├── DigitalBrain.Modules.AI.Contracts
│   │   └── DigitalBrain.Modules.AI.Deployment
│   ├── ClickHouse/
│   │   ├── tests/
│   │   │   └── DigitalBrain.Modules.ClickHouse.Tests.Unit
│   │   ├── DigitalBrain.Modules.ClickHouse
│   │   ├── DigitalBrain.Modules.ClickHouse.Aspire.Hosting
│   │   ├── DigitalBrain.Modules.ClickHouse.Contracts
│   │   └── DigitalBrain.Modules.ClickHouse.Deployment
│   ├── DigitalBrain/
│   │   ├── Apps/
│   │   │   ├── tests/
│   │   │   │   ├── DigitalBrain.Modules.Apps.Tests.E2E
│   │   │   │   └── DigitalBrain.Modules.Apps.Tests.Unit
│   │   │   ├── DigitalBrain.Modules.Apps
│   │   │   ├── DigitalBrain.Modules.Apps.Contracts
│   │   │   └── DigitalBrain.Modules.Apps.Testing
│   │   ├── Assistant/
│   │   │   ├── tests/
│   │   │   │   ├── DigitalBrain.Modules.Assistant.Tests.E2E
│   │   │   │   └── DigitalBrain.Modules.Assistant.Tests.Unit
│   │   │   ├── DigitalBrain.Modules.Assistant
│   │   │   └── DigitalBrain.Modules.Assistant.Contracts
│   │   ├── Compute/
│   │   │   ├── tests/
│   │   │   │   └── DigitalBrain.Modules.Compute.Tests.Unit
│   │   │   ├── DigitalBrain.Modules.Compute
│   │   │   └── DigitalBrain.Modules.Compute.Contracts
│   │   ├── Registry/
│   │   │   ├── tests/
│   │   │   │   └── DigitalBrain.Modules.Registry.Tests.Unit
│   │   │   ├── DigitalBrain.Modules.Registry
│   │   │   └── DigitalBrain.Modules.Registry.Contracts
│   │   └── Specs/
│   │       ├── tests/
│   │       │   └── DigitalBrain.Modules.Specs.Tests.Unit
│   │       ├── DigitalBrain.Modules.Specs
│   │       └── DigitalBrain.Modules.Specs.Contracts
│   ├── Files/
│   │   ├── tests/
│   │   │   ├── DigitalBrain.Modules.Files.Tests.E2E
│   │   │   └── DigitalBrain.Modules.Files.Tests.Unit
│   │   ├── DigitalBrain.Modules.Files
│   │   └── DigitalBrain.Modules.Files.Contracts
│   ├── Google/
│   │   ├── Flutter/
│   │   │   ├── tests/
│   │   │   │   ├── DigitalBrain.Modules.Flutter.Tests.E2E
│   │   │   │   └── DigitalBrain.Modules.Flutter.Tests.Unit
│   │   │   ├── DigitalBrain.Modules.Flutter
│   │   │   ├── DigitalBrain.Modules.Flutter.Aspire.Hosting
│   │   │   ├── DigitalBrain.Modules.Flutter.Contracts
│   │   │   └── DigitalBrain.Modules.Flutter.Testing
│   │   └── Gmail/
│   │       ├── tests/
│   │       │   ├── DigitalBrain.Modules.Google.Gmail.Tests.E2E
│   │       │   └── DigitalBrain.Modules.Google.Gmail.Tests.Unit
│   │       ├── DigitalBrain.Modules.Google.Gmail
│   │       ├── DigitalBrain.Modules.Google.Gmail.Aspire.Hosting
│   │       └── DigitalBrain.Modules.Google.Gmail.Contracts
│   ├── Microsoft/
│   │   ├── Aspire/
│   │   │   ├── tests/
│   │   │   │   └── DigitalBrain.Modules.Microsoft.Aspire.Tests.Unit
│   │   │   ├── DigitalBrain.Modules.Microsoft.Aspire
│   │   │   ├── DigitalBrain.Modules.Microsoft.Aspire.Aspire.Hosting
│   │   │   └── DigitalBrain.Modules.Microsoft.Aspire.Contracts
│   │   ├── CSharp/
│   │   │   ├── tests/
│   │   │   │   ├── DigitalBrain.Modules.Microsoft.CSharp.Tests.E2E
│   │   │   │   └── DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit
│   │   │   ├── DigitalBrain.Modules.Microsoft.CSharp
│   │   │   ├── DigitalBrain.Modules.Microsoft.CSharp.Aspire.Hosting
│   │   │   ├── DigitalBrain.Modules.Microsoft.CSharp.Contracts
│   │   │   ├── DigitalBrain.Modules.Microsoft.CSharp.Deployment
│   │   │   └── DigitalBrain.Modules.Microsoft.CSharp.Sandbox
│   │   ├── GitHub/
│   │   │   ├── tests/
│   │   │   │   └── DigitalBrain.Modules.Microsoft.GitHub.Tests.Unit
│   │   │   ├── DigitalBrain.Modules.Microsoft.GitHub
│   │   │   ├── DigitalBrain.Modules.Microsoft.GitHub.Aspire.Hosting
│   │   │   └── DigitalBrain.Modules.Microsoft.GitHub.Contracts
│   │   └── Playwright/
│   │       ├── tests/
│   │       │   └── DigitalBrain.Modules.Microsoft.Playwright.Tests.Unit
│   │       ├── DigitalBrain.Modules.Microsoft.Playwright
│   │       └── DigitalBrain.Modules.Microsoft.Playwright.Contracts
│   ├── Postgres/
│   │   ├── tests/
│   │   │   └── DigitalBrain.Modules.Postgres.Tests.Unit
│   │   ├── DigitalBrain.Modules.Postgres
│   │   ├── DigitalBrain.Modules.Postgres.Aspire.Hosting
│   │   └── DigitalBrain.Modules.Postgres.Contracts
│   ├── Qdrant/
│   │   ├── tests/
│   │   │   └── DigitalBrain.Modules.Qdrant.Tests.Unit
│   │   ├── DigitalBrain.Modules.Qdrant
│   │   ├── DigitalBrain.Modules.Qdrant.Aspire.Hosting
│   │   ├── DigitalBrain.Modules.Qdrant.Contracts
│   │   └── DigitalBrain.Modules.Qdrant.Deployment
│   ├── Salesforce/
│   │   ├── tests/
│   │   │   └── DigitalBrain.Modules.Salesforce.Tests.Unit
│   │   ├── DigitalBrain.Modules.Salesforce
│   │   ├── DigitalBrain.Modules.Salesforce.Aspire.Hosting
│   │   └── DigitalBrain.Modules.Salesforce.Contracts
│   ├── Supabase/
│   │   ├── tests/
│   │   │   ├── DigitalBrain.Modules.Supabase.Tests.E2E
│   │   │   └── DigitalBrain.Modules.Supabase.Tests.Unit
│   │   ├── DigitalBrain.Modules.Supabase
│   │   ├── DigitalBrain.Modules.Supabase.Aspire.Hosting
│   │   └── DigitalBrain.Modules.Supabase.Contracts
│   └── Time/
│       ├── tests/
│       │   └── DigitalBrain.Modules.Time.Tests.Unit
│       ├── DigitalBrain.Modules.Time
│       └── DigitalBrain.Modules.Time.Contracts
├── Solution Items/
│   ├── .editorconfig
│   └── Directory.Packages.props
└── Testing/
    ├── tests/
    │   ├── DigitalBrain.Architecture.Tests
    │   └── DigitalBrain.Testing.Unit.Tests.Unit
    ├── DigitalBrain.Testing
    ├── DigitalBrain.Testing.E2E
    └── DigitalBrain.Testing.Unit
```
