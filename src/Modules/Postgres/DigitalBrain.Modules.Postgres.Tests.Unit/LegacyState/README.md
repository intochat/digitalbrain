# State before table lifecycle tracking

`table.orleans` was captured with the production PostgresTableState from commit
ccf5208c9 (the original PostgresTableNeuron.cs restored during capture), before
adding ID 3. Capture.cs.txt records the production Orleans serializer and input.
It carries Owner and Accepted with a null legacy Origin. Keep these bytes fixed.
LegacyPostgresStateFacts loads them into a real neuron, reactivates it, then
replays teardown after a failed grain-state save following the physical drop.
