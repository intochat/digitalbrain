# ClickHouse module — implementation notes

Running log of what the code actually looks like versus the brief in `clickhouse-module-plan.md`.
Deviations are listed at the end and repeated in the PR description.

## Pinned versions (verified with `dotnet package search --exact-match`)

| Package | Version | Where |
|---|---|---|
| `Aspire.Hosting.ClickHouse` | 13.5.3 | `Directory.Packages.props`, `Aspire.Hosting` project |
| `ClickHouse.Driver` | 1.4.0 (newest stable 1.x on nuget.org; 1.4.0-rc1/rc2 skipped) | module project |
| `Testcontainers.ClickHouse` | 4.14.0 (4.15.0 exists; brief pins 4.14.0) | test project only |

`ClickHouse.Client` is not referenced anywhere.

## Things worth knowing (found while reading the reference files)

- **Card mapping lives in `ChatNeuron.OfferAsync`.** `ReceiveAsync` lists the four ui signals
  (`ChartRendered`, `GraphRendered`, `ImageDescribed`, `SheetChanged`) and `OfferAsync` maps the
  type to a `UiCardKinds` value with `_ => UiCardKinds.Spreadsheet` as the default. `TableRendered`
  must be added to both places or it becomes a spreadsheet card.
- **`AddNativeTool` signature:** `IServiceCollection AddNativeTool(string name, Func<IServiceProvider, AIFunction> create)`
  in `DigitalBrain.Modules.AI.Contracts`. Tools are resolved lazily per name.
- **Module loading:** the kernel resolves `DigitalBrain__Modules__N = "FullName, Assembly"` with
  `Type.GetType`, so `DigitalBrain.Silo.csproj` must reference the module project or the silo cannot
  load it at runtime. The brief does not mention this; Memory/Salesforce are referenced the same way.
- **Descriptors walk inherited interfaces.** `DescriptorTable` enumerates every `INeuron` interface a
  grain class implements (including `ITable` through `IClickHouseTable`) and picks the JSON context
  from the assembly that declares each interface. So `create`/`update`/`read`/`operation` on the
  query table neuron use `UIJson`, and `create-query` uses `ClickHouseJson`.
- **`TableService` is the only routing point.** It hard-codes the `table-` prefix and the `table`
  grain type in `Table(id)` and `ListAsync`; the `ITableSource` seam replaces both.
- **`TablePolicy` is internal** to `DigitalBrain.Modules.UI`; the ClickHouse module reaches it through
  `InternalsVisibleTo` rather than a copy.
- **Working-tree line endings are LF** even though `.editorconfig` says CRLF; `.gitattributes` normalises
  on commit and `dotnet format whitespace --verify-no-changes` passes either way.
- **Docker is available locally** (engine 29.7.2), so the gated Testcontainers test and `aspire run`
  can both be exercised on this machine.

## Deviations from the brief

1. `WithClickHouse` takes `Action<ClickHouseHostingOptions>? configure` (the usage shown in the brief,
   `WithClickHouse(o => o.WithSeed("leads"))`) rather than an options instance.
2. `ClickHouseNames` lives in the Contracts project (like `UIVocabulary`/`ExcelVocabulary`) so the
   hosting projection and the tests share one vocabulary without referencing module internals.
3. The seed starts with `SET allow_experimental_json_type = 1;` so the `JSON attributes` column also
   creates on 24.8–25.2 images where the type was still gated; on newer images the setting is an
   accepted no-op.
