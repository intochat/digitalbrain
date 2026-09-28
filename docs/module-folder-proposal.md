# DigitalBrain module folder proposal

Status: approved and implemented for all projects under src/Modules.

## Proposed structure

Keep short module grouping folders, and name every project directory exactly after its `.csproj` filename (without the extension). Place test projects alongside implementation and contract projects.

```text
src/Modules/DigitalBrain/
├── Apps/
├── Compute/
├── Discovery/
│   ├── DigitalBrain.Modules.Discovery/
│   │   ├── DigitalBrain.Modules.Discovery.csproj
│   │   ├── Agents/
│   │   ├── Configuration/
│   │   ├── Search/
│   │   └── Sources/
│   ├── DigitalBrain.Modules.Discovery.Contracts/
│   │   └── DigitalBrain.Modules.Discovery.Contracts.csproj
│   ├── DigitalBrain.Modules.Discovery.Tests.Unit/
│   │   └── DigitalBrain.Modules.Discovery.Tests.Unit.csproj
│   └── README.md
├── Identity/
│   ├── DigitalBrain.Modules.Identity/
│   │   ├── DigitalBrain.Modules.Identity.csproj
│   │   ├── Configuration/
│   │   ├── Directory/
│   │   └── Grants/
│   ├── DigitalBrain.Modules.Identity.Contracts/
│   │   └── DigitalBrain.Modules.Identity.Contracts.csproj
│   └── DigitalBrain.Modules.Identity.Tests.Unit/
│       └── DigitalBrain.Modules.Identity.Tests.Unit.csproj
├── Kernel/
└── Specs/
```

The solution groups remain `/Modules/DigitalBrain/Identity/` and `/Modules/DigitalBrain/Discovery/`. Each project entry points to its matching physical directory. The same project-folder naming convention now applies to Apps, Compute, Kernel, and Specs, including their test projects.

## Namespace convention

Project directories match project names. C# namespaces use the configured project root plus the source subfolder, preserving the established public roots used by sibling modules.

| Project | Root namespace |
| --- | --- |
| DigitalBrain.Modules.Identity | DigitalBrain.Identity |
| DigitalBrain.Modules.Identity.Contracts | DigitalBrain.Identity |
| DigitalBrain.Modules.Identity.Tests.Unit | DigitalBrain.Modules.Identity.Tests.Unit |
| DigitalBrain.Modules.Discovery | DigitalBrain.Discovery |
| DigitalBrain.Modules.Discovery.Contracts | DigitalBrain.Discovery |
| DigitalBrain.Modules.Discovery.Tests.Unit | DigitalBrain.Modules.Discovery.Tests.Unit |

For example, implementation files under `Configuration/` use `DigitalBrain.Identity.Configuration` or `DigitalBrain.Discovery.Configuration`. Existing `Directory`, `Grants`, `Agents`, `Search`, and `Sources` namespaces already follow this pattern. Update all affected imports and project root namespaces.

## Implemented scope

1. Move the complete Identity and Discovery module trees under `src/Modules/DigitalBrain/` and rename their project directories as shown.
2. Update solution entries, all affected relative project references, imports, fully qualified names, and active documentation/build paths.
3. Preserve explicit serialization aliases, grain identities, routes, and configuration keys.
4. Check for stale paths/namespaces, build the solution, and run affected unit tests.

## Applied across all modules

Every project directory under src/Modules now matches its .csproj filename without the extension. Module and vendor grouping folders remain in place. Test projects sit alongside the other projects in their module.

```text
src/Modules/AI/
    DigitalBrain.Modules.AI/
    DigitalBrain.Modules.AI.Aspire.Hosting/
    DigitalBrain.Modules.AI.Contracts/
    DigitalBrain.Modules.AI.Deployment/
    DigitalBrain.Modules.AI.Tests.E2E/
    DigitalBrain.Modules.AI.Tests.Unit/
src/Modules/ClickHouse/
    DigitalBrain.Modules.ClickHouse/
    DigitalBrain.Modules.ClickHouse.Aspire.Hosting/
    DigitalBrain.Modules.ClickHouse.Contracts/
    DigitalBrain.Modules.ClickHouse.Deployment/
    DigitalBrain.Modules.ClickHouse.Tests.Unit/
src/Modules/Coding/
    DigitalBrain.Modules.Coding/
    DigitalBrain.Modules.Coding.Aspire.Hosting/
    DigitalBrain.Modules.Coding.Contracts/
    DigitalBrain.Modules.Coding.Tests.Unit/
src/Modules/DigitalBrain/Apps/
    DigitalBrain.Modules.Apps/
    DigitalBrain.Modules.Apps.Contracts/
    DigitalBrain.Modules.Apps.Testing/
    DigitalBrain.Modules.Apps.Tests.E2E/
    DigitalBrain.Modules.Apps.Tests.Unit/
src/Modules/DigitalBrain/Compute/
    DigitalBrain.Modules.Compute/
    DigitalBrain.Modules.Compute.Contracts/
    DigitalBrain.Modules.Compute.Deployment/
    DigitalBrain.Modules.Compute.Tests.Unit/
src/Modules/DigitalBrain/Discovery/
    DigitalBrain.Modules.Discovery/
    DigitalBrain.Modules.Discovery.Contracts/
    DigitalBrain.Modules.Discovery.Tests.Unit/
src/Modules/DigitalBrain/Identity/
    DigitalBrain.Modules.Identity/
    DigitalBrain.Modules.Identity.Contracts/
    DigitalBrain.Modules.Identity.Tests.Unit/
src/Modules/DigitalBrain/Kernel/
    DigitalBrain/
    DigitalBrain.Aspire/
    DigitalBrain.Aspire.Hosting/
    DigitalBrain.Client/
    DigitalBrain.Contracts/
    DigitalBrain.Deployment/
    DigitalBrain.Deployment.Tests/
    DigitalBrain.Runtime.Tests.E2E/
    DigitalBrain.Runtime.Tests.Unit/
    DigitalBrain.Sdk/
src/Modules/DigitalBrain/Specs/
    DigitalBrain.Modules.Specs/
    DigitalBrain.Modules.Specs.Contracts/
    DigitalBrain.Modules.Specs.Tests.Unit/
src/Modules/Files/
    DigitalBrain.Modules.Files/
    DigitalBrain.Modules.Files.Contracts/
    DigitalBrain.Modules.Files.Tests.Unit/
src/Modules/Google/Flutter/
    DigitalBrain.Modules.Flutter/
    DigitalBrain.Modules.Flutter.Aspire.Hosting/
    DigitalBrain.Modules.Flutter.Contracts/
    DigitalBrain.Modules.Flutter.Testing/
    DigitalBrain.Modules.Flutter.Tests.E2E/
    DigitalBrain.Modules.Flutter.Tests.Unit/
src/Modules/Google/Gmail/
    DigitalBrain.Modules.Google.Gmail/
    DigitalBrain.Modules.Google.Gmail.Aspire.Hosting/
    DigitalBrain.Modules.Google.Gmail.Contracts/
    DigitalBrain.Modules.Google.Gmail.Tests.E2E/
    DigitalBrain.Modules.Google.Gmail.Tests.Unit/
src/Modules/Memory/
    DigitalBrain.Modules.Memory/
    DigitalBrain.Modules.Memory.Contracts/
    DigitalBrain.Modules.Memory.Tests.Unit/
src/Modules/Microsoft/Aspire/
    DigitalBrain.Modules.Microsoft.Aspire/
    DigitalBrain.Modules.Microsoft.Aspire.Aspire.Hosting/
    DigitalBrain.Modules.Microsoft.Aspire.Contracts/
    DigitalBrain.Modules.Microsoft.Aspire.Tests.Unit/
src/Modules/Microsoft/CSharp/
    DigitalBrain.Modules.Microsoft.CSharp/
    DigitalBrain.Modules.Microsoft.CSharp.Aspire.Hosting/
    DigitalBrain.Modules.Microsoft.CSharp.Contracts/
    DigitalBrain.Modules.Microsoft.CSharp.Deployment/
    DigitalBrain.Modules.Microsoft.CSharp.Sandbox/
    DigitalBrain.Modules.Microsoft.CSharp.Tests.E2E/
    DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit/
src/Modules/Microsoft/DotNet/
    DigitalBrain.Modules.Microsoft.DotNet/
    DigitalBrain.Modules.Microsoft.DotNet.Contracts/
    DigitalBrain.Modules.Microsoft.DotNet.Tests.Unit/
src/Modules/Microsoft/GitHub/
    DigitalBrain.Modules.Microsoft.GitHub/
    DigitalBrain.Modules.Microsoft.GitHub.Aspire.Hosting/
    DigitalBrain.Modules.Microsoft.GitHub.Contracts/
    DigitalBrain.Modules.Microsoft.GitHub.Tests.Unit/
src/Modules/Microsoft/Roslyn/
    DigitalBrain.Modules.Microsoft.Roslyn/
    DigitalBrain.Modules.Microsoft.Roslyn.Contracts/
    DigitalBrain.Modules.Microsoft.Roslyn.Tests.Unit/
src/Modules/Qdrant/
    DigitalBrain.Modules.Qdrant/
    DigitalBrain.Modules.Qdrant.Aspire.Hosting/
    DigitalBrain.Modules.Qdrant.Deployment/
    DigitalBrain.Modules.Qdrant.Tests.Unit/
src/Modules/Salesforce/
    DigitalBrain.Modules.Salesforce/
    DigitalBrain.Modules.Salesforce.Aspire.Hosting/
    DigitalBrain.Modules.Salesforce.Contracts/
    DigitalBrain.Modules.Salesforce.Tests.Unit/
src/Modules/Supabase/
    DigitalBrain.Modules.Supabase/
    DigitalBrain.Modules.Supabase.Aspire.Hosting/
    DigitalBrain.Modules.Supabase.Contracts/
    DigitalBrain.Modules.Supabase.Tests.Unit/
src/Modules/Time/
    DigitalBrain.Modules.Time/
    DigitalBrain.Modules.Time.Contracts/
    DigitalBrain.Modules.Time.Tests.Unit/
```
