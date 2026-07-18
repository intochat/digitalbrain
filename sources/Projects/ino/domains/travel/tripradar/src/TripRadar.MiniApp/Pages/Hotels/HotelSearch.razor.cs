namespace TripRadar.MiniApp.Pages.Hotels;

public partial class HotelSearch
{
    private bool CanSearch => !string.IsNullOrEmpty(SearchState.Params.Query)
                           && !string.IsNullOrEmpty(SearchState.Params.CheckInDate)
                           && !string.IsNullOrEmpty(SearchState.Params.CheckOutDate);

    private void OnGuestsChanged((int Adults, int Children) g)
    {
        SearchState.Params.Adults = g.Adults;
        SearchState.Params.Children = g.Children;
    }

    private void Search() => Nav.NavigateTo(AppRoutes.HotelResults);
}
