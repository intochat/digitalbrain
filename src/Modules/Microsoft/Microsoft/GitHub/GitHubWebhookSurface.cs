using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubWebhookSurface(GitHubWebhookIngress ingress) : IHttpSurface
{
    private const int MaxBodyBytes = 1_048_576;

    public void Map(IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.Equals(new PathString("/integrations/github/webhook")))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            await AcceptAsync(context).ConfigureAwait(false);
        });
    }

    private async Task AcceptAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.Headers.Allow = "POST";
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType)
            || !string.Equals(contentType.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        if (context.Request.ContentLength > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        // WaitAsync enforces the deadline even for a transport ignoring cancellation.
        // Such a pending read must retain its own buffer, never an array returned to a pool.
        var buffer = new byte[(int)Math.Min(MaxBodyBytes + 1L, 81920L)];
        try
        {
            using var body = new MemoryStream();
            while (true)
            {
                var read = await context.Request.Body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length,
                    MaxBodyBytes - body.Length + 1)), budget.Token).AsTask().WaitAsync(budget.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (body.Length + read > MaxBodyBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }

                body.Write(buffer, 0, read);
            }

            var headers = context.Request.Headers.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Select(value => value ?? string.Empty).ToArray(),
                StringComparer.OrdinalIgnoreCase);
            var acceptance = await ingress.HandleAsync(body.ToArray(), headers, budget.Token).WaitAsync(budget.Token).ConfigureAwait(false);
            context.Response.StatusCode = acceptance switch
            {
                GitHubWebhookAcceptance.Accepted => StatusCodes.Status202Accepted,
                GitHubWebhookAcceptance.Duplicate => StatusCodes.Status200OK,
                GitHubWebhookAcceptance.Ignored => StatusCodes.Status204NoContent,
                GitHubWebhookAcceptance.BadRequest => StatusCodes.Status400BadRequest,
                GitHubWebhookAcceptance.Unauthorized => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status503ServiceUnavailable,
            };
        }
        catch (BadHttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        catch (Exception)
        {
            // Payloads, credentials and provider exceptions must never become an HTTP response.
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
    }
}
