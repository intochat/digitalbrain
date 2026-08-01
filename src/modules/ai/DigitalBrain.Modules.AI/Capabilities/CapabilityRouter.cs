using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.AI;

public sealed class CapabilityRouter
{
    public const int DefaultLimit = 8;

    private readonly ExactCapabilityValidator _validator;
    private readonly ICapabilityCandidateSearch? _search;

    public CapabilityRouter(ActiveCapabilityCatalog catalog, ICapabilityCandidateSearch? search = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _validator = new ExactCapabilityValidator(catalog);
        _search = search;
    }

    public CapabilityRouter(ExactCapabilityValidator validator, ICapabilityCandidateSearch? search = null)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _validator = validator;
        _search = search;
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                candidates = [];
            }
            catch (ArgumentException)
            {
                candidates = [];
            }
            catch (TimeoutException)
            {
                candidates = [];
            }
            catch (IOException)
            {
                candidates = [];
            }
            catch (HttpRequestException)
            {
                candidates = [];
            }
        }

        var validated = _validator.Validate(candidates, limit);
        if (validated.Count > 0)
        {
            return validated;
        }

        return _validator.ResolveExactTerms(prompt, limit);
    }
}
