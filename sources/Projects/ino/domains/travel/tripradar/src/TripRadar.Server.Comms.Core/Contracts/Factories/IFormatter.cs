namespace TripRadar.Server.Comms.Core.Contracts.Factories;

public interface IFormatter
{
    /// <summary>
    /// Gets the MIME type of the content
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// Serializes an object to its byte representation
    /// </summary>
    /// <param name="data">The object to be serialized</param>
    /// <returns></returns>
    byte[] Serialize(object data);

    /// <summary>
    /// Deserializes the data to an object of the desired type
    /// </summary>
    /// <param name="data">The binary data representing the serialized object</param>
    /// <param name="resultType">The type of the resulting object</param>
    /// <returns></returns>
    object Deserialize(byte[] data, Type resultType);

    /// <summary>
    /// Deserializes the stream to an object of the desired type
    /// </summary>
    /// <param name="stream">The stream with the data representing the serialized object</param>
    /// <param name="resultType">The type of the resulting object</param>
    /// <returns></returns>
    object Deserialize(Stream stream, Type resultType);

    /// <summary>
    /// Deserializes the data to an object of the desired type
    /// </summary>
    /// <param name="data">The string data representing the serialized object</param>
    /// <param name="resultType">The type of the resulting object</param>
    /// <returns></returns>
    object Deserialize(string data, Type resultType);

    /// <summary>
    /// Serializes an object to its string representation
    /// </summary>
    /// <param name="data">The object to be serialized</param>
    /// <returns></returns>
    string SerializeToString(object data);

}