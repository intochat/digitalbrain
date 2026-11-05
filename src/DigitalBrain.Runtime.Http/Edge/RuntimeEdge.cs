using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Client;
using DigitalBrain.Invocation;
using DigitalBrain.Rules;
using DigitalBrain.Runtime.Http.Surface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Runtime.Http;

/// <summary>
/// Exposes the HTTP and SSE runtime edge endpoints (/brains/{brainId}/runtime/v1/...) for remote clients and sandboxes.
/// </summary>
public static class RuntimeEdge
{
    public const string RefusalHeader = "X-DigitalBrain-Refusal";

    /// <summary>
    /// Registers the runtime invocation dispatcher and options.
    /// </summary>
    public static IServiceCollection AddRuntimeEdge(this IServiceCollection services, Action<RuntimeEdgeOptions>? configure = null)
    {
        services.AddOptions<RuntimeEdgeOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }
        return services;
    }

    /// <summary>
    /// Maps the runtime edge endpoints on the provided endpoint route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapRuntimeEdge(this IEndpointRouteBuilder endpoints)
    {
        var edge = BrainRoutes.SessionGroup(endpoints).AllowAnonymous().ExcludeFromDescription();

        edge.MapPost("runtime/v1/invoke", async Task<IResult> (
            HttpContext http,
            string brainId,
            IRuntimeInvocationDispatcher dispatcher,
            IOptions<RuntimeEdgeOptions> options,
            CancellationToken cancellationToken) =>
        {
            try
            {
                ValidateToken(http, brainId, options.Value);
                var invocation = await http.Request.ReadFromJsonAsync<RuntimeInvocation>(RuntimeProtocol.Json, cancellationToken)
                    ?? throw new ArgumentException("An invocation is required.");
                var value = await dispatcher.DispatchAsync(brainId, invocation, cancellationToken).ConfigureAwait(false);
                return value is { } json ? Results.Json(json, RuntimeProtocol.Json) : Results.NoContent();
            }
            catch (Exception error)
            {
                return Refusal(http, error);
            }
        });

        edge.MapPost("runtime/v1/report", (
            HttpContext http,
            string brainId,
            RuntimeReport report,
            IOptions<RuntimeEdgeOptions> options) =>
        {
            try
            {
                ValidateToken(http, brainId, options.Value);
                if (report.Heartbeat)
                {
                    Activity.Current?.SetTag("digitalbrain.runtime.report", "heartbeat");
                }
                if (report.Subscribe is { } sub)
                {
                    return Results.Ok(new { subscription = "sub-" + Guid.NewGuid().ToString("N")[..8] });
                }
                return Results.Ok();
            }
            catch (Exception error)
            {
                return Refusal(http, error);
            }
        });

        edge.MapGet("runtime/v1/stream", (
            HttpContext http,
            string brainId,
            IOptions<RuntimeEdgeOptions> options,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var claims = ValidateToken(http, brainId, options.Value);
                return TypedResults.ServerSentEvents(StreamSessionEvents(claims, options.Value, cancellationToken));
            }
            catch (Exception error)
            {
                return Refusal(http, error);
            }
        });

        return endpoints;
    }

    private static async IAsyncEnumerable<System.Net.ServerSentEvents.SseItem<string>> StreamSessionEvents(
        SessionClaims claims,
        RuntimeEdgeOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var ready = new ReadyEvent(claims.Session, []);
        yield return new System.Net.ServerSentEvents.SseItem<string>(JsonSerializer.Serialize(ready, RuntimeProtocol.Json), ReadyEvent.Kind);
        if (options.SecretKey is { Length: > 0 } key)
        {
            var refreshedToken = SessionTokens.Issue(key, claims.Brain, claims.Session, options.TokenLifetime);
            yield return new System.Net.ServerSentEvents.SseItem<string>(JsonSerializer.Serialize(new TokenEvent(refreshedToken), RuntimeProtocol.Json), TokenEvent.Kind);
        }
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(10000, cancellationToken).ConfigureAwait(false);
        }
    }

    private static SessionClaims ValidateToken(HttpContext http, string brainId, RuntimeEdgeOptions options)
    {
        var token = Bearer(http);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new UnauthorizedAccessException("The session token is missing.");
        }
        if (options.SecretKey is null || options.SecretKey.Length == 0)
        {
            throw new UnauthorizedAccessException("Runtime edge secret key is not configured.");
        }
        var tokens = new SessionTokens(options.SecretKey);
        var claims = tokens.Validate(token);
        if (claims is null)
        {
            throw new UnauthorizedAccessException("The session token is invalid or expired.");
        }
        if (!string.Equals(claims.Brain, brainId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The session token speaks for another brain.");
        }
        return claims;
    }

    private static string? Bearer(HttpContext http)
        => http.Request.Headers.Authorization.ToString() is { } header && header.StartsWith("Bearer ", StringComparison.Ordinal)
            ? header[7..]
            : null;

    private static IResult Refusal(HttpContext http, Exception error)
    {
        if (error is RevisionConflictException conflict)
        {
            http.Response.Headers[RefusalHeader] = RevisionConflictException.Code;
            return Results.Json(new { message = conflict.Message, currentRevision = conflict.CurrentRevision }, statusCode: StatusCodes.Status409Conflict);
        }
        if (error is RuleViolationException ruleViolation)
        {
            http.Response.Headers[RefusalHeader] = nameof(RuleViolationException);
            return Results.Text(ruleViolation.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        if (error is RemoteInvocationException remoteEx)
        {
            if (remoteEx.Code is not null)
            {
                http.Response.Headers[RefusalHeader] = remoteEx.Code;
            }
            return Results.Text(remoteEx.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        return error switch
        {
            UnauthorizedAccessException => Results.Text(error.Message, statusCode: StatusCodes.Status401Unauthorized),
            ArgumentException or NotSupportedException or JsonException => Results.Text(error.Message, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Text(error.Message, statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }
}
