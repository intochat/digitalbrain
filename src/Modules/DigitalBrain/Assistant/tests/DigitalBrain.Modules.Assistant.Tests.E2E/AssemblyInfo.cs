// The reference host shares its scripted model and shell state; facts run serially.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
[assembly: Xunit.AssemblyFixture(typeof(DigitalBrain.Testing.E2E.ReferenceBrainFixture))]
