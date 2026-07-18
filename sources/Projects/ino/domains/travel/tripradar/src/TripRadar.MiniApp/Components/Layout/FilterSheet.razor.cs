using Microsoft.AspNetCore.Components;

namespace TripRadar.MiniApp.Components.Layout;

public partial class FilterSheet
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public string Title { get; set; } = "Filters";
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment? FooterContent { get; set; }

    private async Task Close() => await OnClose.InvokeAsync();
}
