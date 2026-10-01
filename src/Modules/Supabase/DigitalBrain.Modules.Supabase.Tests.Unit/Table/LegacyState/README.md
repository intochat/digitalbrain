# Pre-move table state

`table.orleans` was captured from master commit `02351af5a3c548486e4d77a6a259eb4cbb647d5b`, before the LiveTables extraction. The production types and serializers were unchanged: `SupabaseTableState` was in `DigitalBrain.Modules.Supabase`, with alias `supabase.table-state` and an `IReadOnlyList<SupabaseColumn>` field at ID 2.

To reproduce, check out that commit, copy `Capture.cs.txt` into the Supabase unit-test project as `LegacyCaptureFacts.cs`, and run that test project with `-p:CodeGraphRefresh=false -p:RunAnalyzers=false`. The temporary capture helper needs analyzers disabled for its explicit array initialization. Copy `bin/Debug/net11.0/table.orleans` into this directory. The provider returns a concrete array, as the real provider does. A real table grain writes the view, normalized SQL, source columns, operation ID, and creation request through `OrleansGrainStorageSerializer`, the serializer used by production Azure Blob storage.

`LegacySupabaseStateFacts` loads these bytes through grain storage, reads live rows, updates the view, and deactivates/reactivates the grain using newly serialized state. Keep the fixture fixed; regenerating it with current types would erase the assembly-move compatibility check.
