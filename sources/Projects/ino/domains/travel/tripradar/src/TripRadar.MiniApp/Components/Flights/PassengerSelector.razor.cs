using Microsoft.AspNetCore.Components;

namespace TripRadar.MiniApp.Components.Flights;

public partial class PassengerSelector
{
    [Parameter] public int Adults { get; set; } = 1;
    [Parameter] public int Children { get; set; }
    [Parameter] public int Infants { get; set; }
    [Parameter] public EventCallback<(int Adults, int Children, int Infants)> OnChanged { get; set; }

    private bool _open;
    private string Summary { get { var total = Adults + Children + Infants; return string.Format(total > 1 ? L["PassengersTravelers"] : L["PassengersTraveler"], total); } }

    private void Toggle() => _open = !_open;

    private async Task Update(int adults, int children, int infants)
    {
        Adults = adults;
        Children = children;
        Infants = infants;
        await OnChanged.InvokeAsync((adults, children, infants));
    }
}