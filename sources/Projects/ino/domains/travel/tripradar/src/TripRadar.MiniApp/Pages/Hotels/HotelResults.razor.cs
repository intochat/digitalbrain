namespace TripRadar.MiniApp.Pages.Hotels;

public partial class HotelResults
{
    protected override async Task OnInitializedAsync()
    {
        SearchState.OnChanged += StateHasChanged;
        await UserPrefs.LoadAsync();
        await LoadResults();
    }

    private async Task LoadResults()
    {
        SearchState.SetLoading();
        try
        {
            var result = await HotelApi.SearchAsync(SearchState.Params);
            if (result is not null)
                SearchState.SetResults(result);
            else
                SearchState.SetError("No response from server");
        }
        catch (Exception ex)
        {
            SearchState.SetError(ex.Message);
        }
    }

    private async Task LoadMore()
    {
        if (SearchState.NextPageToken is not { } token) return;

        SearchState.IsLoadingMore = true;
        SearchState.NotifyChanged();
        try
        {
            var result = await HotelApi.LoadMoreAsync(SearchState.Params, token);
            if (result is not null)
                SearchState.AppendResults(result);
            else
            {
                SearchState.IsLoadingMore = false;
                SearchState.NotifyChanged();
            }
        }
        catch (Exception ex)
        {
            SearchState.SetError(ex.Message);
        }
    }

    public void Dispose() => SearchState.OnChanged -= StateHasChanged;
}
