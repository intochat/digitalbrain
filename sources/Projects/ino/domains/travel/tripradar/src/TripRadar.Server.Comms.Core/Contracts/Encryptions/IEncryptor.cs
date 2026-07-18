namespace TripRadar.Server.Comms.Core.Contracts.Encryptions;

public interface IEncryptor
{
    byte[]? Encrypt(byte[]? data);
}
