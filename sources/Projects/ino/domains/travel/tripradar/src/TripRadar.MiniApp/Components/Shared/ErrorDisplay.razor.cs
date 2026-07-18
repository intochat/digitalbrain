using Microsoft.AspNetCore.Components;

namespace TripRadar.MiniApp.Components.Shared;

public partial class ErrorDisplay
{
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public string Message { get; set; } = "";
    [Parameter] public EventCallback OnRetry { get; set; }
}
