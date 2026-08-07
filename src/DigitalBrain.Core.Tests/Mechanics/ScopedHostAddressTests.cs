namespace DigitalBrain;

public sealed class ScopedHostAddressTests
{
    [Fact]
    public void SameLogicalNeuronInDifferentWorkspacesUsesDifferentPhysicalAddresses()
    {
        var neuron = new NeuronId("salesforce", "account:acme");
        var left = new ScopedNeuronAddress(new ScopeKey("workspace/left:one"), neuron);
        var right = new ScopedNeuronAddress(new ScopeKey("workspace/right:one"), neuron);

        var encoded = ScopedNeuronAddressCodec.Encode(left);

        Assert.Equal(left, ScopedNeuronAddressCodec.Decode(encoded));
        Assert.Equal(neuron, left.Neuron);
        Assert.NotEqual(NeuronHost.AddressOf(left), NeuronHost.AddressOf(right));
    }

    [Fact]
    public void RejectsALegacyKeyThatCouldOtherwiseMasqueradeAsAScopedAddress()
    {
        var legacy = NeuronKey.Encode(new NeuronId("oldscope", "1:xreceiver"));
        var scoped = new ScopedNeuronAddress(
            new ScopeKey("oldscope"),
            new NeuronId("x", "receiver"));

        Assert.Throws<InvalidOperationException>(() => ScopedNeuronAddressCodec.Decode(legacy));
        Assert.NotEqual(legacy, ScopedNeuronAddressCodec.Encode(scoped));
    }
}
