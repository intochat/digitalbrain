using Ino.NeuronTesting;

namespace Ino.Domains.Travel.SmokeTests;

public abstract class TravelNeuronTest<TNeuron>(NeuronAppHostFixture<Projects.Ino_AppHost_Testing> fixture)
    : NeuronE2ETest<TNeuron, Projects.Ino_AppHost_Testing>(fixture)
    where TNeuron : class;
