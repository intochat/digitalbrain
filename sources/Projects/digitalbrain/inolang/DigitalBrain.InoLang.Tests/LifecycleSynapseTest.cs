using Xunit;
using DigitalBrain.Runtime.Neurons;
using System;

namespace DigitalBrain.InoLang.Tests;

public sealed class LifecycleSynapseTest
{
    [Fact]
    public void LifecycleSynapses_CanBeInstantiated_WithProperHeaders()
    {
        var activated = new NeuronActivated("TestNeuronType", "Instance-123");
        Assert.Equal("TestNeuronType", activated.NeuronType);
        Assert.Equal("Instance-123", activated.InstanceId);

        var deactivated = new NeuronDeactivated("TestNeuronType", "Instance-123");
        Assert.Equal("TestNeuronType", deactivated.NeuronType);
        Assert.Equal("Instance-123", deactivated.InstanceId);

        var unresolved = new NeuronUnresolvedReference("TestNeuronType", "sqlite.connection");
        Assert.Equal("TestNeuronType", unresolved.NeuronType);
        Assert.Equal("sqlite.connection", unresolved.TargetReference);
    }
}
