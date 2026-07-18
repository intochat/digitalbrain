using Microsoft.AspNetCore.Components;

namespace TripRadar.MiniApp.Components.Shared;

public partial class LoadingSpinner
{
    [Parameter] public string? Message { get; set; }
}
