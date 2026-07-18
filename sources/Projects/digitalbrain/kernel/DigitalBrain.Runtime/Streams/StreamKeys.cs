using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Runtime.Streams;

public static class StreamKeys
{
    public static StreamId Receiver(string neuronType, Guid receiverId)
        => StreamId.Create(neuronType, receiverId);

    public static StreamId Receiver(string neuronType, string receiverStringKey)
        => StreamId.Create(neuronType, StringKeyToGuid(receiverStringKey));

    public static Guid StringKeyToGuid(string key)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(key), hash);
        return new Guid(hash[..16]);
    }
}
