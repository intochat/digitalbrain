using Microsoft.AspNetCore.Components;

namespace TripRadar.MiniApp.Components.Hotels;

public partial class GuestSelector
{
    [Parameter] public int Adults { get; set; } = 2;
    [Parameter] public int Children { get; set; }
    [Parameter] public EventCallback<(int Adults, int Children)> OnChanged { get; set; }

    private bool _open;
    private string Summary
    {
        get
        {
            var count = Adults + Children;
            return string.Format(count > 1 ? L["GuestSelectorGuests"] : L["GuestSelectorGuest"], count);
        }
    }

    private void Toggle() => _open = !_open;

    private async Task Update(int adults, int children)
    {
        Adults = adults;
        Children = children;
        await OnChanged.InvokeAsync((adults, children));
    }
}