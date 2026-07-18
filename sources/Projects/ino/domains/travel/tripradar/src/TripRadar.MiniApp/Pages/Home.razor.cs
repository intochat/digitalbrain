namespace TripRadar.MiniApp.Pages;

public partial class Home
{
    protected override void OnInitialized() => Nav.NavigateTo(AppRoutes.Flights, replace: true);
}
