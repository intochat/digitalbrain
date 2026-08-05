using System.Collections.Immutable;
using System.Globalization;
using DigitalBrain.Mocks;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record MarketSignalClassified(
    string PostId,
    ImmutableArray<string> AssetHints,
    double Relevance) : Synapse;

public sealed record DashboardAnnotateAsked(
    string PostId,
    string Excerpt,
    DateTimeOffset PostAt,
    ImmutableArray<string> AssetHints) : Synapse;

public sealed record SpotSnapshotAsked(ImmutableArray<string> Symbols) : Synapse;

public sealed record SpotQuote(string Symbol, double Price, double Delta5m);

public sealed record SpotSnapshotAnswered(ImmutableArray<SpotQuote> Quotes) : Synapse;

public sealed record ChartPointAppended(
    string Symbol,
    DateTimeOffset At,
    double Price,
    double Delta,
    string SeriesId,
    string PostId) : Synapse;

public sealed record ChartAnnotationAdded(
    string PostId,
    string Excerpt,
    ImmutableArray<string> LinkedSymbols,
    string Description) : Synapse;

// Mock topic router: scores X posts for crypto asset mentions and fans annotate intent.
public sealed class TopicRouter : Neuron, INeuron<XPostObserved>
{
    // Scenario tracks six coins; the acceptance test keeps the set small (two).
    internal static readonly ImmutableArray<string> TrackedCoins = ["BTC", "ETH"];

    public Task HandleAsync(XPostObserved fact, CancellationToken cancellationToken)
    {
        var hints = TrackedCoins
            .Where(symbol => fact.Text.Contains(symbol, StringComparison.OrdinalIgnoreCase))
            .ToImmutableArray();
        var relevance = hints.Length > 0 ? 1.0 : 0.0;
        Emit(new MarketSignalClassified(fact.PostId, hints, relevance));
        if (relevance >= 0.5)
        {
            var excerpt = fact.Text.Length <= 160 ? fact.Text : fact.Text[..160];
            Emit(new DashboardAnnotateAsked(fact.PostId, excerpt, fact.CreatedAt, hints));
        }

        return Task.CompletedTask;
    }
}

// Catalog registration only — ambient market signals need a declared listener to be emit-able.
public sealed class MarketSignalLedger : Neuron, INeuron<MarketSignalClassified>
{
    public Task HandleAsync(MarketSignalClassified fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// Mock spot market answerer: deterministic quotes per symbol for journal-stable proofs.
public sealed class CryptoMarket : Neuron, IAnswers<SpotSnapshotAsked, SpotSnapshotAnswered>
{
    private static readonly Dictionary<string, SpotQuote> Book = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = new("BTC", 98_123.5, 1.2),
        ["ETH"] = new("ETH", 3_450.0, -0.4),
    };

    public Task<SpotSnapshotAnswered?> HandleAsync(
        SpotSnapshotAsked question, CancellationToken cancellationToken)
    {
        var quotes = question.Symbols
            .Select(symbol => Book.TryGetValue(symbol, out var quote)
                ? quote
                : new SpotQuote(symbol, 0, 0))
            .ToImmutableArray();
        return Task.FromResult<SpotSnapshotAnswered?>(new(quotes));
    }
}

public sealed class CryptoDashboardState
{
    public string? PendingPostId { get; set; }
    public string? PendingExcerpt { get; set; }
    public DateTimeOffset PendingPostAt { get; set; }
}

// Mock owner dashboard: annotate ask → spot snapshot ask → chart points + annotation fact.
public sealed class CryptoDashboard : Neuron<CryptoDashboardState>,
    INeuron<DashboardAnnotateAsked>,
    INeuron<SpotSnapshotAnswered>
{
    public Task HandleAsync(DashboardAnnotateAsked fact, CancellationToken cancellationToken)
    {
        State.PendingPostId = fact.PostId;
        State.PendingExcerpt = fact.Excerpt;
        State.PendingPostAt = fact.PostAt;
        Ask<SpotSnapshotAnswered>(new SpotSnapshotAsked(fact.AssetHints));
        return Task.CompletedTask;
    }

    public Task HandleAsync(SpotSnapshotAnswered answer, CancellationToken cancellationToken)
    {
        var postId = State.PendingPostId
            ?? throw new InvalidOperationException("SpotSnapshotAnswered arrived with no pending annotation.");
        var excerpt = State.PendingExcerpt ?? string.Empty;
        var at = State.PendingPostAt;
        var seriesId = Id.Name;
        var moves = new List<string>(answer.Quotes.Length);
        foreach (var quote in answer.Quotes)
        {
            Emit(new ChartPointAppended(
                quote.Symbol, at, quote.Price, quote.Delta5m, seriesId, postId));
            moves.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{quote.Symbol} {quote.Price} (d5m {quote.Delta5m})"));
        }

        var linked = answer.Quotes.Select(quote => quote.Symbol).ToImmutableArray();
        var description = $"{excerpt} -> {string.Join("; ", moves)}";
        Emit(new ChartAnnotationAdded(postId, excerpt, linked, description));
        return Task.CompletedTask;
    }
}

// Shell-side chart renderer stand-in: catalogs ambient chart facts and proves they fan out.
public sealed class ChartRenderer : Neuron, INeuron<ChartPointAppended>, INeuron<ChartAnnotationAdded>
{
    public Task HandleAsync(ChartPointAppended fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ChartAnnotationAdded fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
