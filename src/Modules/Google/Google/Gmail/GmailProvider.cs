using Google.Apis.Gmail.v1;

namespace DigitalBrain.Google;

internal sealed class GmailProvider
{
    private readonly GmailService _service;

    public GmailProvider(GmailService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    public GmailService Service => _service;

    public async Task<IReadOnlyList<GmailMessageHeader>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        cancellationToken.ThrowIfCancellationRequested();

        var capped = (int)SdkCatalogAdmission.BoundMaxResults(maxResults);
        var list = _service.Users.Messages.List("me");
        list.Q = query;
        list.MaxResults = capped;
        var listed = await list.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (listed.Messages is not { Count: > 0 })
        {
            return [];
        }

        var headers = new List<GmailMessageHeader>(Math.Min(listed.Messages.Count, capped));
        foreach (var stub in listed.Messages.Take(capped))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(stub.Id))
            {
                continue;
            }

            var get = _service.Users.Messages.Get("me", stub.Id);
            get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            var message = await get.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            headers.Add(GmailMessageMapper.ToHeader(message));
        }

        return headers;
    }

    public async Task<GmailMessage> GetMessageAsync(string messageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        cancellationToken.ThrowIfCancellationRequested();

        var get = _service.Users.Messages.Get("me", messageId);
        get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        var message = await get.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return GmailMessageMapper.ToMessage(message, requestedId: messageId);
    }
}
