namespace DigitalBrain.Google;

using Brain.Contracts;

[Alias("digitalbrain.google.IGmail")]
[NeuronContract("google.gmail.v1")]
public interface IGmail : IGrainWithStringKey
{
    [Alias("GetIdentityAsync")]
    Task<string> GetIdentityAsync();
}
