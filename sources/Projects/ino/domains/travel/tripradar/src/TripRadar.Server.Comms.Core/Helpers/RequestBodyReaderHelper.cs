using Microsoft.AspNetCore.Http;
using System.Text;

namespace TripRadar.Server.Comms.Core.Helpers;

public static class RequestBodyReaderHelper
{
    public static async Task<(bool IsTooLarge, string Body)> TryReadAsStringAsync(
        HttpRequest request,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8 * 1024];
        await using var ms = new MemoryStream();
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (ms.Length + read > maxBytes)
            {
                return (true, string.Empty);
            }

            await ms.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var body = Encoding.UTF8.GetString(ms.ToArray());
        return (false, body);
    }
}