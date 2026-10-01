# Shared implementation libraries

This directory holds implementation shared by explicitly named modules. It is not a new module or runtime layer, a home for contracts, or a catch-all for helpers. Public module contracts remain in their owning `.Contracts` projects. Libraries must not reference module implementations or provide a back door for one module to reach another module's implementation.

`DigitalBrain.LiveTables` contains the PostgreSQL query and live-table engine shared by the Postgres and Supabase modules. Both modules compose the engine through their own connectors. It retains the `DigitalBrain.Supabase` namespace and Orleans aliases for persisted-state continuity. `SupabaseTableNeuron` lives with the shared engine to preserve the existing grain identity and serializer contract; this is a specific compatibility exception, not a precedent for moving module-owned grains into generic libraries.
