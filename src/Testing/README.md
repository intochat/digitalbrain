# Testing the migrated foundation
Run `dotnet test --solution DigitalBrain.Foundation.slnx -p:CodeGraphRefresh=false`.
BrainTestHost starts real Orleans with the production BrainClient. Subscribe before acting;
trigger once and await results. The full solution is not migrated yet.
