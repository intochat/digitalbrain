namespace DigitalBrain.Capabilities.Compute;

public static class ExternalCallHttp
{
    internal static readonly HttpRequestOptionsKey<ExternalCallMetadata> Metadata = new("DigitalBrain.ExternalCall");
    internal static readonly HttpRequestOptionsKey<ExternalCallCompletion> Accounting = new("DigitalBrain.ExternalAccounting");

    public static void SetExternalCall(this HttpRequestMessage request, ExternalCallMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.Operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.InvocationId);
        if (metadata.InvocationId.Length > 128) { throw new ArgumentException("Invocation ID cannot exceed 128 characters.", nameof(metadata)); }
        request.Options.Set(Metadata, metadata);
    }

    public static ExternalCallCompletion? GetExternalAccounting(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.RequestMessage?.Options.TryGetValue(Accounting, out var completion) == true ? completion : null;
    }
}
