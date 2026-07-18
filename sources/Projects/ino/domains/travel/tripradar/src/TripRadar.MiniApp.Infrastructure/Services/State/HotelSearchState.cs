using TripRadar.MiniApp.Client.Infrastructure.Models.Hotels;

namespace TripRadar.MiniApp.Client.Infrastructure.Services.State;

public sealed class HotelSearchState
{
    public HotelSearchParams Params { get; set; } = new();
    public HotelSearchResult? Results { get; set; }
    public bool IsLoading { get; set; }
    public bool IsLoadingMore { get; set; }
    public string? Error { get; set; }

    public string? NextPageToken => Results?.Pagination?.NextPageToken;

    public event Action? OnChanged;

    public void NotifyChanged() => OnChanged?.Invoke();

    public void SetLoading()
    {
        IsLoading = true;
        Error = null;
        OnChanged?.Invoke();
    }

    public void SetResults(HotelSearchResult results)
    {
        Results = results;
        IsLoading = false;
        Error = null;
        OnChanged?.Invoke();
    }

    public void AppendResults(HotelSearchResult moreResults)
    {
        if (Results is null)
        {
            SetResults(moreResults);
            return;
        }

        var merged = new List<HotelProperty>(Results.Properties ?? []);
        if (moreResults.Properties is { Count: > 0 })
            merged.AddRange(moreResults.Properties);

        Results = Results with
        {
            Properties = merged,
            Pagination = moreResults.Pagination
        };
        IsLoadingMore = false;
        OnChanged?.Invoke();
    }

    public void SetError(string error)
    {
        Error = error;
        IsLoading = false;
        IsLoadingMore = false;
        OnChanged?.Invoke();
    }

    public void Reset()
    {
        Params = new();
        Results = null;
        IsLoading = false;
        IsLoadingMore = false;
        Error = null;
        OnChanged?.Invoke();
    }
}
