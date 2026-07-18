using Ino.NeuronTesting;

namespace Ino.Domains.Travel.Tests;

// Per-domain intermediate base so concrete test classes are one-liners:
//   public sealed class TripPlannerTests(NeuronAppHostFixture<Projects.Ino_AppHost_Testing> f)
//       : TravelNeuronTest<TripPlanner.TripPlanner>(f);
// Pinned to Projects.Ino_AppHost_Testing so every Travel-domain neuron boots
// the same test-mode topology — Ino:Mode = Testing is stamped on every silo
// by the AppHost itself, so the fixture never has to mutate process env.
public abstract class TravelNeuronTest<TNeuron>(NeuronAppHostFixture<Projects.Ino_AppHost_Testing> fixture)
    : NeuronE2ETest<TNeuron, Projects.Ino_AppHost_Testing>(fixture)
    where TNeuron : class;
