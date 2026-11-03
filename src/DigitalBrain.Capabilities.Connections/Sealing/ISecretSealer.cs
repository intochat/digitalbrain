using DigitalBrain.Capabilities.Secrets;

namespace DigitalBrain.Capabilities.Connections.Sealing;

// Seals and opens secrets inside a ConnectionNeuron activation without going over a grain call.
public interface ISecretSealer
{
    SealedSecret Seal(string name, string plaintext);
    string Open(SealedSecret secret);
}
