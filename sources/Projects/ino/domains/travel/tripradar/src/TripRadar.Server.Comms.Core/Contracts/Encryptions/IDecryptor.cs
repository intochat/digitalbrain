namespace TripRadar.Server.Comms.Core.Contracts.Encryptions;

public interface IDecryptor
{
    byte[]? Decrypt(byte[]? encryptedData);
}
