namespace DigitalBrain.Sdk;

public sealed class McpOperationException : Exception
{
    public McpOperationException()
        : base("The MCP operation failed.")
    {
    }

    public McpOperationException(string message)
        : base(message)
    {
    }

    public McpOperationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
