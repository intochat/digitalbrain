# DigitalBrain.Capabilities.Storage

The `storage@1` capability contract: files and folders behind one storage connection.

- `IStorage` (`[Capability("storage", 1)]`): `Describe`, `Roots`, `List` (paged, sorted), `Stat`, `Read` (bytes with
  range; `ReadText` helper), `Write(WritePlan)` returning an `OperationId`, `Search(SearchQuery)`, `Ancestors`, and the
  host-local stream members `OpenRead(ItemId, ByteRange?)` and `OpenWrite(WritePlan)`.
- `ItemId` is opaque; only the module providing the storage connection interprets it.
- `Quota` is tri-state (`Known`, `Unlimited`, `Unknown`); an unknown quota has no numbers and never renders as 0.
- `WritePlan` factories default `ConflictPolicy.Fail`; delete goes to the trash unless `Permanent`.
- Errors derive from `StorageException` with stable codes: `not-found`, `conflict`, `denied`, `quota-exceeded`,
  `unavailable`, `needs-auth`, `unsupported`.
- Operation signals: `storage.operation.started`, `storage.operation.progress`, `storage.operation.done`.

References only `DigitalBrain`; no Orleans, no ASP.NET Core.
