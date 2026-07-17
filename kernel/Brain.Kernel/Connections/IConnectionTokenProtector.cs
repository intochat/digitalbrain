using Brain.Contracts;

namespace Brain.Kernel.Connections;

public interface IConnectionTokenProtector
{
    string Protect(NeuronAddress address, ConnectionToken token);
    ConnectionToken Unprotect(NeuronAddress address, string protectedToken);
}
