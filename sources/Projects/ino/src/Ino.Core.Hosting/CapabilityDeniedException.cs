namespace Ino.Core.Hosting;

public sealed class CapabilityDeniedException : Exception
{
    public CapabilityDeniedException(string message, IReadOnlyDictionary<string, string>? details = null)
        : base(message)
    {
        Details = details;
    }

    public IReadOnlyDictionary<string, string>? Details { get; }
}
