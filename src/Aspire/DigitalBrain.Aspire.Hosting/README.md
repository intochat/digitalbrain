# Module groups in the Aspire dashboard

Hosting extensions attach their resources to `module.Resource` or `brain.GetModuleResource<TModule>()` using `WithParentRelationship`. The `modules` collection contains short module names; repeated requests reuse the same group. Groups are created lazily, so modules without separate hosted resources do not add empty rows. The dashboard collection name is independent of the Orleans service identity passed to `AddDigitalBrain`.

Current hierarchy:

```text
modules
  telegram
    telegram-tunnel
    telegram-miniapp-build
  ui
    flutter
  memory
    qdrant
  clickhouse
    clickhouse-server
      clickhouse-db
  ai (when Ollama is hosted)
    ollama
      models
kernel (shared by all modules)
  storage
    grainstate / journal / clustering / reminders
```

The parents are marked `Configured`, not healthy/running: they are organizational rows, not processes or aggregate health checks. They are excluded from deployment manifests. Module-specific hosting code owns these relationships, keeping the AppHost free of layout wiring.

Storage belongs to the kernel because it provides shared Orleans infrastructure, not a business integration. The first kernel that references the brain becomes its visual parent; all kernels still wait for the same storage. This UI relationship does not reverse startup dependencies.

ClickHouse's process is named `clickhouse-server` to distinguish it from the `clickhouse` module group. Its data volume is explicitly derived from the original `clickhouse` resource name, preserving stored data. When upgrading a running AppHost from the old name, stop the old persistent `clickhouse` process before starting `clickhouse-server`, so they never mount the same data volume concurrently.
