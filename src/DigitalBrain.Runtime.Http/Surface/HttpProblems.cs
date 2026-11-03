using System.Text.Json;
using DigitalBrain.Modules.Validation;
using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Runtime.Http.Surface;

// Uniform error bodies ({ code, ... }) and a bounded JSON body reader. Error bodies never echo input.
public static class HttpProblems
{
    public const int MaxBodyBytes = 4096;
    public const string RequestInvalid = "REQUEST_INVALID";
    public const string RateLimitedCode = "RATE_LIMITED";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IResult Code(int status, string code) => Results.Json(new { code }, statusCode: status);

    public static IResult BadRequest(string code) => Code(StatusCodes.Status400BadRequest, code);

    public static IResult RateLimited(DateTimeOffset? retryAt) =>
        Results.Json(new { code = RateLimitedCode, retryAt }, statusCode: StatusCodes.Status429TooManyRequests);

    public static IResult Validation(ValidationIssue[] issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        return Results.Json(new { code = RequestInvalid, issues }, statusCode: StatusCodes.Status400BadRequest);
    }

    public static Task<(T? Body, IResult? Failure)> ReadAsync<T>(HttpRequest request, int maxBodyBytes = MaxBodyBytes) where T : class
        => ReadCoreAsync<T>(request, maxBodyBytes, CancellationToken.None);

    public static async Task<(T? Body, IResult? Failure)> ReadValidatedAsync<T>(HttpRequest request,
        IModuleValidator<T> validator, int maxBodyBytes = MaxBodyBytes, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(validator);
        var (body, failure) = await ReadCoreAsync<T>(request, maxBodyBytes, cancellationToken).ConfigureAwait(false);
        if (failure is not null) { return (null, failure); }
        cancellationToken.ThrowIfCancellationRequested();
        request.HttpContext.RequestAborted.ThrowIfCancellationRequested();
        var issues = ModuleValidation.Validate(body!, validator);
        return issues.Length == 0 ? (body, null) : (null, Validation(issues));
    }

    private static async Task<(T? Body, IResult? Failure)> ReadCoreAsync<T>(HttpRequest request,
        int maxBodyBytes, CancellationToken cancellationToken) where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBodyBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(maxBodyBytes, Array.MaxLength);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, request.HttpContext.RequestAborted);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        if (!request.HasJsonContentType())
        {
            return (null, Results.StatusCode(StatusCodes.Status415UnsupportedMediaType));
        }

        var buffer = new byte[maxBodyBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await request.Body.ReadAsync(buffer.AsMemory(length), token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        if (length > maxBodyBytes)
        {
            return (null, Code(StatusCodes.Status413PayloadTooLarge, RequestInvalid));
        }

        token.ThrowIfCancellationRequested();
        try
        {
            return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, length), Json) is { } body ? (body, null) : (null, BadRequest(RequestInvalid));
        }
        catch (JsonException)
        {
            return (null, BadRequest(RequestInvalid));
        }
    }
}
