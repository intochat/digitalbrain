namespace TripRadar.Server.Comms.Core.Contracts.Encryptions;

public interface IEncryptionFactory
{
    IEncryptor GetEncryptor(string cryptographerName);

    IDecryptor GetDecryptor(string cryptographerName);
}
