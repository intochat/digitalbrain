namespace TripRadar.Server.Comms.Core.Contracts.Encryptions;

public interface ICryptographer : IEncryptor, IDecryptor
{
    string Name { get; }
}
