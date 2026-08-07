using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.AI;

public sealed partial class CapabilityRouter
{
    public const int DefaultLimit = 8;

    private readonly ExactCapabilityValidator _validator;
    private readonly ICapabilityCandidateSearch? _search;
    private readonly ILogger _logger;

    public CapabilityRouter(
        ActiveCapabilityCatalog catalog,
        ICapabilityCandidateSearch? search = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _validator = new ExactCapabilityValidator(catalog);
        _search = search;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<IReadOnlyList<ValidatedCapability>> SelectAsync(
        OwnerId owner,
        string prompt,
        CancellationToken cancellationToken,
        int limit = DefaultLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<CapabilityCandidate> candidates = [];
        if (_search is not null)
        {
            try
            {
                candidates = await _search.SearchAsync(owner, prompt, limit, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception failure) when (failure
                is InvalidOperationException
                or ArgumentException
                or TimeoutException
                or IOException
                or HttpRequestException)
            {
                LogSearchUnavailable(_logger, failure, owner.Value);
            }
        }

        var validated = _validator.Validate(candidates, limit);
        if (validated.Count > 0)
        {
            return validated;
        }

        return _validator.ResolveExactTerms(prompt, limit);
    }

    [LoggerMessage(
        LogLevel.Warning,
        "Capability candidate search is unavailable for owner {Owner}; falling back to exact catalog terms only.")]
    private static partial void LogSearchUnavailable(ILogger logger, Exception failure, string owner);
}
