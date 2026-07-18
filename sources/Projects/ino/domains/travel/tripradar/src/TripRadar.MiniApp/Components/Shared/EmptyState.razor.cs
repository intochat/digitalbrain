using Microsoft.AspNetCore.Components;

namespace TripRadar.MiniApp.Components.Shared;

public partial class EmptyState
{
    [Parameter] public string Icon { get; set; } = "search_off";
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public string Message { get; set; } = "";
}
