using Microsoft.AspNetCore.Components;

namespace TripRadar.MiniApp.Components.Shared;

public partial class BottomSheet
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public string? Icon { get; set; }
    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter] public string? BadgeText { get; set; }
    [Parameter] public string? BadgeCss { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public string? CloseText { get; set; }

    private async Task Close() => await OnClose.InvokeAsync();
}
