using Microsoft.AspNetCore.Components;

namespace TripRadar.MiniApp.Components.Shared;

public partial class FilterPill
{
    [Parameter] public string Label { get; set; } = "";
    [Parameter] public string? Icon { get; set; }
    [Parameter] public bool IsActive { get; set; }
    [Parameter] public bool ShowDropdown { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
}
